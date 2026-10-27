using System;
using System.Collections.Generic;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Net;

namespace Arte.Rhythm.Viewer
{
    /// <summary>
    /// Viewer が描く LED の一覧（位置と，LED に送った値のどこを読むか）と，部屋を照らす光源のまとめ方．Unity に依存しない．
    ///
    /// - LED: 演出用は設営で外したストリップを除いた全部，ノーツ用は設営データに置き場所（modules）があるレーンだけ．
    ///   座標は m で，リングの中心が原点，y が上（PerfLayout と同じ）．
    /// - 光源: 床や暗幕を照らす計算を軽くするため，近くの LED をまとめる（演出用はストリップを segmentsPerStrip 個に分ける，
    ///   むぎゅっとは 1 個，つながるノーツ用テープは 3 個）．まとめた光源の明るさは中の LED の光量の合計．
    /// </summary>
    public sealed class LedSceneModel
    {
        public const int ConnectSegments = 3;

        readonly List<float> positions = new List<float>();
        readonly List<int> device = new List<int>(), pin = new List<int>(), offset = new List<int>();
        readonly List<bool> swapRG = new List<bool>();
        readonly List<int> clusterStart = new List<int>();   // 光源ごとの最初の LED（次の光源の開始まで）

        public int LedCount => device.Count;
        public int ClusterCount => clusterStart.Count;
        /// <summary>LED の位置（x, y, z の順に LedCount × 3）．</summary>
        public float[] Positions { get; }
        /// <summary>光源の位置（x, y, z の順に ClusterCount × 3）．</summary>
        public float[] ClusterPositions { get; }

        public LedSceneModel(PerfLayout perf, NoteLayout notes, int deviceCount, int segmentsPerStrip = 4)
        {
            int rows = perf.LedsPerStrip;
            segmentsPerStrip = Math.Max(1, Math.Min(segmentsPerStrip, rows));
            for (int column = 0; column < perf.ColumnCount; column++)
            {
                var s = perf.GetStrip(column);
                if (!s.Installed || s.Address.device >= deviceCount) continue;
                for (int row = 0; row < rows; row++)
                {
                    if (row * segmentsPerStrip / rows != (row - 1) * segmentsPerStrip / rows || row == 0) clusterStart.Add(LedCount);
                    perf.GetLedPosition(column, row, out float x, out float y, out float z);
                    Add(x, y, z, s.Address.device, s.Address.pin, (s.Address.slot * rows + perf.WireIndexOf(column, row)) * 3, false);
                }
            }
            foreach (int lane in notes.Lanes)
            {
                if (!notes.TryGetPlacement(lane, out var place) || !notes.TryGetModule(lane, out var m) || m.device >= deviceCount) continue;
                clusterStart.Add(LedCount);
                for (int row = 0; row < notes.MuguRows; row++)
                {
                    for (int col = 0; col < notes.MuguColumns; col++)
                    {
                        ModuleGeometry.MuguLed(place, row, col, notes.MuguRows, notes.MuguColumns, notes.MuguSizeM, out float x, out float y, out float z);
                        Add(x, y, z, m.device, notes.NoteOutputPin, notes.MuguLedIndex(m.module, row, col) * 3, false);
                    }
                }
                int n = notes.ConnectLeds;
                for (int i = 0; i < n; i++)
                {
                    if (i * ConnectSegments / n != (i - 1) * ConnectSegments / n || i == 0) clusterStart.Add(LedCount);
                    ModuleGeometry.ConnectLed(place, i, n, out float x, out float y, out float z);
                    Add(x, y, z, m.device, notes.NoteOutputPin, notes.ConnectLedIndex(m.module, i) * 3, notes.ConnectSwapRG);
                }
            }
            Positions = positions.ToArray();
            ClusterPositions = new float[ClusterCount * 3];
            for (int c = 0; c < ClusterCount; c++)
            {
                int from = clusterStart[c], to = c + 1 < ClusterCount ? clusterStart[c + 1] : LedCount;
                for (int k = from; k < to; k++)
                    for (int a = 0; a < 3; a++) ClusterPositions[c * 3 + a] += Positions[k * 3 + a] / (to - from);
            }
        }

        void Add(float x, float y, float z, int dev, int p, int off, bool swap)
        {
            positions.Add(x);
            positions.Add(y);
            positions.Add(z);
            device.Add(dev);
            pin.Add(p);
            offset.Add(off);
            swapRG.Add(swap);
        }

        /// <summary>
        /// LED に送った値（output，明るさ・曲線を掛けた後）から各 LED の光量（リニア，RGB）を linear に書く（LedCount × 3）．
        /// つながるノーツ用テープは R と G を入れ替えて送っているので戻す．
        /// </summary>
        public void ComputeLinear(LedFrame output, OutputSection outCfg, float exposure, float[] linear)
        {
            float gamma = outCfg.firmwareGamma;
            float lr = Channel(outCfg, 0) * exposure, lg = Channel(outCfg, 1) * exposure, lb = Channel(outCfg, 2) * exposure;
            for (int k = 0; k < LedCount; k++)
            {
                var o = output.Pin(device[k], pin[k]);
                int off = offset[k];
                if (off + 2 >= o.Length)
                {
                    linear[k * 3] = linear[k * 3 + 1] = linear[k * 3 + 2] = 0;
                    continue;
                }
                byte r = o[off], g = o[off + 1];
                if (swapRG[k]) (r, g) = (g, r);
                linear[k * 3] = LedOutputModel.OutputToLinear(r, gamma) * lr;
                linear[k * 3 + 1] = LedOutputModel.OutputToLinear(g, gamma) * lg;
                linear[k * 3 + 2] = LedOutputModel.OutputToLinear(o[off + 2], gamma) * lb;
            }
        }

        /// <summary>光源ごとに中の LED の光量を足して clusters に書く（ClusterCount × 3）．</summary>
        public void ComputeClusters(float[] linear, float[] clusters)
        {
            for (int c = 0; c < ClusterCount; c++)
            {
                int from = clusterStart[c], to = c + 1 < ClusterCount ? clusterStart[c + 1] : LedCount;
                float r = 0, g = 0, b = 0;
                for (int k = from; k < to; k++)
                {
                    r += linear[k * 3];
                    g += linear[k * 3 + 1];
                    b += linear[k * 3 + 2];
                }
                clusters[c * 3] = r;
                clusters[c * 3 + 1] = g;
                clusters[c * 3 + 2] = b;
            }
        }

        static float Channel(OutputSection o, int i) =>
            o.channelLuminance != null && o.channelLuminance.Length > i ? o.channelLuminance[i] : 1f;
    }
}
