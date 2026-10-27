using UnityEngine;
using UnityEngine.InputSystem;

namespace Arte.Rhythm.Render
{
    /// <summary>Viewer シーンの状態表示（F2 で表示・非表示）．LED のデータが届いているかと操作方法．</summary>
    public sealed class ViewerHud : MonoBehaviour
    {
        public LedPreview preview;
        public ViewerRoom room;
        public bool visible = true;

        long lastPackets;
        float lastTime, rate;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame) visible = !visible;
            var m = preview != null ? preview.Mirror : null;
            if (m != null && Time.unscaledTime - lastTime >= 1f)
            {
                rate = (m.PacketsReceived - lastPackets) / (Time.unscaledTime - lastTime);
                lastPackets = m.PacketsReceived;
                lastTime = Time.unscaledTime;
            }
        }

        void OnGUI()
        {
            if (!visible || preview == null) return;
            var m = preview.Mirror;
            string state = m == null ? "（同じシーンの DeviceLink から）"
                : m.PacketsReceived == 0 ? $"ポート {m.Port} で待受中．送る側の DeviceLink.mirrorTo か起動引数 -mirror にこの PC の IP を入れる"
                : $"{m.LastSender?.Address} から受信 {rate:F0} パケット/秒（不正 {m.PacketsBad}，フレーム {m.LastFrame}）";
            GUILayout.BeginArea(new Rect(10, 10, 900, 100), GUI.skin.box);
            GUILayout.Label($"Viewer（F2 で隠す）  {state}");
            if (room != null) GUILayout.Label($"床: {room.FloorLabel}（F3 で切り替え）  暗幕: {room.CurtainLabel}");
            GUILayout.Label("右ボタン + マウス: 見回す，右ボタン + WASD / Q / E: 歩く（Shift で速く）．F5 中心・子どもの目，F6 中心・大人の目，F7 外から，F8 真上から");
            GUILayout.EndArea();
        }
    }
}
