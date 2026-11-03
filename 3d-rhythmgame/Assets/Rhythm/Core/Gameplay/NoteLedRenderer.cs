using System;
using System.Collections.Generic;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Net;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>案内（ノーツが来ることとタイミング）の見せ方．</summary>
    public enum NoteApproach
    {
        /// <summary>
        /// 拍で案内する（2026-11-03 ユーザー決定）: ノーツ時刻の approachBeats 拍前から，
        /// むぎゅっとの外周の四角が中心へ縮み（中心の 2×2 は薄く点けておく．閉じた瞬間がノーツ時刻），
        /// つながるノーツ用テープの遠い端から光の粒が走ってむぎゅっとの側の端に着き，1 拍ごとに光が強まる（最後の 1 拍は色を変える）．
        /// すぐ前のノーツが隣のレーン（テープでつながっているむぎゅっと）なら，光の粒はその間のテープを前のむぎゅっとから次のむぎゅっとへ流れる．
        /// </summary>
        Beats,
        /// <summary>旧版: ノーツ時刻の SpawnLeadSec 前から青の点滅（openBlinkPeriodSec．ノーツ時刻にちょうど点く位相）．何回目かを数えないと分かりにくかった</summary>
        Blink,
    }

    /// <summary>ノーツ用 LED の見た目の設定．色は旧仕様書（docs/legacy/SPECIFICATION-v1.md 7.1），速さなどは旧版の値（docs/SPECIFICATION.md 6 章）．</summary>
    [Serializable]
    public sealed class NoteLedStyle
    {
        public NoteApproach approach = NoteApproach.Beats;
        /// <summary>案内の色（青）．</summary>
        public byte[] openColor = { 0, 0, 255 };
        /// <summary>Blink: 点滅の周期．ノーツ時刻にちょうど点く位相にそろえる．</summary>
        public float openBlinkPeriodSec = 0.5f;
        /// <summary>Beats: 何拍前から案内するか．</summary>
        public float approachBeats = 3f;
        /// <summary>
        /// Beats: 同じ流れのすぐ前のノーツとの間が approachBeats より短いときは，むぎゅっとの案内をこの拍数前から出す
        /// （前のノーツの案内と重なって分かりにくかった．2026-11-03 ユーザー決定）．
        /// テープを走る光の粒はいつも approachBeats 前から同じ速さで走る（速さが変わると分かりにくい．同）．
        /// </summary>
        public float approachShortBeats = 2f;
        /// <summary>
        /// Beats: ノーツ時刻ちょうどに，そのむぎゅっとだけを最後の 1 拍の色でいっぱいに光らせ，この秒数で戻す（「今」の合図．0 で無し）．
        /// 全体は光らせない（2026-11-03 ユーザー決定）．
        /// </summary>
        public float approachNowSec = 0.12f;
        /// <summary>Beats: 最後の 1 拍の色（水色）．</summary>
        public byte[] approachLastColor = { 0, 200, 255 };
        /// <summary>
        /// Beats: 2 拍前・3 拍前の色．拍を数えやすいよう 1 拍ごとに違う色（紫 → 青 → 水色．2026-11-03 ユーザー: 青 → 青は別の色どうしの方がいい）．
        /// 判定の色（緑・黄・赤）と白は使わない．
        /// </summary>
        public byte[] approachColor2 = { 0, 0, 255 };
        public byte[] approachColor3 = { 170, 0, 255 };
        /// <summary>Beats: 中心の 2×2（閉じる先）の明るさ（0〜1）．</summary>
        public float approachTargetLevel = 0.25f;
        /// <summary>Beats: 拍で強まった光が戻る速さ（大きいほど早く戻る）と，戻った先の明るさ（0〜1）．</summary>
        public float approachPulseDecay = 4f;
        public float approachPulseMin = 0.55f;
        /// <summary>Beats: テープを走る光の粒の尾の長さ（個）．</summary>
        public float approachTailLeds = 5f;
        /// <summary>
        /// Beats: テープの光の粒は「次のノーツがどこに出るか」の道しるべ（叩く時刻は示さない．2026-11-03 ユーザー決定）．
        /// 同じ流れの前のノーツの時刻のこの拍数前から，次のノーツが判定されるまで，次のノーツの両隣のテープを次のノーツへ向かって流れ続ける
        /// （両側から流すのは，違う方を見ていても気付けるように）．前のノーツが無いときは approachBeats 前から．
        /// </summary>
        public float approachGuideLeadBeats = 1f;
        /// <summary>Beats: 道しるべの光の粒が 1 本のテープを流れきるのにかかる拍数（拍の頭でむぎゅっとに着く）．</summary>
        public float approachGuideRunBeats = 1f;
        /// <summary>Beats: 道しるべを出すのは次のノーツの最大この秒数前から（前のノーツが遠いとき）．</summary>
        public float approachGuideMaxSec = 8f;
        /// <summary>Beats: 道しるべの色．</summary>
        public byte[] approachGuideColor = { 0, 120, 255 };
        /// <summary>
        /// Beats: むぎゅっとに縮む四角を出す．切ると，むぎゅっと全体が拍で光り，ノーツ時刻に向けてだんだん明るくなる
        /// （2026-11-03 ユーザー: 一度なしにしたが，やっぱり採用）．
        /// </summary>
        public bool approachSquare = true;
        /// <summary>Beats: 四角を出さないとき，案内の始めの明るさ（0〜1．ノーツ時刻で 1）．</summary>
        public float approachFillStart = 0.3f;
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
        /// 案内の光の粒と見分けにくいので一旦切ってある（2026-11-03 ユーザー）．
        /// </summary>
        public bool connectSweep = false;
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
    /// ノーツ用 LED（むぎゅっと 8×8 と，つながるノーツ用 30 個）を描く．旧仕様書（docs/legacy/SPECIFICATION-v1.md 4.7，7.1）の意図に沿い，
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
        readonly byte[][] approachConnect;  // [lane] テープを走る光の粒（毎フレーム描き直す．connect に明るい方で重ねる）
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
            approachConnect = new byte[laneCount][];
            trailColor = new byte[laneCount][];
            trailStart = new double[laneCount];
            judgedColor = new byte[laneCount][];
            judgedAt = new double[laneCount];
            judgedPerfect = new bool[laneCount];
            for (int l = 0; l < laneCount; l++)
            {
                mugu[l] = new byte[layout.MuguLeds * Channels];
                connect[l] = new byte[layout.ConnectLeds * Channels];
                approachConnect[l] = new byte[layout.ConnectLeds * Channels];
                trailColor[l] = new byte[Channels];
                judgedColor[l] = new byte[Channels];
            }
            Reset();
        }

        public NoteLedStyle Style => style;

        /// <summary>曲のテンポ（Beats の案内の 1 拍の長さに使う）．無ければ 120BPM．</summary>
        public TempoMap Tempo { get; set; }

        double SecPerBeatAt(double t)
        {
            if (Tempo == null) return 0.5;
            const double dt = 0.01;
            double beats = Tempo.BeatOf(t) - Tempo.BeatOf(t - dt);
            return beats > 1e-9 ? dt / beats : 0.5;
        }

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
            for (int l = 0; l < laneCount; l++) Array.Clear(approachConnect[l], 0, approachConnect[l].Length);
            if (chart != null && judge != null)
            {
                if (style.approach == NoteApproach.Beats) PaintApproach(chart, judge, songTime);
                else PaintOpenNotes(chart, judge, songTime);
            }
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

        readonly HashSet<int> approachPainted = new HashSet<int>();
        // 案内の光の粒が走っているテープ（このテープでは色相の演出・判定のあとの光を消し，光の粒だけを出す）
        readonly HashSet<int> approachTapes = new HashSet<int>();

        // 拍で案内する（NoteApproach.Beats）
        void PaintApproach(Chart chart, TouchJudge judge, double songTime)
        {
            var notes = chart.Notes;
            while (openCursor < notes.Count && (notes[openCursor].Type != NoteType.Touch || judge.ResultOf(openCursor) != null)) openCursor++;
            approachPainted.Clear();
            approachTapes.Clear();
            for (int i = openCursor; i < notes.Count && notes[i].Time - 10 <= songTime; i++)
            {
                if (notes[i].Type != NoteType.Touch || judge.ResultOf(i) != null || !Has(notes[i].Lane)) continue;
                double beat = SecPerBeatAt(notes[i].Time);
                double gap = notes[i].Time - PreviousTimeInFlow(notes, i);
                double window = style.approachBeats * beat;
                double muguWindow = gap < window ? Math.Min(window, style.approachShortBeats * beat) : window;   // むぎゅっと
                // 道しるべ（テープ）: 前のノーツの少し前から．前のノーツが無ければ window 前から
                double guideWindow = double.IsInfinity(gap) ? window : Math.Min(style.approachGuideMaxSec, gap + style.approachGuideLeadBeats * beat);
                guideWindow = Math.Max(guideWindow, muguWindow);
                double before = notes[i].Time - songTime;          // ノーツ時刻まであと何秒
                if (before > guideWindow) continue;
                int lane = notes[i].Lane;
                // 同じレーンに続けてノーツがあるときは先のものを描く（後のものは前のノーツの判定のあと）
                if (!approachPainted.Add(lane)) continue;
                double beatsBefore = before / beat;                  // ノーツ時刻まであと何拍
                // 拍の頭（ノーツ時刻から 1 拍ずつ数えたところ）からどれだけ過ぎたか（拍に対する割合）
                double phase = beatsBefore > 0 ? Math.Ceiling(beatsBefore) - beatsBefore : Math.Min(1, -beatsBefore);
                float pulse = style.approachPulseMin + (1 - style.approachPulseMin) * (float)Math.Exp(-phase * style.approachPulseDecay);
                var color = beatsBefore <= 1 ? style.approachLastColor : beatsBefore <= 2 ? style.approachColor2 : style.approachColor3;
                double muguRemain = Math.Max(0, before) / muguWindow;
                if (before > muguWindow) { }                         // むぎゅっとはまだ（テープの光の粒だけ先に走る）
                else if (style.approachSquare) PaintShrinkingSquare(mugu[lane], (float)muguRemain, pulse, color);
                else
                {
                    float k = (style.approachFillStart + (1 - style.approachFillStart) * (1 - (float)muguRemain)) * pulse;
                    Fill(mugu[lane], ToByte(color[0] / 255f * k), ToByte(color[1] / 255f * k), ToByte(color[2] / 255f * k));
                }
                // 「今」の合図: ノーツ時刻ちょうどに，むぎゅっと全体を明るく光らせてすぐ戻す
                if (before <= 0 && style.approachNowSec > 0)
                {
                    float now = (float)Math.Exp(before / style.approachNowSec);
                    byte[] dest = mugu[lane];
                    for (int p = 0; p < dest.Length; p++)
                        dest[p] = Math.Max(dest[p], ToByte(color[p % Channels] / 255f * now));
                }
                // 道しるべ（今のノーツへのものと次のノーツへのものが同じテープで逆向きに重なってよい．2026-11-03 ユーザー）
                double run = Math.Max(1e-3, style.approachGuideRunBeats) * beat;
                double x = before / run;
                guides.Add((notes[i].Flow, notes[i].Time, lane, (float)(x - Math.Floor(x))));   // 1 → 0 を繰り返す（拍の頭で着く）
            }
            // 道しるべ: 次のノーツの両隣のテープを，次のノーツへ向かって流れ続ける
            // （テープ k はむぎゅっと k から次の k+1 へ伸び，0 番目の LED が k の側．むぎゅっとは輪で，最後の次は最初）
            foreach (var g in guides)
            {
                int left = PrevLane(g.lane);
                if (left >= 0 && left != g.lane && Has(left))
                {
                    approachTapes.Add(left);
                    PaintComet(approachConnect[left], g.remain, 1f, style.approachGuideColor, towardEnd: true);
                }
                if (NextLane(g.lane) >= 0 && NextLane(g.lane) != g.lane)
                {
                    approachTapes.Add(g.lane);
                    PaintComet(approachConnect[g.lane], g.remain, 1f, style.approachGuideColor, towardEnd: false);
                }
            }
            guides.Clear();
        }

        readonly List<(int flow, double time, int lane, float remain)> guides = new List<(int, double, int, float)>();

        // 外周の四角が中心へ縮む．remain = 1 で外周（8×8 の縁），0 で中心の 2×2．中心はいつも薄く点ける
        void PaintShrinkingSquare(byte[] dest, float remain, float pulse, byte[] color)
        {
            int rows = layout.MuguRows, cols = layout.MuguColumns;
            float cx = (cols - 1) / 2f, cy = (rows - 1) / 2f;
            float outer = Math.Max(cx, cy);                 // 縁の距離（8×8 なら 3.5）
            const float inner = 0.5f;                       // 中心の 2×2 の距離
            float ring = inner + (outer - inner) * remain;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float d = Math.Max(Math.Abs(c - cx), Math.Abs(r - cy));      // 中心からの四角い距離
                    float k = Clamp01(1f - Math.Abs(d - ring) / 0.8f) * pulse;
                    if (d <= inner + 1e-3f) k = Math.Max(k, style.approachTargetLevel);
                    int i = (r * cols + c) * Channels;
                    for (int ch = 0; ch < Channels; ch++) dest[i + ch] = ToByte(color[ch] / 255f * k);
                }
            }
        }

        // むぎゅっとの輪の次（最後のレーンの次は最初のレーン）
        int NextLane(int lane)
        {
            int i = Array.IndexOf(lanes, lane);
            return i < 0 ? -1 : lanes[(i + 1) % lanes.Length];
        }

        // むぎゅっとの輪の前（最初のレーンの前は最後のレーン）
        int PrevLane(int lane)
        {
            int i = Array.IndexOf(lanes, lane);
            return i < 0 ? -1 : lanes[(i - 1 + lanes.Length) % lanes.Length];
        }


        // 同じ流れ（ChartNote.Flow）のすぐ前のノーツ（同じ時刻のものは除く）の時刻．無ければ −∞
        static double PreviousTimeInFlow(IReadOnlyList<ChartNote> notes, int index)
        {
            double t = notes[index].Time;
            int flow = notes[index].Flow;
            for (int j = index - 1; j >= 0 && j >= index - 12; j--)
                if (notes[j].Flow == flow && t - notes[j].Time >= 1e-3) return notes[j].Time;
            return double.NegativeInfinity;
        }

        // つながるノーツ用テープを光の粒が走る．remain = 1 で走り始め，0 で着く．
        // towardEnd = false: 遠い端（最後の LED）からむぎゅっとの側の端（0 番目）へ（このテープのむぎゅっとに着く）
        // towardEnd = true: 0 番目から最後の LED へ（テープの先の，次のレーンのむぎゅっとに着く）
        void PaintComet(byte[] dest, float remain, float pulse, byte[] color, bool towardEnd)
        {
            int n = layout.ConnectLeds;
            float head = towardEnd ? (n - 1) * (1 - remain) : (n - 1) * remain;
            float tail = Math.Max(0.5f, style.approachTailLeds);
            for (int i = 0; i < n; i++)
            {
                float behind = towardEnd ? head - i : i - head;   // 頭より後ろ（来た側）が尾
                float k = behind < -0.5f ? 0 : behind <= 0.5f ? 1f : (float)Math.Exp(-(behind - 0.5f) / tail * 2.5f);
                k *= pulse;
                for (int ch = 0; ch < Channels; ch++)
                    dest[i * Channels + ch] = Math.Max(dest[i * Channels + ch], ToByte(color[ch] / 255f * k));
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
                // 案内の光の粒が走っているテープは光の粒だけ（色相の演出の青〜紫と見分けにくかった．2026-11-03 ユーザー）
                var con = approachTapes.Contains(lane) ? approachConnect[lane] : connect[lane];
                var over = approachConnect[lane];
                bool swap = layout.ConnectSwapRG;
                for (int i = 0; i < layout.ConnectLeds; i++)
                {
                    // テープを走る光の粒は，色相の演出や判定のあとの光に明るい方で重ねる
                    byte r = Math.Max(con[i * Channels], over[i * Channels]);
                    byte g = Math.Max(con[i * Channels + 1], over[i * Channels + 1]);
                    byte b = Math.Max(con[i * Channels + 2], over[i * Channels + 2]);
                    pin[conStart + i * Channels] = swap ? g : r;
                    pin[conStart + i * Channels + 1] = swap ? r : g;
                    pin[conStart + i * Channels + 2] = b;
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
