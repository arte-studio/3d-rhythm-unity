using System.Collections.Generic;
using Arte.Rhythm.Hardware;
using UnityEngine;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// むぎゅっと（ノーツモジュールの叩く所）の本体を描く: 半径 47.5mm の半透明の半球（2026-11-03 ユーザー．素材は透明に近い）．
    /// 平らな面の内側に LED（8×8）が貼ってあり，丸い面（リングの中心の側）から透けて見える．
    /// 半透明の殻を LED の上に被せるだけ（LED の点は LedPreview がそのまま描くので半球越しに見える）．
    /// 素材が乳白色で光が散らばる場合は glowScale を上げると，中の LED の平均の色で半球全体がぼんやり光る（近似）．
    /// 置き場所は設営データの modules（LedPreview.Notes）．
    /// 当たり判定も同じ形（Core の ModuleTouchDetector の Dome）．
    /// </summary>
    [DefaultExecutionOrder(101)]
    public sealed class ModuleDomes : MonoBehaviour
    {
        public LedPreview preview;
        [Tooltip("Arte/Viewer/Dome のマテリアル")]
        public Material material;
        [Tooltip("半球の半径（m）")]
        public float radiusM = 0.0475f;
        [Tooltip("乳白色の素材で光が散らばる見え方の近似（中の LED の平均の色で半球を光らせる強さ．0 = 被せるだけ）")]
        public float glowScale = 0f;

        readonly List<(int lane, int device, int module, Renderer renderer)> domes = new List<(int, int, int, Renderer)>();
        MaterialPropertyBlock props;
        static readonly int GlowId = Shader.PropertyToID("_Glow");

        /// <summary>シーンに無ければ足す（古いシーンでもそのまま出るように）．</summary>
        public static void EnsureFor(LedPreview preview)
        {
            if (preview == null || preview.GetComponent<ModuleDomes>() != null) return;
            var shader = Shader.Find("Arte/Viewer/Dome");
            if (shader == null) return;
            var d = preview.gameObject.AddComponent<ModuleDomes>();
            d.preview = preview;
            d.material = new Material(shader);
        }

        void Start()
        {
            if (preview == null) preview = GetComponent<LedPreview>();
        }

        void Build()
        {
            var notes = preview.Notes;
            var mesh = Hemisphere(radiusM, 24, 10);
            foreach (int lane in notes.Lanes)
            {
                if (!notes.TryGetPlacement(lane, out var p) || !notes.TryGetModule(lane, out var m)) continue;
                var go = new GameObject($"むぎゅっと（レーン {lane + 1}）", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(preview.transform, false);
                var center = new Vector3(p.mugyu[0], p.mugyu[1], p.mugyu[2]);
                var toCenter = new Vector3(-center.x, 0, -center.z);
                if (toCenter.sqrMagnitude < 1e-6f) toCenter = Vector3.forward;
                go.transform.localPosition = center;
                go.transform.localRotation = Quaternion.LookRotation(toCenter.normalized, Vector3.up);   // 丸い面（+z）がリングの中心を向く
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                domes.Add((lane, m.device, m.module, r));
            }
        }

        void LateUpdate()
        {
            if (preview == null || preview.Notes == null || preview.Output == null || material == null) return;
            if (domes.Count == 0) Build();
            props ??= new MaterialPropertyBlock();
            var notes = preview.Notes;
            var frame = preview.Output;
            int leds = notes.MuguLeds;
            foreach (var d in domes)
            {
                if (d.device >= frame.DeviceCount) continue;
                var pin = frame.Pin(d.device, notes.NoteOutputPin);
                int start = notes.MuguLedIndex(d.module, 0, 0) * 3;
                if (start + leds * 3 > pin.Length) continue;
                float r = 0, g = 0, b = 0;
                for (int i = 0; i < leds; i++)
                {
                    r += pin[start + i * 3];
                    g += pin[start + i * 3 + 1];
                    b += pin[start + i * 3 + 2];
                }
                // 送る値（明るさと曲線を掛けたあと）は光量に比例する
                float k = preview.exposure * glowScale / (255f * leds);
                props.SetColor(GlowId, new Color(r * k, g * k, b * k, 1));
                d.renderer.SetPropertyBlock(props);
            }
        }

        // 半球（+z 側が丸い面．平らな面は z = 0 で開いている）
        static Mesh Hemisphere(float radius, int around, int rings)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float phi = Mathf.PI / 2 * i / rings;            // 0 = 先（+z），π/2 = 縁
                for (int j = 0; j <= around; j++)
                {
                    float th = 2 * Mathf.PI * j / around;
                    var dir = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Sin(phi) * Mathf.Sin(th), Mathf.Cos(phi));
                    v.Add(dir * radius);
                    n.Add(dir);
                }
            }
            int w = around + 1;
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < around; j++)
                {
                    int a = i * w + j, b = a + 1, c = a + w, d = c + 1;
                    t.AddRange(new[] { a, c, b, b, c, d });
                }
            var mesh = new Mesh { name = "むぎゅっと（半球）" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
