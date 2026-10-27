using System;
using System.Collections.Generic;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Net;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>ノーツ用 LED の見た目の設定．既定値は旧版（GameManager と古代祭のシーン設定）．</summary>
    [Serializable]
    public sealed class NoteLedStyle
    {
        // 色は旧版の Unity の Color を Color32 にした値（Color.yellow は (1, 0.92, 0.016)）
        public byte[] openColor = { 255, 235, 4 };      // 判定の受付中（黄）
        public byte[] perfectColor = { 255, 0, 255 };   // magenta
        public byte[] goodColor = { 255, 255, 255 };    // white
        public byte[] missColor = { 0, 255, 255 };      // cyan

        /// <summary>判定のあとにつながるノーツ用 LED を流れる光（旧 RunTouchJudgeTrail）．</summary>
        public float trailDurationSec = 3.0f;
        public float trailSpeedLedsPerSec = 18.0f;
        public float trailSigmaLeds = 4.0f;

        /// <summary>
        /// 曲の間，レーン 0 のつながるノーツ用 LED を 1 フレームに 1 個ずつ色相を変えながら塗る（旧 ConnectLEDPerform）．
        /// 旧版は全モジュールのつもりで書かれていたが，LED にはレーン 0 にしか出ていなかった（SetConColor(0, ...)）．
        /// 実機の動作を変えないため既定は旧版どおり．
        /// </summary>
        public bool legacyLane0ConnectSweep = true;
        public float connectHueMinDeg = 210f;
        public float connectHueMaxDeg = 296f;
        public float connectSaturation = 1f;
        public float connectValue = 1f;
        /// <summary>色相が変わる速さ（色相 0〜1 を 1 として 1 秒あたり）．</summary>
        public float connectSpeed = 0.5f;
    }

    /// <summary>
    /// ノーツ用 LED（むぎゅっと 8×8 と，つながるノーツ用 30 個）を描く．旧版 GameManager / NoteLeds の LED の動きと同じ:
    /// - 判定の受付が始まった未判定のノーツ（TouchJudge.IsOpen）のレーンのむぎゅっとを黄色にする
    /// - 判定したらむぎゅっとを Perfect = magenta，Good = white，Miss = cyan にする．次のノーツが黄色にするまでそのまま
    /// - 判定したらそのレーンのつながるノーツ用 LED に判定色の光を流す（3 秒で消える）
    /// - legacyLane0ConnectSweep（上の説明）
    /// つながるノーツ用のテープは色の並びが違うので R と G を入れ替えて書く（NoteLayout.ConnectSwapRG）．
    /// Unity に依存しないので，時刻は呼び出し側が渡す（songTime = 曲の時刻，realtime = 実時間）．
    /// </summary>
    public sealed class NoteLedRenderer
    {
        const int Channels = 3;

        readonly NoteLayout layout;
        readonly NoteLedStyle style;
        readonly int[] lanes;               // 実機にあるレーン
        readonly byte[][] mugu;             // [lane] 3 バイト（むぎゅっと全体が 1 色）
        readonly byte[][] connect;          // [lane] 30 × 3 バイト（R と G を入れ替える前）
        readonly double[] trailStart;       // [lane] NaN = 流れていない
        readonly byte[][] trailColor;
        readonly int laneCount;
        int openCursor;
        double sweepHue;
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
            for (int l = 0; l < laneCount; l++)
            {
                mugu[l] = new byte[Channels];
                connect[l] = new byte[layout.ConnectLeds * Channels];
                trailColor[l] = new byte[Channels];
            }
            Reset();
        }

        public NoteLedStyle Style => style;

        /// <summary>曲の始めに呼ぶ（全部消える）．</summary>
        public void Reset()
        {
            for (int l = 0; l < laneCount; l++)
            {
                Array.Clear(mugu[l], 0, Channels);
                Array.Clear(connect[l], 0, connect[l].Length);
                trailStart[l] = double.NaN;
            }
            openCursor = 0;
            sweepHue = style.connectHueMinDeg / 360.0;
            sweepDirection = 1;
            sweepIndex = 0;
        }

        /// <summary>判定が出たときに呼ぶ．realtime は流れる光の起点．</summary>
        public void OnJudged(in JudgeEvent e, double realtime)
        {
            if (!Has(e.Lane)) return;
            byte[] c = e.Result == JudgeResult.Perfect ? style.perfectColor : e.Result == JudgeResult.Good ? style.goodColor : style.missColor;
            Array.Copy(c, mugu[e.Lane], Channels);
            Array.Copy(c, trailColor[e.Lane], Channels);
            trailStart[e.Lane] = realtime;
        }

        /// <summary>
        /// 1 フレーム分を進めて frame のノーツ用ピンに書く．playing は曲の再生中か（レーン 0 の色相の動きに使う），
        /// deltaTime は前のフレームからの実時間（秒）．
        /// </summary>
        public void Render(Chart chart, TouchJudge judge, double songTime, double realtime, float deltaTime, bool playing, LedFrame frame)
        {
            if (chart != null && judge != null) PaintOpenNotes(chart, judge, songTime);
            for (int l = 0; l < laneCount; l++)
                if (!double.IsNaN(trailStart[l])) PaintTrail(l, realtime);
            if (playing && style.legacyLane0ConnectSweep && Has(0)) PaintSweep(deltaTime);
            Write(frame);
        }

        bool Has(int lane) => lane >= 0 && lane < laneCount && layout.HasLane(lane);

        void PaintOpenNotes(Chart chart, TouchJudge judge, double songTime)
        {
            var notes = chart.Notes;
            double lead = judge.Rules.GoodSec;
            // 判定が済んだ（またはタッチでない）先頭のノーツは飛ばす（ノーツは時刻順）
            while (openCursor < notes.Count && (notes[openCursor].Type != NoteType.Touch || judge.ResultOf(openCursor) != null)) openCursor++;
            for (int i = openCursor; i < notes.Count && notes[i].Time - lead <= songTime; i++)
            {
                if (!judge.IsOpen(i, songTime) || !Has(notes[i].Lane)) continue;
                Array.Copy(style.openColor, mugu[notes[i].Lane], Channels);
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

        // 旧 ConnectLEDPerform と同じ（1 フレームに 1 個ずつ）
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
            var dest = connect[0];
            dest[sweepIndex * Channels] = ToByte(r);
            dest[sweepIndex * Channels + 1] = ToByte(g);
            dest[sweepIndex * Channels + 2] = ToByte(b);
            sweepIndex = (sweepIndex + 1) % layout.ConnectLeds;
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
                var c = mugu[lane];
                for (int i = 0; i < layout.MuguLeds; i++)
                {
                    pin[muguStart + i * Channels] = c[0];
                    pin[muguStart + i * Channels + 1] = c[1];
                    pin[muguStart + i * Channels + 2] = c[2];
                }
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
