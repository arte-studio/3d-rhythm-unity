using System;
using System.Collections.Generic;
using System.IO;
using Arte.Rhythm.Hardware;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arte.Rhythm.Tests
{
    /// <summary>ノーツモジュールの置き場所（旧版の配置ファイルからの取り込みと，LED の位置）．</summary>
    public class ModulePlacementTests
    {
        PerfLayout perf;
        NoteLayout notes;
        List<ModulePlacement> placements;

        [SetUp]
        public void Load()
        {
            string dir = TestData.HardwareDir();
            var config = HardwareJson.LoadConfig(File.ReadAllText(Path.Combine(dir, "hardware.json")));
            var setup = HardwareJson.LoadSetup(File.ReadAllText(Path.Combine(dir, "setups", "kodaisai-1101.json")));
            perf = new PerfLayout(config, setup);
            notes = new NoteLayout(config, setup);
            var objects = new List<LegacyPlacedObject>();
            var root = JObject.Parse(File.ReadAllText(Path.Combine(TestData.LegacyResourcesDir(), "object_positions_kokasai.json")));
            foreach (var o in root["objects"])
            {
                objects.Add(new LegacyPlacedObject
                {
                    prefabName = (string)o["prefabName"], x = (float)o["x"], y = (float)o["y"], z = (float)o["z"],
                    rx = (float)o["rx"], ry = (float)o["ry"], rz = (float)o["rz"],
                });
            }
            placements = ModuleGeometry.FromLegacy(objects, perf, notes);
        }

        static double Angle(float[] p) => Math.Atan2(p[0], p[2]);

        [Test]
        public void LanesGoLeftToRightLikeCanvasColumns()
        {
            Assert.AreEqual(notes.Lanes.Count, placements.Count);
            // 演出の列が増える向き
            perf.GetLedPosition(0, 0, out float x0, out _, out float z0);
            perf.GetLedPosition(3, 0, out float x3, out _, out float z3);
            double dir = Math.Sign(Math.IEEERemainder(Math.Atan2(x3, z3) - Math.Atan2(x0, z0), 2 * Math.PI));
            double total = 0;
            for (int i = 0; i + 1 < placements.Count; i++)
            {
                double step = Math.IEEERemainder(Angle(placements[i + 1].mugyu) - Angle(placements[i].mugyu), 2 * Math.PI) * dir;
                Assert.Greater(step, 0, $"レーン {placements[i].lane} → {placements[i + 1].lane} が左から右に並んでいない");
                total += step;
            }
            Assert.Less(total, 2 * Math.PI, "一周を超えない");
        }

        [Test]
        public void ConnectStartsAtOwnMugyuAndPointsToNext()
        {
            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                var next = placements[(i + 1) % placements.Count];
                double dFrom = Dist(p.connectFrom, p.mugyu), dTo = Dist(p.connectTo, p.mugyu);
                Assert.Less(dFrom, dTo, $"レーン {p.lane} のテープの 0 番目は自分のむぎゅっと側");
                Assert.Less(Dist(p.connectTo, next.mugyu), Dist(p.connectFrom, next.mugyu), "次のむぎゅっとへ向かう");
            }
        }

        [Test]
        public void MuguColumnsGoLeftToRightFromInside()
        {
            var p = placements[0];
            ModuleGeometry.MuguLed(p, 0, 0, 8, 8, 0.056f, out float lx, out float ly, out float lz);
            ModuleGeometry.MuguLed(p, 0, 7, 8, 8, 0.056f, out float rx, out _, out float rz);
            ModuleGeometry.MuguLed(p, 7, 0, 8, 8, 0.056f, out _, out float ty, out _);
            // 中心から見て右 = 列番号が増える向き（演出の列と同じ向き）
            perf.GetLedPosition(0, 0, out float x0, out _, out float z0);
            perf.GetLedPosition(3, 0, out float x3, out _, out float z3);
            double dir = Math.Sign(Math.IEEERemainder(Math.Atan2(x3, z3) - Math.Atan2(x0, z0), 2 * Math.PI));
            double turn = Math.IEEERemainder(Math.Atan2(rx, rz) - Math.Atan2(lx, lz), 2 * Math.PI) * dir;
            Assert.Greater(turn, 0);
            Assert.Greater(ty, ly, "行 0 が下");
        }

        static double Dist(float[] a, float[] b) =>
            Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
    }
}
