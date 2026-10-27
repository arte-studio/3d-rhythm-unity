using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// 実機の接続状態を画面に出す（F2 で表示・非表示）．設営と本番中の監視用．
    /// 番号未設定の基板には番号を割り当てるボタンが出る．テスト表示（LedTestPattern）もここから切り替えられる．
    /// </summary>
    public sealed class DeviceMonitorHud : MonoBehaviour
    {
        public DeviceLink link;
        public LedTestPattern testPattern;
        public bool visible = true;

        List<DeviceInfo> snapshot = new List<DeviceInfo>();
        float nextRefresh;
        readonly StringBuilder sb = new StringBuilder();

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame) visible = !visible;
            if (visible && link != null && link.Host != null && Time.unscaledTime >= nextRefresh)
            {
                snapshot = link.Host.Snapshot();
                nextRefresh = Time.unscaledTime + 0.25f;
            }
        }

        void OnGUI()
        {
            if (!visible || link == null) return;
            const float w = 760;
            GUILayout.BeginArea(new Rect(Screen.width - w - 10, 10, w, Screen.height - 20), GUI.skin.box);
            if (link.Host == null)
            {
                GUILayout.Label("DeviceLink が動いていない（ポートが使えないなど．Console を見る）");
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label($"実機（F2 で隠す）  設営 {link.Setup?.name}  フレーム {link.Host.LastFrame}");
            GUILayout.Label("ID  接続   RSSI  表示fps  欠け/捨て  送信kbps  往復ms  遅れms  タッチ  ファーム  IP");
            foreach (var d in snapshot)
            {
                if (d.Id >= link.Hardware.devices.count) continue;
                GUILayout.Label(Row(d));
            }
            foreach (var d in snapshot)
            {
                if (d.Id < link.Hardware.devices.count || !d.Connected) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"番号未設定 {d.Mac}（{d.EndPoint?.Address}）→", GUILayout.Width(330));
                for (int id = 0; id < link.Hardware.devices.count; id++)
                    if (GUILayout.Button(id.ToString(), GUILayout.Width(36))) link.Host.AssignId(d.Mac, (byte)id);
                GUILayout.EndHorizontal();
            }
            if (testPattern != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("テスト表示", GUILayout.Width(80));
                foreach (TestPatterns.Mode m in System.Enum.GetValues(typeof(TestPatterns.Mode)))
                    if (GUILayout.Toggle(testPattern.mode == m, m.ToString(), GUI.skin.button)) testPattern.mode = m;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        readonly Dictionary<int, (uint shown, float time)> lastShown = new Dictionary<int, (uint, float)>();
        readonly Dictionary<int, float> fps = new Dictionary<int, float>();

        string Row(DeviceInfo d)
        {
            sb.Clear();
            sb.Append($"{d.Id,2}  ");
            if (d.IdConflict) sb.Append("<color=red>番号重複</color> ");
            sb.Append(d.Connected ? "<color=lime>OK</color>   " : $"<color=red>{d.SecondsSinceSeen:F0}秒</color>  ");
            if (d.HasStatus)
            {
                var s = d.Status;
                float now = Time.unscaledTime;
                if (lastShown.TryGetValue(d.Id, out var prev) && now - prev.time >= 1f)
                {
                    fps[d.Id] = (s.FramesShown + s.FramesForced - prev.shown) / (now - prev.time);
                    lastShown[d.Id] = (s.FramesShown + s.FramesForced, now);
                }
                else if (!lastShown.ContainsKey(d.Id)) lastShown[d.Id] = (s.FramesShown + s.FramesForced, now);
                fps.TryGetValue(d.Id, out float f);
                sb.Append($"{s.Rssi,4}  {f,6:F1}  {s.FramesForced,5}/{s.FramesDropped,-5} ");
            }
            else sb.Append("   -       -         -     ");
            // 変わらないピンは送らないので，止まった絵なら表示 fps も送信量も下がる（異常ではない）
            sb.Append($"{d.SentKbps,7:F0}  ");
            sb.Append(d.Synced ? $"{d.LastRttUs / 1000.0,6:F1}  " : "     -  ");
            sb.Append(double.IsNaN(d.DisplayLatencyMs) ? "    -   " : $"{d.DisplayLatencyMs,6:F1}  ");
            for (int i = 0; i < 5; i++) sb.Append((d.TouchState & (1 << i)) != 0 ? '■' : '□');
            sb.Append($"  v{d.Firmware >> 8}.{d.Firmware & 0xFF}  {d.EndPoint?.Address}");
            return sb.ToString();
        }
    }
}
