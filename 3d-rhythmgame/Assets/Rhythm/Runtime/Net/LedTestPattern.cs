using UnityEngine;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// 設営確認用の表示を DeviceLink.Frame に書く（演出の描画ができる P5 までの間はこれだけが LED を光らせる）．
    /// 中身は Core の TestPatterns（コマンドの LinkTool~ と共通）．
    ///
    /// - StripHeads: 各ストリップの配線上の先頭 LED（ファームの自己診断と同じ．ピン 0 赤，1 緑，2 青）
    /// - CanvasCheck: 下端を赤，上端を青にし，1 列ずつ白く走らせる．どのストリップでも赤が下ならジグザグの向き，
    ///   白が隣の柱へ順に進めば列の並びが正しい
    /// - NoteModules: 配線されたレーンのモジュールをレーンごとの色で，触れている間は白．配線の無いモジュールは暗い赤
    /// - Rainbow: 全 LED が光る虹（電源と通信量の確認）
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class LedTestPattern : MonoBehaviour
    {
        public DeviceLink link;
        public TestPatterns.Mode mode = TestPatterns.Mode.Off;
        [Tooltip("CanvasCheck の白が 1 列進む秒数")]
        public float sweepSecondsPerColumn = 0.5f;

        byte[] canvas;
        bool[,] touching;
        TestPatterns.Mode lastMode = (TestPatterns.Mode)(-1);

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
            // Off のときは書かない（演出の描画が書いたものをそのまま送る）．切り替えた直後だけ消す
            if (mode == TestPatterns.Mode.Off)
            {
                if (lastMode != TestPatterns.Mode.Off) frame.Clear();
                lastMode = mode;
                return;
            }
            lastMode = mode;
            TestPatterns.Render(mode, frame, link.Perf, link.Notes, Time.unscaledTimeAsDouble, touching, ref canvas, sweepSecondsPerColumn);
        }
    }
}
