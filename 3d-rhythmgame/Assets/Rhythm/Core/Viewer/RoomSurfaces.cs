using System;

namespace Arte.Rhythm.Viewer
{
    /// <summary>会場の部屋（Viewer 用）．設営データの room に書く．</summary>
    [Serializable]
    public sealed class RoomSettings
    {
        /// <summary>床の種類（RoomSurfaces.Floors の名前）．</summary>
        public string floor = "carpet-gray";
        /// <summary>暗幕の種類（RoomSurfaces.Curtains の名前）．</summary>
        public string curtain = "blackout";
        /// <summary>リングの中心から暗幕まで（m）．</summary>
        public float curtainRadiusM = 3.0f;
        public float curtainHeightM = 3.0f;
    }

    /// <summary>床と暗幕の反射率（拡散反射，リニア）．値は一般的な材料の目安で，会場で測ったものではない．</summary>
    public static class RoomSurfaces
    {
        public readonly struct Surface
        {
            public readonly string Name;
            public readonly string Label;
            public readonly float R, G, B;

            public Surface(string name, string label, float r, float g, float b)
            {
                Name = name;
                Label = label;
                R = r;
                G = g;
                B = b;
            }
        }

        /// <summary>
        /// 床．学校の床と展示場のコンクリートは少しつやがあり，実際には LED が床に映り込むが，その映り込みはまだ描いていない．
        /// </summary>
        public static readonly Surface[] Floors =
        {
            new Surface("carpet-gray", "グレーのカーペット", 0.15f, 0.15f, 0.15f),
            new Surface("school-white", "白い床（学校）", 0.60f, 0.60f, 0.58f),
            new Surface("concrete", "コンクリート（展示場）", 0.30f, 0.29f, 0.27f),
        };

        public static readonly Surface[] Curtains =
        {
            new Surface("blackout", "一般的な暗幕（黒）", 0.03f, 0.03f, 0.03f),
        };

        public static Surface Find(Surface[] list, string name)
        {
            foreach (var s in list)
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            return list[0];
        }

        public static int IndexOf(Surface[] list, string name)
        {
            for (int i = 0; i < list.Length; i++)
                if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }
    }
}
