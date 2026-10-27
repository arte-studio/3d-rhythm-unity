using System;
using System.Collections.Generic;

namespace Arte.Rhythm.Hardware
{
    /// <summary>
    /// ノーツモジュール 1 個の置き場所（設営ごとに変わるので SetupProfile に持つ）．座標は m で，リングの中心が原点，y が上
    /// （PerfLayout と同じ．旧版のシーンの座標そのまま）．
    /// </summary>
    [Serializable]
    public sealed class ModulePlacement
    {
        public int lane;
        /// <summary>むぎゅっと（8×8）の中心．面はリングの中心（y 軸）を向く．</summary>
        public float[] mugyu = { 0, 0, 0 };
        /// <summary>つながるノーツ用テープの 0 番目の LED と最後の LED の位置．</summary>
        public float[] connectFrom = { 0, 0, 0 };
        public float[] connectTo = { 0, 0, 0 };
    }

    /// <summary>旧版の配置ファイル（Resources/object_positions_*.json）の 1 行．</summary>
    public struct LegacyPlacedObject
    {
        public string prefabName;
        public float x, y, z, rx, ry, rz;
    }

    /// <summary>ノーツモジュールの LED の位置（シミュレーション表示用）．</summary>
    public static class ModuleGeometry
    {
        /// <summary>
        /// むぎゅっとの (row, column) の LED の位置．row 0 が下，column 0 はリングの中心から見て左（旧版の LEDMatrixGenerator と同じ並び）．
        /// sizeM は端の LED から反対の端の LED まで．
        /// </summary>
        public static void MuguLed(ModulePlacement p, int row, int column, int rows, int columns, float sizeM, out float x, out float y, out float z)
        {
            float cx = p.mugyu[0], cy = p.mugyu[1], cz = p.mugyu[2];
            // 面の向き = 中心へ（水平）．中心から見た右 = 向き × 上 の逆
            float len = (float)Math.Sqrt(cx * cx + cz * cz);
            float fx = len > 1e-6f ? -cx / len : 0f, fz = len > 1e-6f ? -cz / len : 1f;
            float rx = -fz, rz = fx;
            float pitchX = columns > 1 ? sizeM / (columns - 1) : 0f, pitchY = rows > 1 ? sizeM / (rows - 1) : 0f;
            float u = (column - (columns - 1) * 0.5f) * pitchX;
            float v = (row - (rows - 1) * 0.5f) * pitchY;
            x = cx + rx * u;
            y = cy + v;
            z = cz + rz * u;
        }

        /// <summary>つながるノーツ用テープの i 番目（count 個中）の LED の位置．</summary>
        public static void ConnectLed(ModulePlacement p, int i, int count, out float x, out float y, out float z)
        {
            float t = count > 1 ? (float)i / (count - 1) : 0.5f;
            x = p.connectFrom[0] + (p.connectTo[0] - p.connectFrom[0]) * t;
            y = p.connectFrom[1] + (p.connectTo[1] - p.connectFrom[1]) * t;
            z = p.connectFrom[2] + (p.connectTo[2] - p.connectFrom[2]) * t;
        }

        /// <summary>
        /// 旧版の配置ファイルから，設営のレーンごとの置き場所を作る．
        /// ファイルは touch_notes（むぎゅっと）と Connect_module（テープ）が交互に並んでいるので，touch_notes と
        /// その次の Connect_module を 1 組とする．旧版はノーツが来るたびに順番に組を使い回していたので，レーンとの対応は
        /// ファイルに無い．ここでは各レーンのモジュールがつながるデバイスの演出用ストリップの角度に近い組から順に割り当てる（仮）．
        /// </summary>
        public static List<ModulePlacement> FromLegacy(IReadOnlyList<LegacyPlacedObject> objects, PerfLayout perf, NoteLayout notes,
                                                       float connectHalfLengthM = 0.2475f)
        {
            var pairs = new List<(LegacyPlacedObject touch, LegacyPlacedObject connect)>();
            for (int i = 0; i + 1 < objects.Count; i++)
            {
                if (objects[i].prefabName == "touch_notes" && objects[i + 1].prefabName == "Connect_module")
                {
                    pairs.Add((objects[i], objects[i + 1]));
                    i++;
                }
            }

            // デバイスごとの演出用ストリップの向き（平均）
            var sumSin = new double[perf.DeviceCount];
            var sumCos = new double[perf.DeviceCount];
            for (int c = 0; c < perf.ColumnCount; c++)
            {
                var s = perf.GetStrip(c);
                perf.GetLedPosition(c, 0, out float x, out _, out float z);
                double a = Math.Atan2(x, z);
                sumSin[s.Address.device] += Math.Sin(a);
                sumCos[s.Address.device] += Math.Cos(a);
            }

            var used = new bool[pairs.Count];
            var result = new List<ModulePlacement>();
            foreach (int lane in notes.Lanes)
            {
                notes.TryGetModule(lane, out var m);
                double target = Math.Atan2(sumSin[m.device], sumCos[m.device]);
                int best = -1;
                double bestDist = double.MaxValue;
                for (int k = 0; k < pairs.Count; k++)
                {
                    if (used[k]) continue;
                    double a = Math.Atan2(pairs[k].touch.x, pairs[k].touch.z);
                    double d = Math.Abs(Math.IEEERemainder(a - target, 2 * Math.PI));
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = k;
                    }
                }
                if (best < 0) break;
                used[best] = true;
                var (t, con) = pairs[best];
                Rotate(con.rx, con.ry, con.rz, connectHalfLengthM, out float hx, out float hy, out float hz);
                result.Add(new ModulePlacement
                {
                    lane = lane,
                    mugyu = new[] { t.x, t.y, t.z },
                    connectFrom = new[] { con.x - hx, con.y - hy, con.z - hz },
                    connectTo = new[] { con.x + hx, con.y + hy, con.z + hz },
                });
            }
            return result;
        }

        /// <summary>Unity の Quaternion.Euler(rx, ry, rz) * (len, 0, 0)（z → x → y の順に回す）．</summary>
        public static void Rotate(float rxDeg, float ryDeg, float rzDeg, float len, out float x, out float y, out float z)
        {
            double rx = rxDeg * Math.PI / 180, ry = ryDeg * Math.PI / 180, rz = rzDeg * Math.PI / 180;
            double vx = len * Math.Cos(rz), vy = len * Math.Sin(rz), vz = 0;
            double y1 = vy * Math.Cos(rx) - vz * Math.Sin(rx), z1 = vy * Math.Sin(rx) + vz * Math.Cos(rx);
            double x2 = vx * Math.Cos(ry) + z1 * Math.Sin(ry), z2 = -vx * Math.Sin(ry) + z1 * Math.Cos(ry);
            x = (float)x2;
            y = (float)y1;
            z = (float)z2;
        }
    }
}
