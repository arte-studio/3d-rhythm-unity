using System;
using System.Collections.Generic;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Net;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>ノーツ用 LED の見た目の設定．色は仕様書（docs/SPECIFICATION.md 7.1），速さなどは旧版の値．</summary>
    [Serializable]
    public sealed class NoteLedStyle
    {
        /// <summary>案内（タイミング案内）は青の点滅．</summary>
        public byte[] openColor = { 0, 0, 255 };
        /// <summary>点滅の周期．ノーツ時刻にちょうど点く位相にそろえる．</summary>
        public float openBlinkPeriodSec = 0.5f;
        public byte[] perfectColor = { 0, 255, 0 };     // 緑
        public byte[] goodColor = { 255, 255, 0 };      // 黄
        public byte[] missColor = { 255, 0, 0 };        // 赤

        /// <summary>Perfect のとき，むぎゅっとに虹色の回転を出す時間（旧版 PerfectPerformance．旧版は画面上の 3D オブジェクトにだけ出ていた）．</summary>
        public float perfectRainbowSec = 0.2f;
        /// <summary>虹色が回る速さ（周/秒）．</summary>
        public float perfectRainbowSpeed = 0.8f;

        /// <summary>
        /// 判定色を出してから消えるまで（仕様書 4.7「判定 → フェードアウト」）．旧版は判定色のまま次のノーツまで残っていた
        /// （0.5 秒で黒に戻す処理が画面上の 3D オブジェクトにしか効いていなかった）．
        /// </summary>
        public float judgedFadeSec = 0.5f;

        /// <summary>判定のあとにつながるノーツ用 LED を流れる光（仕様書 7.1.1，旧 RunTouchJudgeTrail）．</summary>
        public float trailDurationSec = 3.0f;
        public float trailSpeedLedsPerSec = 18.0f;
        public float trailSigmaLeds = 4.0f;

        /// <summary>
        /// 曲の間，全モジュールのつながるノーツ用 LED を 1 個ずつ色相を変えながら塗っていく（旧 ConnectLEDPerform）．
        /// 旧版は全モジュールのつもりで書かれていたが，LED にはレーン 0 にしか出ておらず（SetConColor(0, ...)），
        /// 1 フレームに 1 個進むのでフレームレートで速さが変わっていた．曲が終わったら消す（旧版の意図）．
        /// </summary>
        public bool connectSweep = true;
        /// <summary>塗る位置が進む速さ（個/秒）．旧版は 1 フレームに 1 個（60fps なら 60）．</summary>
        public float connectSweepLedsPerSec = 60f;
        public float connectHueMinDeg = 210f;
        public float connectHueMaxDeg = 296f;
        public float connectSaturation = 1f;
        public float connectValue = 1f;
        /// <summary>色相が変わる速さ（色相 0〜1 を 1 として 1 秒あたり）．</summary>
        public float connectSpeed = 0.5f;
    }

    /// <summary>
    /// ノーツ用 LED（むぎゅっと 8×8 と，つながるノーツ用 30 個）を描く．仕様書（docs/SPECIFICATION.md 4.7，7.1）の意図に沿い，
    /// 色や速さは旧版 GameManager / NoteLeds の値を使う:
    /// - 案内が始まった未判定のノーツ（TouchJudge.IsOpen）のレーンのむぎゅっとを青で点滅させる
    /// - 判定したらむぎゅっとを判定色（Perfect は虹色の回転のあと）にし，judgedFadeSec で消す
    /// - 判定したらそのレーンのつながるノーツ用 LED に判定色の光を流す（3 秒で消える）
    /// - 曲の間，つながるノーツ用 LED を色相を変えながら塗っていく（connectSweep）．流れる光が出ている間はそちらが上
    /// つながるノーツ用のテープは色の並びが違うので R と G を入れ替えて書く（NoteLayout.ConnectSwapRG）．
    /// Unity に依存しないので，時刻は呼び出し側が渡す（songTime = 曲の時刻，realtime = 実時間）．
    /// </summary>
    public sealed class NoteLedRenderer
    {
        const int Channels = 3;

        readonly NoteLayout layout;
        readonly NoteLedStyle style;
        readonly int[] lanes;               // 実機にあるレーン
        readonly byte[][] mugu;             // [lane] むぎゅっとの LED × 3 バイト（行 × 列の順）
        readonly byte[][] connect;          // [lane] 30 × 3 バイト（R と G を入れ替える前）
        readonly double[] trailStart;       // [lane] NaN = 流れていない
        readonly byte[][] trailColor;
        readonly double[] judgedAt;         // [lane] NaN = 判定色を出していない
        readonly byte[][] judgedColor;
        readonly bool[] judgedPerfect;
        readonly int laneCount;
        int openCursor;
        double sweepHue, sweepSteps;
        int sweepDirection = 1, sweepIndex;

        public NoteLedRenderer(NoteLayout layout, NoteLedStyle style = null)
        {
            this.layout = layout;
            this.style = style ?? new NoteLedStyle();
            lanes = new List<int>(layout.Lanes).ToArray();
            laneCount = 0;
            foreach (int l in lanes) laneCount = Math.Max(laneCount, l + 1);
            mugu = new byte[laneCount][];
            connect = new byte[laneCount][];
            trailColor = new byte[laneCount][];
            trailStart = new double[laneCount];
            judgedColor = new byte[laneCount][];
            judgedAt = new double[laneCount];
            judgedPerfect = new bool[laneCount];
            for (int l = 0; l < laneCount; l++)
            {
                mugu[l] = new byte[layout.MuguLeds * Channels];
                connect[l] = new byte[layout.ConnectLeds * Channels];
                trailColor[l] = new byte[Channels];
                judgedColor[l] = new byte[Channels];
            }
            Reset();
        }

        public NoteLedStyle Style => style;

        /// <summary>曲の始めに呼ぶ（全部消える）．</summary>
        public void Reset()
        {
            for (int l = 0; l < laneCount; l++)
            {
                Array.Clear(mugu[l], 0, mugu[l].Length);
                Array.Clear(connect[l], 0, connect[l].Length);
                trailStart[l] = double.NaN;
                judgedAt[l] = double.NaN;
            }
            openCursor = 0;
            sweepSteps = 0;
            sweepHue = style.connectHueMinDeg / 360.0;
            sweepDirection = 1;
            sweepIndex = 0;
        }

        /// <summary>判定が出たときに呼ぶ．realtime は流れる光の起点．</summary>
        public void OnJudged(in JudgeEvent e, double realtime)
        {
            if (!Has(e.Lane)) return;
            byte[] c = e.Result == JudgeResult.Perfect ? style.perfectColor : e.Result == JudgeResult.Good ? style.goodColor : style.missColor;
            Array.Copy(c, judgedColor[e.Lane], Channels);
            judgedAt[e.Lane] = realtime;
            judgedPerfect[e.Lane] = e.Result == JudgeResult.Perfect;
            Array.Copy(c, trailColor[e.Lane], Channels);
            trailStart[e.Lane] = realtime;
        }

        /// <summary>曲が終わったときに呼ぶ（つながるノーツ用 LED の色相の演出を消す．流れる光とむぎゅっとはそのまま消えていく）．</summary>
        public void OnSongEnded()
        {
            for (int l = 0; l < laneCount; l++)
                if (double.IsNaN(trailStart[l])) Array.Clear(connect[l], 0, connect[l].Length);
        }

        /// <summary>
        /// 1 フレーム分を進めて frame のノーツ用ピンに書く．playing は曲の再生中か（色相の演出に使う），
        /// deltaTime は前のフレームからの実時間（秒）．
        /// </summary>
        public void Render(Chart chart, TouchJudge judge, double songTime, double realtime, float deltaTime, bool playing, LedFrame frame)
        {
            for (int l = 0; l < laneCount; l++)
                if (!double.IsNaN(judgedAt[l])) PaintJudged(l, realtime);
            if (chart != null && judge != null) PaintOpenNotes(chart, judge, songTime);
            if (playing && style.connectSweep) PaintSweep(deltaTime);
            for (int l = 0; l < laneCount; l++)
                if (!double.IsNaN(trailStart[l])) PaintTrail(l, realtime);
            Write(frame);
        }

        void PaintJudged(int lane, double realtime)
        {
            float elapsed = (float)(realtime - judgedAt[lane]);
            float fade = Math.Max(style.judgedFadeSec, judgedPerfect[lane] ? style.perfectRainbowSec : 0f);
            if (elapsed >= fade)
            {
                Array.Clear(mugu[lane], 0, mugu[lane].Length);
                judgedAt[lane] = double.NaN;
                return;
            }
            if (judgedPerfect[lane] && elapsed < style.perfectRainbowSec)
            {
                PaintRainbow(mugu[lane], elapsed);
                return;
            }
            float k = 1f - Clamp01(elapsed / fade);
            var c = judgedColor[lane];
            Fill(mugu[lane], ToByte(c[0] / 255f * k), ToByte(c[1] / 255f * k), ToByte(c[2] / 255f * k));
        }

        // 旧版 RunPerfectRainbowAnimation と同じ: 中心からの角度で色相を付け，時間で回す
        void PaintRainbow(byte[] dest, float elapsed)
        {
            int rows = layout.MuguRows, cols = layout.MuguColumns;
            float cx = (cols - 1) / 2f, cy = (rows - 1) / 2f;
            float offset = elapsed * style.perfectRainbowSpeed;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float angle = (float)Math.Atan2(r - cy, c - cx);
                    float h = (angle / (2f * (float)Math.PI) + 0.5f + offset) % 1f;
                    HsvToRgb(h, 1f, 1f, out float rr, out float gg, out float bb);
                    int i = (r * cols + c) * Channels;
                    dest[i] = ToByte(rr);
                    dest[i + 1] = ToByte(gg);
                    dest[i + 2] = ToByte(bb);
                }
            }
        }

        static void Fill(byte[] dest, byte r, byte g, byte b)
        {
            for (int i = 0; i < dest.Length; i += Channels)
            {
                dest[i] = r;
                dest[i + 1] = g;
                dest[i + 2] = b;
            }
        }

        bool Has(int lane) => lane >= 0 && lane < laneCount && layout.HasLane(lane);

        void PaintOpenNotes(Chart chart, TouchJudge judge, double songTime)
        {
            var notes = chart.Notes;
            double lead = judge.Rules.SpawnLeadSec;
            // 判定が済んだ（またはタッチでない）先頭のノーツは飛ばす（ノーツは時刻順）
            while (openCursor < notes.Count && (notes[openCursor].Type != NoteType.Touch || judge.ResultOf(openCursor) != null)) openCursor++;
            for (int i = openCursor; i < notes.Count && notes[i].Time - lead <= songTime; i++)
            {
                if (!judge.IsOpen(i, songTime) || !Has(notes[i].Lane)) continue;
                // ノーツ時刻にちょうど点く位相で点滅（点いている時間は周期の半分）
                double phase = (songTime - notes[i].Time) / style.openBlinkPeriodSec;
                bool on = style.openBlinkPeriodSec <= 0 || phase - Math.Floor(phase) < 0.5;
                var c = style.openColor;
                if (on) Fill(mugu[notes[i].Lane], c[0], c[1], c[2]);
                else Fill(mugu[notes[i].Lane], 0, 0, 0);
            }
        }

        // 旧 RunTouchJudgeTrail と同じ計算（float，Color → Color32 の丸めも同じ）
        void PaintTrail(int lane, double realtime)
        {
            var dest = connect[lane];
            float elapsed = (float)(realtime - trailStart[lane]);
            if (elapsed >= style.trailDurationSec)
            {
                Array.Clear(dest, 0, dest.Length);
                trailStart[lane] = double.NaN;
                return;
            }
            int n = layout.ConnectLeds;
            float fadeOut = 1f - Clamp01(elapsed / style.trailDurationSec);
            float headPos = Repeat(elapsed * style.trailSpeedLedsPerSec, n);
            float sigma = style.trailSigmaLeds;
            var c = trailColor[lane];
            for (int i = 0; i < n; i++)
            {
                float distance = Math.Abs(i - headPos);
                distance = Math.Min(distance, n - distance);
                float spatial = (float)Math.Exp(-(distance * distance) / (2f * sigma * sigma));
                float intensity = fadeOut * spatial;
                for (int ch = 0; ch < Channels; ch++) dest[i * Channels + ch] = ToByte(c[ch] / 255f * intensity);
            }
        }

        // 旧 ConnectLEDPerform と同じ色の動き．塗る位置は時間で進め，全レーンに塗る
        void PaintSweep(float deltaTime)
        {
            double min = style.connectHueMinDeg / 360.0, max = style.connectHueMaxDeg / 360.0;
            sweepHue += deltaTime * style.connectSpeed * sweepDirection;
            if (sweepHue >= max)
            {
                sweepHue = max;
                sweepDirection = -1;
            }
            else if (sweepHue <= min)
            {
                sweepHue = min;
                sweepDirection = 1;
            }
            HsvToRgb((float)sweepHue, style.connectSaturation, style.connectValue, out float r, out float g, out float b);
            byte br = ToByte(r), bg = ToByte(g), bb = ToByte(b);
            sweepSteps += deltaTime * style.connectSweepLedsPerSec;
            int steps = Math.Min((int)sweepSteps, layout.ConnectLeds);
            sweepSteps -= (int)sweepSteps;
            for (int k = 0; k < steps; k++)
            {
                foreach (int lane in lanes)
                {
                    var dest = connect[lane];
                    dest[sweepIndex * Channels] = br;
                    dest[sweepIndex * Channels + 1] = bg;
                    dest[sweepIndex * Channels + 2] = bb;
                }
                sweepIndex = (sweepIndex + 1) % layout.ConnectLeds;
            }
        }

        void Write(LedFrame frame)
        {
            int notePin = layout.NoteOutputPin;
            for (int d = 0; d < frame.DeviceCount; d++) frame.Pin(d, notePin).Clear();
            foreach (int lane in lanes)
            {
                if (!layout.TryGetModule(lane, out var m) || m.device >= frame.DeviceCount) continue;
                var pin = frame.Pin(m.device, notePin);
                int muguStart = layout.MuguLedIndex(m.module, 0, 0) * Channels;
                int conStart = layout.ConnectLedIndex(m.module, 0) * Channels;
                if (conStart + layout.ConnectLeds * Channels > pin.Length) continue;
                mugu[lane].AsSpan().CopyTo(pin.Slice(muguStart));
                var con = connect[lane];
                bool swap = layout.ConnectSwapRG;
                for (int i = 0; i < layout.ConnectLeds; i++)
                {
                    byte r = con[i * Channels], g = con[i * Channels + 1];
                    pin[conStart + i * Channels] = swap ? g : r;
                    pin[conStart + i * Channels + 1] = swap ? r : g;
                    pin[conStart + i * Channels + 2] = con[i * Channels + 2];
                }
            }
        }

        // ---- Unity の関数と同じ計算 ----

        static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        static float Repeat(float t, float length) => Math.Min(Math.Max(t - (float)Math.Floor(t / length) * length, 0f), length);

        /// <summary>Color → Color32（Mathf.Round(Clamp01(v) * 255)．偶数への丸め）．</summary>
        static byte ToByte(float v) => (byte)Math.Round(Clamp01(v) * 255f);

        /// <summary>Color.HSVToRGB(h, s, v) と同じ．</summary>
        public static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            if (s == 0f)
            {
                r = g = b = v;
                return;
            }
            if (v == 0f)
            {
                r = g = b = 0f;
                return;
            }
            float h6 = h * 6f;
            int sector = (int)Math.Floor(h6);
            float f = h6 - sector;
            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));
            switch (sector + 1)
            {
                case 0: r = v; g = p; b = q; break;
                case 1: r = v; g = t; b = p; break;
                case 2: r = q; g = v; b = p; break;
                case 3: r = p; g = v; b = t; break;
                case 4: r = p; g = q; b = v; break;
                case 5: r = t; g = p; b = v; break;
                case 6: r = v; g = p; b = q; break;
                default: r = v; g = t; b = p; break;
            }
            r = Clamp01(r);
            g = Clamp01(g);
            b = Clamp01(b);
        }
    }
}
