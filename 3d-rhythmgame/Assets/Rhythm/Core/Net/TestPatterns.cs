using System;
using Arte.Rhythm.Hardware;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// 設営確認用の表示を LedFrame に書く．Unity の LedTestPattern と，コマンドの LinkTool~ の両方から使う．
    /// </summary>
    public static class TestPatterns
    {
        public enum Mode { Off, StripHeads, CanvasCheck, NoteModules, Rainbow }

        /// <summary>
        /// 描く．time は秒（動く表示に使う），touching[device, module] は触れているか（null 可）．
        /// canvas は CanvasCheck / Rainbow 用の作業領域（null なら作る）．
        /// </summary>
        public static void Render(Mode mode, LedFrame frame, PerfLayout perf, NoteLayout notes, double time,
            bool[,] touching, ref byte[] canvas, double sweepSecondsPerColumn = 0.5)
        {
            frame.Clear();
            switch (mode)
            {
                case Mode.StripHeads: StripHeads(frame, perf); break;
                case Mode.CanvasCheck:
                    CanvasCheck(frame, perf, ref canvas, (int)(time / Math.Max(0.05, sweepSecondsPerColumn)) % perf.ColumnCount);
                    break;
                case Mode.NoteModules: NoteModules(frame, notes, touching); break;
                case Mode.Rainbow:
                    Rainbow(frame, perf, ref canvas, time);
                    NoteModules(frame, notes, touching, time);
                    break;
            }
        }

        /// <summary>各ストリップの配線上の先頭 LED を明るく，続く 2 個を暗く（ピン 0 赤，1 緑，2 青．ファームの自己診断と同じ）．</summary>
        public static void StripHeads(LedFrame frame, PerfLayout perf)
        {
            int perStrip = perf.LedsPerStrip;
            for (int d = 0; d < frame.DeviceCount; d++)
            {
                for (int pin = 0; pin < perf.PinsPerDevice; pin++)
                {
                    var rgb = frame.Pin(d, pin);
                    for (int start = 0; start < frame.LedCount(d, pin); start += perStrip)
                        for (int j = 0; j < 3; j++)
                            rgb[(start + j) * 3 + pin % 3] = (byte)(j == 0 ? 255 : 40);
                }
            }
        }

        /// <summary>
        /// キャンバスの下端 3 個を赤，上端 3 個を青にし，litColumn 列だけ白くする．
        /// どのストリップでも赤が下なら ジグザグの向き，白が隣の柱へ順に進めば列の並びが正しい．
        /// </summary>
        public static void CanvasCheck(LedFrame frame, PerfLayout perf, ref byte[] canvas, int litColumn)
        {
            int rows = perf.LedsPerStrip;
            PrepareCanvas(perf, ref canvas);
            for (int c = 0; c < perf.ColumnCount; c++)
            {
                int o = c * rows * 3;
                for (int k = 0; k < 3; k++)
                {
                    canvas[o + k * 3] = 255;
                    canvas[o + (rows - 1 - k) * 3 + 2] = 255;
                }
                if (c != litColumn) continue;
                for (int r = 3; r < rows - 3; r++) canvas[o + r * 3] = canvas[o + r * 3 + 1] = canvas[o + r * 3 + 2] = 120;
            }
            PackCanvas(frame, perf, canvas);
        }

        /// <summary>柱の角度方向に流れる虹．全 LED が光るので電源と通信量の確認になる．</summary>
        public static void Rainbow(LedFrame frame, PerfLayout perf, ref byte[] canvas, double time)
        {
            int rows = perf.LedsPerStrip;
            PrepareCanvas(perf, ref canvas);
            for (int c = 0; c < perf.ColumnCount; c++)
            {
                double angle = perf.GetStrip(c).AngleDeg / 360.0;
                for (int r = 0; r < rows; r++)
                {
                    Hsv((angle + r / (double)rows * 0.3 - time * 0.2) % 1.0, 1, 0.8, out byte R, out byte G, out byte B);
                    int o = (c * rows + r) * 3;
                    canvas[o] = R;
                    canvas[o + 1] = G;
                    canvas[o + 2] = B;
                }
            }
            PackCanvas(frame, perf, canvas);
        }

        /// <summary>
        /// 配線されたレーンのモジュールをレーンごとの色で，触れている間は白で光らせる．配線の無いモジュールは暗い赤．
        /// time を渡すと色がゆっくり回る．
        /// </summary>
        public static void NoteModules(LedFrame frame, NoteLayout notes, bool[,] touching, double time = 0)
        {
            for (int d = 0; d < frame.DeviceCount; d++)
            {
                var rgb = frame.Pin(d, notes.NoteOutputPin);
                for (int m = 0; m < notes.ModulesPerDevice; m++)
                {
                    int lane = notes.LaneOf(new ModuleAddress(d, m));
                    byte r, g, b;
                    if (touching != null && d < touching.GetLength(0) && m < touching.GetLength(1) && touching[d, m]) r = g = b = 255;
                    else if (lane < 0) { r = 40; g = 0; b = 0; }
                    else Hsv((lane / 12.0 + time * 0.1) % 1.0, 1, 0.6, out r, out g, out b);
                    for (int i = 0; i < notes.MuguLeds; i++) Put(rgb, notes.MuguLedIndex(m, 0, 0) + i, r, g, b);
                    for (int i = 0; i < notes.ConnectLeds; i++)
                    {
                        if (notes.ConnectSwapRG) Put(rgb, notes.ConnectLedIndex(m, i), g, r, b);
                        else Put(rgb, notes.ConnectLedIndex(m, i), r, g, b);
                    }
                }
            }
        }

        static void PrepareCanvas(PerfLayout perf, ref byte[] canvas)
        {
            int size = perf.ColumnCount * perf.LedsPerStrip * 3;
            if (canvas == null || canvas.Length != size) canvas = new byte[size];
            else Array.Clear(canvas, 0, canvas.Length);
        }

        static void PackCanvas(LedFrame frame, PerfLayout perf, byte[] canvas)
        {
            for (int d = 0; d < frame.DeviceCount; d++)
                for (int pin = 0; pin < perf.PinsPerDevice; pin++)
                    if (frame.LedCount(d, pin) > 0) perf.PackPin(canvas, d, pin, frame.Pin(d, pin));
        }

        static void Put(Span<byte> rgb, int led, byte r, byte g, byte b)
        {
            int o = led * 3;
            if (o + 2 >= rgb.Length) return;
            rgb[o] = r;
            rgb[o + 1] = g;
            rgb[o + 2] = b;
        }

        static void Hsv(double h, double s, double v, out byte r, out byte g, out byte b)
        {
            h = (h % 1.0 + 1.0) % 1.0 * 6.0;
            int i = (int)h;
            double f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            double R, G, B;
            switch (i)
            {
                case 0: R = v; G = t; B = p; break;
                case 1: R = q; G = v; B = p; break;
                case 2: R = p; G = v; B = t; break;
                case 3: R = p; G = q; B = v; break;
                case 4: R = t; G = p; B = v; break;
                default: R = v; G = p; B = q; break;
            }
            r = (byte)(R * 255);
            g = (byte)(G * 255);
            b = (byte)(B * 255);
        }
    }
}
