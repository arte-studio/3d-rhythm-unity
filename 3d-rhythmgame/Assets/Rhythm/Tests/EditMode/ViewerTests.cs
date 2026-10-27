using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Net;
using Arte.Rhythm.Viewer;
using NUnit.Framework;

namespace Arte.Rhythm.Tests
{
    /// <summary>Viewer（LED の一覧と光源のまとめ方，PIXELS のミラー受信）．</summary>
    public class ViewerTests
    {
        HardwareConfig config;
        PerfLayout perf;
        NoteLayout notes;

        [SetUp]
        public void Load()
        {
            string dir = TestData.HardwareDir();
            config = HardwareJson.LoadConfig(File.ReadAllText(Path.Combine(dir, "hardware.json")));
            var setup = HardwareJson.LoadSetup(File.ReadAllText(Path.Combine(dir, "setups", "kodaisai-1101.json")));
            perf = new PerfLayout(config, setup);
            notes = new NoteLayout(config, setup);
        }

        [Test]
        public void SceneModelCountsInstalledLedsAndClusters()
        {
            var model = new LedSceneModel(perf, notes, 8, segmentsPerStrip: 4);
            int strips = 0;
            for (int c = 0; c < perf.ColumnCount; c++)
                if (perf.GetStrip(c).Installed) strips++;
            Assert.AreEqual(86, strips, "kodaisai-1101 は 4 本外している");
            Assert.AreEqual(strips * 120 + 11 * (64 + 30), model.LedCount);
            Assert.AreEqual(strips * 4 + 11 * (1 + LedSceneModel.ConnectSegments), model.ClusterCount);

            // 光源の位置は中の LED の平均（ストリップの最初の 1/4 は下の方）
            Assert.Less(model.ClusterPositions[1], 0.5f);
            Assert.Greater(model.ClusterPositions[3 * 3 + 1], 1.5f);
        }

        [Test]
        public void LedsFaceTheRingCenter()
        {
            var model = new LedSceneModel(perf, notes, 8, segmentsPerStrip: 4);
            for (int k = 0; k < model.LedCount; k += 97)
            {
                float x = model.Positions[k * 3], z = model.Positions[k * 3 + 2];
                float nx = model.Normals[k * 3], ny = model.Normals[k * 3 + 1], nz = model.Normals[k * 3 + 2];
                Assert.AreEqual(1f, Math.Sqrt(nx * nx + ny * ny + nz * nz), 1e-4);
                Assert.AreEqual(0f, ny, "水平");
                Assert.Less(nx * x + nz * z, 0, $"LED {k} は中心を向く");
            }
            for (int c = 0; c < model.ClusterCount; c++)
            {
                float x = model.ClusterPositions[c * 3], z = model.ClusterPositions[c * 3 + 2];
                Assert.Less(model.ClusterNormals[c * 3] * x + model.ClusterNormals[c * 3 + 2] * z, 0);
            }
        }

        [Test]
        public void RoomSurfacesHaveThreeFloors()
        {
            Assert.AreEqual(3, RoomSurfaces.Floors.Length);
            Assert.AreEqual("school-white", RoomSurfaces.Find(RoomSurfaces.Floors, "School-White").Name);
            Assert.AreEqual("carpet-gray", RoomSurfaces.Find(RoomSurfaces.Floors, "unknown").Name, "知らない名前は最初のもの");
            var setup = HardwareJson.LoadSetup(File.ReadAllText(Path.Combine(TestData.HardwareDir(), "setups", "kodaisai-1101.json")));
            Assert.AreEqual("blackout", setup.room.curtain);
        }

        [Test]
        public void SceneModelLinearAndClusters()
        {
            var model = new LedSceneModel(perf, notes, 8, segmentsPerStrip: 4);
            var output = LedFrame.FromLayout(perf, notes, 8);
            // 列 0 の一番下の LED（配線上の位置は PerfLayout が決める）を (255, 0, 0)
            var s = perf.GetStrip(0);
            output.Pin(s.Address.device, s.Address.pin)[(s.Address.slot * 120 + perf.WireIndexOf(0, 0)) * 3] = 255;
            // レーン 0 のつながるノーツ用テープの 0 番目に (R, G, B) = (10, 20, 30) を送った（R と G を入れ替えて送るので 20, 10, 30）
            notes.TryGetModule(0, out var m);
            var np = output.Pin(m.device, notes.NoteOutputPin);
            int ci = notes.ConnectLedIndex(m.module, 0) * 3;
            np[ci] = 20;
            np[ci + 1] = 10;
            np[ci + 2] = 30;

            var linear = new float[model.LedCount * 3];
            model.ComputeLinear(output, config.output, 1f, linear);
            Assert.AreEqual(1f, linear[0], 1e-6);
            Assert.AreEqual(0f, linear[1]);
            var clusters = new float[model.ClusterCount * 3];
            model.ComputeClusters(linear, clusters);
            Assert.AreEqual(1f, clusters[0], 1e-6, "最初の光源は列 0 の下の LED を含む");

            // テープの LED を探して色が戻っているか
            bool found = false;
            for (int k = 0; k < model.LedCount; k++)
            {
                if (Math.Abs(linear[k * 3] - 10 / 255f) < 1e-6 && Math.Abs(linear[k * 3 + 1] - 20 / 255f) < 1e-6) found = true;
            }
            Assert.IsTrue(found, "つながるノーツ用テープは R と G を戻して表示する");
        }

        [Test]
        public void MirrorReceivesWhatDevicesWouldShow()
        {
            var output = LedFrame.FromLayout(perf, notes, 8);
            using var mirror = new MirrorReceiver(output, 0);
            var watch = Stopwatch.StartNew();
            var options = new LinkOptions { HostPort = 0, DeviceCount = 8 };
            options.MirrorTargets.Add(new IPEndPoint(IPAddress.Loopback, mirror.Port));
            using var host = new LinkHost(options, () => (ulong)(watch.Elapsed.Ticks / 10) + 1);
            host.Start();

            var frame = LedFrame.FromLayout(perf, notes, 8);
            frame.Pin(2, 1)[33] = 200;
            frame.Pin(7, 3)[5] = 128;
            host.SendFrame(frame);

            // 実機がつながっていなくても全デバイス分が届く
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline && output.Pin(7, 3)[5] == 0)
            {
                mirror.Poll();
                Thread.Sleep(5);
            }
            var c = options.Config;
            Assert.AreEqual(LedOutputModel.ApplyBrightness(200, c.BrightnessPerf), output.Pin(2, 1)[33]);
            Assert.AreEqual(LedOutputModel.ApplyBrightness(128, c.BrightnessNote), output.Pin(7, 3)[5]);
            Assert.AreEqual(0, mirror.PacketsBad);
        }
    }
}
