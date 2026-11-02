using System;
using System.Collections.Generic;
using Arte.Rhythm.Hardware;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>触れる範囲の形．</summary>
    public enum TouchShape
    {
        /// <summary>むぎゅっとの本物の形: 半径 DomeRadiusM の半球（平らな面の内側に LED．丸い面がリングの中心を向く）．丸い面に触れたらタッチ</summary>
        Dome,
        /// <summary>むぎゅっとの正面の箱（HalfSizeM 四方，手前 FrontM〜奥 BackM）</summary>
        Box,
    }

    /// <summary>
    /// VR（や将来のカメラ）で手の位置からノーツモジュールへのタッチを作る．Unity に依存しない．
    ///
    /// 各レーンのむぎゅっとに触れる範囲を置く（面はリングの中心を向く．座標は ModulePlacement と同じ m）．
    /// 既定は本物と同じ半球（2026-11-03 ユーザー: むぎゅっとは半径 47.5mm の半透明の半球で，叩く面が丸い）．
    /// 手の点が範囲に入った瞬間をタッチ（押し始め）とする．出るときは少し余裕を持たせる（縁でぶるぶる出入りして何度もタッチしないように）．
    /// 判定は押し始めだけなので，入ったままでは次のノーツは取れない（実機のタッチセンサーと同じ）．
    /// </summary>
    public sealed class ModuleTouchDetector
    {
        public TouchShape Shape = TouchShape.Dome;
        /// <summary>Dome: 半球の半径（m）．</summary>
        public float DomeRadiusM = 0.0475f;
        /// <summary>Dome: 丸い面からどれだけ離れていても触れたことにするか（m．手の先の大きさの分）．</summary>
        public float DomeMarginM = 0.01f;
        /// <summary>Dome: 平らな面より奥に何 m まで触れたことにするか．</summary>
        public float DomeBackM = 0.02f;

        /// <summary>触れる箱の半分の幅と高さ（m）．むぎゅっとの LED は 5.6cm 角だが，手で触りやすいよう大きめ．</summary>
        public float HalfSizeM = 0.12f;
        /// <summary>面より手前（リングの中心の側）に何 m まで触れたことにするか．</summary>
        public float FrontM = 0.15f;
        /// <summary>面より奥に何 m まで触れたことにするか（手が面を突き抜けても触れたまま）．</summary>
        public float BackM = 0.10f;
        /// <summary>出たことにするときの余裕（m）．</summary>
        public float ReleaseMarginM = 0.05f;      // 半球は小さく，3cm では縁で手がぶれて二重にタッチした（2026-11-03．0.4 秒以内の二重が 17 回）

        readonly struct Box
        {
            public readonly int Lane;
            public readonly float Cx, Cy, Cz, Nx, Nz, Rx, Rz;

            public Box(int lane, float cx, float cy, float cz)
            {
                Lane = lane;
                Cx = cx;
                Cy = cy;
                Cz = cz;
                // 面の向き = 中心へ（水平）．右 = 向き × 上 の逆（ModuleGeometry と同じ）
                float len = (float)Math.Sqrt(cx * cx + cz * cz);
                Nx = len > 1e-6f ? -cx / len : 0f;
                Nz = len > 1e-6f ? -cz / len : 1f;
                Rx = -Nz;
                Rz = Nx;
            }
        }

        readonly List<Box> boxes = new List<Box>();
        readonly HashSet<int> inside = new HashSet<int>();
        readonly HashSet<int> insideNow = new HashSet<int>();

        public IReadOnlyCollection<int> Touching => inside;

        public ModuleTouchDetector(IEnumerable<ModulePlacement> placements)
        {
            foreach (var p in placements)
                if (p?.mugyu != null && p.mugyu.Length >= 3) boxes.Add(new Box(p.lane, p.mugyu[0], p.mugyu[1], p.mugyu[2]));
        }

        public static ModuleTouchDetector FromLayout(NoteLayout notes)
        {
            var list = new List<ModulePlacement>();
            foreach (int lane in notes.Lanes)
                if (notes.TryGetPlacement(lane, out var p)) list.Add(p);
            return new ModuleTouchDetector(list);
        }

        /// <summary>
        /// 手の点（x, y, z を並べたもの．何個でも）で更新し，この呼び出しで触れ始めたレーンを pressed に足す．
        /// </summary>
        public void Update(IReadOnlyList<float> points, List<int> pressed)
        {
            insideNow.Clear();
            int n = points.Count / 3;
            foreach (var b in boxes)
            {
                float margin = inside.Contains(b.Lane) ? ReleaseMarginM : 0f;
                for (int i = 0; i < n; i++)
                {
                    float dx = points[i * 3] - b.Cx, dy = points[i * 3 + 1] - b.Cy, dz = points[i * 3 + 2] - b.Cz;
                    float front = dx * b.Nx + dz * b.Nz;          // 面より手前が正
                    if (Shape == TouchShape.Dome)
                    {
                        // 半球: 中心（平らな面の真ん中）からの距離が半径（と余裕）以内で，平らな面より手前
                        float r = DomeRadiusM + DomeMarginM + margin;
                        if (dx * dx + dy * dy + dz * dz > r * r || front < -DomeBackM - margin) continue;
                    }
                    else
                    {
                        float u = dx * b.Rx + dz * b.Rz;
                        if (front > FrontM + margin || front < -BackM - margin) continue;
                        if (Math.Abs(u) > HalfSizeM + margin || Math.Abs(dy) > HalfSizeM + margin) continue;
                    }
                    insideNow.Add(b.Lane);
                    break;
                }
            }
            foreach (int lane in insideNow)
                if (!inside.Contains(lane)) pressed.Add(lane);
            inside.Clear();
            inside.UnionWith(insideNow);
        }
    }
}
