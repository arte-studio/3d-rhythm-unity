using System;
using Arte.Rhythm.Hardware;
using UnityEngine;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// 設営時の確認用の表示を DeviceLink.Frame に書く（演出の描画ができる P5 までの間はこれだけが LED を光らせる）．
    ///
    /// - StripHeads: 各ストリップの配線上の先頭 LED（ファームの自己診断と同じ．ピン 0 赤，1 緑，2 青）
    /// - CanvasCheck: キャンバスの下端を赤，上端を青にし，1 列ずつ白く走らせる．
    ///   どのストリップでも赤が下に来ればジグザグの向き（hardware.json の topFirstParityOfPin）が正しい．
    ///   白が隣の柱へ順に進めば列の並び（canvasColumnOffsetOfPin）が正しい．
    /// - NoteModules: 配線されたレーンのモジュールをレーンごとの色で，触れている間は白で光らせる．配線の無いモジュールは暗い赤．
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class LedTestPattern : MonoBehaviour
    {
        public enum Mode { Off, StripHeads, CanvasCheck, NoteModules }

        public DeviceLink link;
        public Mode mode = Mode.Off;
        [Tooltip("CanvasCheck の白が 1 列進む秒数")]
        public float sweepSecondsPerColumn = 0.5f;

        byte[] canvas;
        bool[,] touching;
        Mode lastMode = (Mode)(-1);

        void OnEnable()
        {
            if (link != null) link.TouchChanged += OnTouch;
        }

        void OnDisable()
        {
            if (link != null) link.TouchChanged -= OnTouch;
        }

        void OnTouch(DeviceTouch t)
        {
            if (touching != null && t.Device < touching.GetLength(0) && t.Sensor < touching.GetLength(1))
                touching[t.Device, t.Sensor] = t.Down;
        }

        void LateUpdate()
        {
            var frame = link != null ? link.Frame : null;
            if (frame == null) return;
            touching ??= new bool[frame.DeviceCount, link.Notes.ModulesPerDevice];
            if (mode == Mode.Off)
            {
                if (lastMode != Mode.Off) frame.Clear();
                lastMode = mode;
                return;
            }
            lastMode = mode;
            frame.Clear();
            switch (mode)
            {
                case Mode.StripHeads: StripHeads(frame); break;
                case Mode.CanvasCheck: CanvasCheck(frame, link.Perf); break;
                case Mode.NoteModules: NoteModules(frame, link.Notes); break;
            }
        }

        void StripHeads(LedFrame frame)
        {
            int perStrip = link.Perf.LedsPerStrip;
            for (int d = 0; d < frame.DeviceCount; d++)
            {
                for (int pin = 0; pin < LedFrame.PinsPerDevice; pin++)
                {
                    if (pin == frame.NotePin) continue;
                    var rgb = frame.Pin(d, pin);
                    for (int start = 0; start < frame.LedCount(d, pin); start += perStrip)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            byte v = (byte)(j == 0 ? 255 : 40);
                            rgb[(start + j) * 3 + pin] = v;
                        }
                    }
                }
            }
        }

        void CanvasCheck(LedFrame frame, PerfLayout perf)
        {
            int rows = perf.LedsPerStrip, cols = perf.ColumnCount;
            int size = cols * rows * 3;
            if (canvas == null || canvas.Length != size) canvas = new byte[size];
            Array.Clear(canvas, 0, canvas.Length);
            int lit = (int)(Time.unscaledTime / Mathf.Max(0.05f, sweepSecondsPerColumn)) % cols;
            for (int c = 0; c < cols; c++)
            {
                int o = c * rows * 3;
                for (int k = 0; k < 3; k++)
                {
                    canvas[o + k * 3] = 255;                        // 下端 3 個: 赤
                    canvas[o + (rows - 1 - k) * 3 + 2] = 255;       // 上端 3 個: 青
                }
                if (c != lit) continue;
                for (int r = 3; r < rows - 3; r++)
                {
                    canvas[o + r * 3] = canvas[o + r * 3 + 1] = canvas[o + r * 3 + 2] = 120;
                }
            }
            for (int d = 0; d < frame.DeviceCount; d++)
                for (int pin = 0; pin < perf.PinsPerDevice; pin++)
                    if (frame.LedCount(d, pin) > 0) perf.PackPin(canvas, d, pin, frame.Pin(d, pin));
        }

        void NoteModules(LedFrame frame, NoteLayout notes)
        {
            for (int d = 0; d < frame.DeviceCount; d++)
            {
                var rgb = frame.Pin(d, notes.NoteOutputPin);
                for (int m = 0; m < notes.ModulesPerDevice; m++)
                {
                    int lane = notes.LaneOf(new ModuleAddress(d, m));
                    Color32 c = lane < 0 ? new Color32(40, 0, 0, 255)
                        : touching[d, m] ? new Color32(255, 255, 255, 255)
                        : (Color32)Color.HSVToRGB(lane / 12f % 1f, 1f, 0.6f);
                    for (int i = 0; i < notes.MuguLeds; i++)
                        Put(rgb, notes.MuguLedIndex(m, 0, 0) + i, c, false);
                    for (int i = 0; i < notes.ConnectLeds; i++)
                        Put(rgb, notes.ConnectLedIndex(m, i), c, notes.ConnectSwapRG);
                }
            }
        }

        static void Put(Span<byte> rgb, int led, Color32 c, bool swapRG)
        {
            int o = led * 3;
            if (o + 2 >= rgb.Length) return;
            rgb[o] = swapRG ? c.g : c.r;
            rgb[o + 1] = swapRG ? c.r : c.g;
            rgb[o + 2] = c.b;
        }
    }
}
