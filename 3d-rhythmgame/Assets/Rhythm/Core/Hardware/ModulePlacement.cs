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
        ///
        /// 決まりごと（ユーザー確認済み）: モジュールは演出用 LED と同じく，リングの内側から見て左から右へ番号（レーン）が進む．
        /// つながるノーツ用テープはむぎゅっと k から次のむぎゅっとへ伸び，0 番目の LED は若い番号（k）の側．
        ///
        /// 旧版はレーンと置き場所の対応を持っていなかった（ノーツが来るたびに順番に使い回していた）ので，ファイルの
        /// むぎゅっと（touch_notes）の位置から，各レーンのモジュールがつながるデバイスの演出用ストリップに近いものを選び，
        /// 選んだ位置を左から右の順にレーンへ割り当てる（仮）．テープはファイルの向きを使わず，次のレーンのむぎゅっとへ向ける．
        /// </summary>
        public static List<ModulePlacement> FromLegacy(IReadOnlyList<LegacyPlacedObject> objects, PerfLayout perf, NoteLayout notes,
                                                       float connectLengthM = 0.495f, float connectGapM = 0.04f)
        {
            var spots = new List<LegacyPlacedObject>();
            foreach (var o in objects)
                if (o.prefabName == "touch_notes") spots.Add(o);

            // デバイスごとの演出用ストリップの向き（平均）と，列番号が増える向き（内側から見て左 → 右）
            var sumSin = new double[perf.DeviceCount];
            var sumCos = new double[perf.DeviceCount];
            for (int c = 0; c < perf.ColumnCount; c++)
            {
                double a = ColumnAngle(perf, c);
                int d = perf.GetStrip(c).Address.device;
                sumSin[d] += Math.Sin(a);
                sumCos[d] += Math.Cos(a);
            }
            int ringStep = Math.Min(3, perf.ColumnCount - 1);   // 同じ輪の隣の列（列は輪を順に回る）
            double dir = Math.Sign(Math.IEEERemainder(ColumnAngle(perf, ringStep) - ColumnAngle(perf, 0), 2 * Math.PI));
            if (dir == 0) dir = 1;

            // 各レーンに近い位置を選ぶ
            var lanes = new List<int>(notes.Lanes);
            var used = new bool[spots.Count];
            var chosen = new List<int>();
            foreach (int lane in lanes)
            {
                notes.TryGetModule(lane, out var m);
                double target = Math.Atan2(sumSin[m.device], sumCos[m.device]);
                int best = -1;
                double bestDist = double.MaxValue;
                for (int k = 0; k < spots.Count; k++)
                {
                    if (used[k]) continue;
                    double d = Math.Abs(Math.IEEERemainder(SpotAngle(spots[k]) - target, 2 * Math.PI));
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = k;
                    }
                }
                if (best < 0) break;
                used[best] = true;
                chosen.Add(best);
            }

            // 選んだ位置を，最初のレーンの近くから左 → 右の順に並べてレーン順に割り当てる
            if (lanes.Count == 0 || chosen.Count == 0) return new List<ModulePlacement>();
            notes.TryGetModule(lanes[0], out var first);
            double origin = Math.Atan2(sumSin[first.device], sumCos[first.device]) - dir * Math.PI / 8;
            chosen.Sort((a, b) => Along(spots[a], origin, dir).CompareTo(Along(spots[b], origin, dir)));

            var result = new List<ModulePlacement>();
            for (int i = 0; i < chosen.Count; i++)
            {
                var t = spots[chosen[i]];
                var next = spots[chosen[(i + 1) % chosen.Count]];
                float ux = next.x - t.x, uy = next.y - t.y, uz = next.z - t.z;
                float dist = (float)Math.Sqrt(ux * ux + uy * uy + uz * uz);
                if (dist > 1e-6f)
                {
                    ux /= dist;
                    uy /= dist;
                    uz /= dist;
                }
                float len = Math.Max(0.05f, Math.Min(connectLengthM, dist - 2 * connectGapM));
                var from = new[] { t.x + ux * connectGapM, t.y + uy * connectGapM, t.z + uz * connectGapM };
                result.Add(new ModulePlacement
                {
                    lane = lanes[i],
                    mugyu = new[] { t.x, t.y, t.z },
                    connectFrom = from,
                    connectTo = new[] { from[0] + ux * len, from[1] + uy * len, from[2] + uz * len },
                });
            }
            return result;
        }

        static double ColumnAngle(PerfLayout perf, int column)
        {
            perf.GetLedPosition(column, 0, out float x, out _, out float z);
            return Math.Atan2(x, z);
        }

        static double SpotAngle(LegacyPlacedObject o) => Math.Atan2(o.x, o.z);

        // origin から dir の向きに測った角度（0〜2π）
        static double Along(LegacyPlacedObject o, double origin, double dir)
        {
            double a = (SpotAngle(o) - origin) * dir;
            a %= 2 * Math.PI;
            return a < 0 ? a + 2 * Math.PI : a;
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
