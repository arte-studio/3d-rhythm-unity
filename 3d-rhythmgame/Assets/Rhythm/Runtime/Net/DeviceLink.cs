using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using Arte.Rhythm.Gameplay;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Song;
using UnityEngine;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// 実機（ESP32-S3，ファーム v2）とつなぐ．旧版の UdpController の代わり．
    ///
    /// - タッチはデバイスが付けた時刻を PC の時刻（AudioClock.Realtime）に直して GameSession に渡す．
    ///   旧版は 50ms ごとの状態を受け取った時刻で判定していたので，Wi-Fi の揺れがそのまま判定に乗っていた．
    /// - LED は Frame（配線順の RGB）に書かれたものを sendFps で送る．書くのは演出の描画（P5）と LedTestPattern．
    /// - 明るさは設営プロファイル，ピンク表示などの動作はここの設定をデバイスに 1 秒ごとに送る．
    /// 旧版の UdpController と同じポートを使うので，同じシーンに置かない．
    /// </summary>
    public sealed class DeviceLink : MonoBehaviour
    {
        [Tooltip("Assets/Rhythm/Data/Hardware/hardware.json")]
        public TextAsset hardwareJson;
        [Tooltip("Assets/Rhythm/Data/Hardware/setups/*.json")]
        public TextAsset setupJson;
        public GameSession session;

        [Range(1, 120)] public int sendFps = 60;

        [Header("デバイスに送る設定")]
        [Tooltip("タッチから 3 秒間，そのモジュールのむぎゅっとをピンクにする（旧ファームの動作）")]
        public bool touchFeedback = true;
        [Tooltip("PC とつながっていない間，配線の先頭 LED とタッチを光らせる（設営時の確認用）")]
        public bool selfTestWhenIdle;
        [Range(0, 30), Tooltip("タッチのチャタリング除去（ms）")]
        public int debounceMs = 5;
        [Range(10, 500), Tooltip("フレームのパケットが揃わないとき，今あるデータで表示するまでの待ち（ms）")]
        public int holdMs = 50;

        readonly ConcurrentQueue<DeviceTouch> touchQueue = new ConcurrentQueue<DeviceTouch>();
        readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        double nextSendTime;

        public LinkHost Host { get; private set; }
        public LedFrame Frame { get; private set; }
        public HardwareConfig Hardware { get; private set; }
        public SetupProfile Setup { get; private set; }
        public PerfLayout Perf { get; private set; }
        public NoteLayout Notes { get; private set; }

        /// <summary>タッチの変化（メインスレッドで呼ばれる．表示用）．判定には受信スレッドから直接渡している．</summary>
        public event Action<DeviceTouch> TouchChanged;

        static ulong NowUs() => (ulong)(AudioClock.Realtime * 1e6);

        void Awake()
        {
            if (hardwareJson == null || setupJson == null)
            {
                Debug.LogError("[DeviceLink] hardwareJson と setupJson を設定する");
                enabled = false;
                return;
            }
            Hardware = HardwareJson.LoadConfig(hardwareJson.text);
            Setup = HardwareJson.LoadSetup(setupJson.text);
            Perf = new PerfLayout(Hardware, Setup);
            Notes = new NoteLayout(Hardware, Setup);
            Frame = LedFrame.FromLayout(Perf, Notes, Hardware.devices.count);
        }

        void OnEnable()
        {
            if (Hardware == null) return;
            var options = new LinkOptions { HostPort = Hardware.devices.hostPort, DeviceCount = Hardware.devices.count };
            ApplySettings(options);
            Host = new LinkHost(options, NowUs);
            Host.Touched += OnTouched;
            Host.Log += logQueue.Enqueue;
            try
            {
                Host.Start();
                Debug.Log($"[DeviceLink] ポート {Host.LocalPort} で待受（設営 {Setup.name}）");
            }
            catch (SocketException e)
            {
                Debug.LogError($"[DeviceLink] ポート {options.HostPort} を開けない（旧版の UdpController や別の Unity が使っているかもしれない）: {e.Message}");
                Host = null;
                enabled = false;
            }
        }

        void OnDisable()
        {
            Host?.Dispose();
            Host = null;
        }

        void OnValidate()
        {
            if (Host != null) ApplySettings(Host.Options);
        }

        void ApplySettings(LinkOptions options)
        {
            byte flags = 0;
            if (touchFeedback) flags |= ArteProtocol.FlagTouchFeedback;
            if (selfTestWhenIdle) flags |= ArteProtocol.FlagSelfTest;
            options.Config = new DeviceConfig
            {
                BrightnessPerf = (byte)Mathf.Clamp(Setup.brightnessPerf, 0, 255),
                BrightnessNote = (byte)Mathf.Clamp(Setup.brightnessNote, 0, 255),
                Flags = flags,
                DebounceMs = (byte)debounceMs,
                HeartbeatMs = 50,
                HoldMs = (ushort)holdMs,
            };
        }

        // 受信スレッド
        void OnTouched(DeviceTouch t)
        {
            if (t.Down && session != null)
            {
                int lane = Notes.LaneOf(new ModuleAddress(t.Device, t.Sensor));
                if (lane >= 0) session.Touch(lane, t.HostTimeUs / 1e6);
            }
            touchQueue.Enqueue(t);
        }

        void Update()
        {
            if (Host == null) return;
            Host.Update();
            while (logQueue.TryDequeue(out var msg)) Debug.Log($"[DeviceLink] {msg}");
            while (touchQueue.TryDequeue(out var t)) TouchChanged?.Invoke(t);
        }

        void LateUpdate()
        {
            if (Host == null) return;
            double now = AudioClock.Realtime, interval = 1.0 / sendFps;
            // 描画と同じ 60fps のとき，フレーム間隔の揺れで 1 回おきに飛ばさないよう 1/4 間隔の余裕を持たせる
            if (now < nextSendTime - interval * 0.25) return;
            nextSendTime = now - nextSendTime > interval ? now + interval : nextSendTime + interval;
            Host.SendFrame(Frame);
        }
    }
}
