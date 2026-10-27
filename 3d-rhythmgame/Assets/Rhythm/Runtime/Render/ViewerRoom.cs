using System.Collections.Generic;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Viewer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// Viewer の部屋: 床，周りの暗幕，LED テープ本体．どれも LED の光だけで照らす（Arte/Viewer/LitByLeds）．
    /// 光は頂点ごとに計算するので，メッシュを細かく作る．
    /// 床と暗幕の種類・暗幕までの距離は設営データの room（RoomSettings / RoomSurfaces）．F3 か起動引数 -floor で床の種類を切り替えられる．
    /// テープ本体は PerfLayout から作る（外したストリップは作らない）．
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class ViewerRoom : MonoBehaviour
    {
        public LedPreview preview;
        [Tooltip("Arte/Viewer/LitByLeds のマテリアル（床・暗幕・テープで色を変えるので複製して使う）")]
        public Material litMaterial;
        [Tooltip("床の種類（空なら設営データの room.floor）．carpet-gray / school-white / concrete")]
        public string floorOverride = "";
        [Tooltip("LED テープ本体（基板）の反射率")]
        public Color tapeColor = new Color(0.12f, 0.12f, 0.12f);
        public float tapeWidth = 0.010f;
        [Tooltip("床と暗幕の網の細かさ（m）．光の細かさになる")]
        public float gridStep = 0.08f;

        readonly List<Object> created = new List<Object>();
        float curtainRadius, curtainHeight;
        Material floorMaterial;
        int floorIndex;

        /// <summary>今の床の種類（表示用）．</summary>
        public string FloorLabel => RoomSurfaces.Floors[floorIndex].Label;
        public string CurtainLabel { get; private set; }

        void Start()
        {
            if (preview == null || litMaterial == null || preview.Perf == null)
            {
                Debug.LogError("[ViewerRoom] LedPreview と LitByLeds のマテリアルを設定する（LedPreview より後に Start する）");
                enabled = false;
                return;
            }
            var room = preview.Setup.room ?? new RoomSettings();
            curtainRadius = room.curtainRadiusM;
            curtainHeight = room.curtainHeightM;
            var curtain = RoomSurfaces.Find(RoomSurfaces.Curtains, room.curtain);
            CurtainLabel = curtain.Label;
            floorMaterial = Part("Floor", Floor(), Color.gray, false, 0.05f);
            var args = System.Environment.GetCommandLineArgs();
            int a = System.Array.IndexOf(args, "-floor");
            string floor = a >= 0 && a + 1 < args.Length ? args[a + 1] : string.IsNullOrEmpty(floorOverride) ? room.floor : floorOverride;
            SetFloor(RoomSurfaces.IndexOf(RoomSurfaces.Floors, floor));
            Part("Curtain", Curtain(), new Color(curtain.R, curtain.G, curtain.B), false, 0.05f);
            // テープ本体は自分の LED のすぐ横なので，近くの光源（まとめた光源の中心は LED から離れている）を弱める
            Part("Tapes", Tapes(preview.Perf), tapeColor, true, 0.4f);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame && floorMaterial != null) SetFloor((floorIndex + 1) % RoomSurfaces.Floors.Length);
        }

        public void SetFloor(int index)
        {
            floorIndex = index;
            var f = RoomSurfaces.Floors[index];
            floorMaterial.SetColor("_BaseColor", new Color(f.R, f.G, f.B));
        }

        void OnDestroy()
        {
            foreach (var o in created) Destroy(o);
        }

        Material Part(string name, Mesh mesh, Color color, bool twoSided, float nearM)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = new Material(litMaterial) { name = name };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_TwoSided", twoSided ? 1 : 0);
            mat.SetFloat("_NearM", nearM);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            created.Add(mesh);
            created.Add(mat);
            return mat;
        }

        Mesh Floor()
        {
            int n = Mathf.CeilToInt(curtainRadius * 2 / gridStep);
            var v = new List<Vector3>();
            var tri = new List<int>();
            for (int z = 0; z <= n; z++)
                for (int x = 0; x <= n; x++)
                    v.Add(new Vector3(-curtainRadius + x * gridStep, 0, -curtainRadius + z * gridStep));
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int a = z * (n + 1) + x;
                    tri.AddRange(new[] { a, a + n + 1, a + 1, a + 1, a + n + 1, a + n + 2 });
                }
            }
            return Build("Floor", v, tri, i => Vector3.up);
        }

        Mesh Curtain()
        {
            int around = Mathf.CeilToInt(2 * Mathf.PI * curtainRadius / gridStep);
            int up = Mathf.CeilToInt(curtainHeight / gridStep);
            var v = new List<Vector3>();
            var tri = new List<int>();
            for (int j = 0; j <= up; j++)
            {
                for (int i = 0; i <= around; i++)
                {
                    float a = i * 2 * Mathf.PI / around;
                    v.Add(new Vector3(Mathf.Sin(a) * curtainRadius, j * curtainHeight / up, Mathf.Cos(a) * curtainRadius));
                }
            }
            for (int j = 0; j < up; j++)
            {
                for (int i = 0; i < around; i++)
                {
                    int a = j * (around + 1) + i;
                    tri.AddRange(new[] { a, a + 1, a + around + 1, a + 1, a + around + 2, a + around + 1 });
                }
            }
            // 面は中心を向く
            return Build("Curtain", v, tri, i => new Vector3(-v[i].x, 0, -v[i].z).normalized);
        }

        Mesh Tapes(PerfLayout perf)
        {
            const int segments = 30;
            var v = new List<Vector3>();
            var normals = new List<Vector3>();
            var tri = new List<int>();
            for (int c = 0; c < perf.ColumnCount; c++)
            {
                if (!perf.GetStrip(c).Installed) continue;
                perf.GetLedPosition(c, 0, out float x0, out _, out float z0);
                var inward = new Vector3(-x0, 0, -z0).normalized;
                var side = Vector3.Cross(Vector3.up, inward) * (tapeWidth * 0.5f);
                perf.GetLedPosition(c, perf.LedsPerStrip - 1, out _, out float yTop, out _);
                float bottom = yTop - perf.LedPitchM * (perf.LedsPerStrip - 0.5f), top = yTop + perf.LedPitchM * 0.5f;
                int start = v.Count;
                for (int s = 0; s <= segments; s++)
                {
                    var p = new Vector3(x0, Mathf.Lerp(bottom, top, (float)s / segments), z0);
                    v.Add(p - side);
                    v.Add(p + side);
                    normals.Add(inward);
                    normals.Add(inward);
                }
                for (int s = 0; s < segments; s++)
                {
                    int a = start + s * 2;
                    tri.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
            }
            return Build("Tapes", v, tri, i => normals[i]);
        }

        static Mesh Build(string name, List<Vector3> v, List<int> tri, System.Func<int, Vector3> normal)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v);
            var n = new Vector3[v.Count];
            for (int i = 0; i < n.Length; i++) n[i] = normal(i);
            mesh.SetNormals(n);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
