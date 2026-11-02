using System.Collections.Generic;
using Arte.Rhythm.Gameplay;
using Arte.Rhythm.Hardware;
using NUnit.Framework;

namespace Arte.Rhythm.Tests
{
    /// <summary>VR の手の位置からノーツモジュールへのタッチを作る．</summary>
    public class ModuleTouchDetectorTests
    {
        // レーン 3 のむぎゅっとが +z 方向 1.2m・高さ 1.0m にある（面は中心 = -z を向く）
        static ModuleTouchDetector Detector() =>
            new ModuleTouchDetector(new[] { new ModulePlacement { lane = 3, mugyu = new[] { 0f, 1f, 1.2f } } });

        static List<int> Step(ModuleTouchDetector d, params float[] points)
        {
            var pressed = new List<int>();
            d.Update(points, pressed);
            return pressed;
        }

        [Test]
        public void TouchingFromFrontPressesOnce()
        {
            var d = Detector();
            Assert.IsEmpty(Step(d, 0, 1, 0.8f), "40cm 手前は届いていない");
            CollectionAssert.AreEqual(new[] { 3 }, Step(d, 0, 1, 1.1f), "10cm 手前で触れた");
            Assert.IsEmpty(Step(d, 0.02f, 1.01f, 1.15f), "触れたままでは押し直さない");
            Assert.IsEmpty(Step(d, 0, 1, 0.8f), "離した");
            CollectionAssert.AreEqual(new[] { 3 }, Step(d, 0, 1, 1.12f), "もう一度触れた");
        }

        [Test]
        public void OutsideTheBoxDoesNotTouch()
        {
            var d = Detector();
            Assert.IsEmpty(Step(d, 0.3f, 1, 1.1f), "横に 30cm ずれている");
            Assert.IsEmpty(Step(d, 0, 1.3f, 1.1f), "上に 30cm ずれている");
            Assert.IsEmpty(Step(d, 0, 1, 1.5f), "裏から 30cm");
        }

        [Test]
        public void EdgeJitterDoesNotRetrigger()
        {
            var d = Detector();
            CollectionAssert.AreEqual(new[] { 3 }, Step(d, 0.11f, 1, 1.1f));
            // 箱の縁（12cm）を少し出入りしても，余裕（3cm）の内なら触れたまま
            Assert.IsEmpty(Step(d, 0.135f, 1, 1.1f));
            Assert.IsEmpty(Step(d, 0.11f, 1, 1.1f));
        }

        [Test]
        public void EitherHandCanTouch()
        {
            var d = Detector();
            CollectionAssert.AreEqual(new[] { 3 }, Step(d, -1, 0, 0, 0, 1, 1.1f), "2 つ目の点（右手）で触れた");
        }
    }
}
