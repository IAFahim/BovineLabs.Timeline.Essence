using BovineLabs.Timeline.Essence.Data;
using NUnit.Framework;

namespace BovineLabs.Timeline.Essence.Tests
{
    public class LoopRefireMathTests
    {
        [Test]
        public void ZeroDelta_ReturnsFalse()
        {
            Assert.IsFalse(LoopRefireMath.ShouldRearm(0, 0, 1.0));
        }

        [Test]
        public void NegativePastStart_ReturnsFalse()
        {
            Assert.IsFalse(LoopRefireMath.ShouldRearm(10, -1, 1.0));
        }

        [Test]
        public void PastStartZero_ScaleOne_ReturnsTrue()
        {
            Assert.IsTrue(LoopRefireMath.ShouldRearm(10, 0, 1.0));
        }

        [Test]
        public void PastStartAtWindow_ReturnsFalse()
        {
            Assert.IsFalse(LoopRefireMath.ShouldRearm(10, 11, 1.0));
        }

        [Test]
        public void PastStartAtWindowMinusOne_ReturnsTrue()
        {
            Assert.IsTrue(LoopRefireMath.ShouldRearm(10, 10, 1.0));
        }

        [Test]
        public void NegativeDelta_AbsFolds()
        {
            Assert.AreEqual(
                LoopRefireMath.ShouldRearm(10, 10, 1.0),
                LoopRefireMath.ShouldRearm(-10, 10, 1.0));
        }

        [Test]
        public void ScaleZero_WindowIsOne()
        {
            Assert.IsTrue(LoopRefireMath.ShouldRearm(10, 0, 0.0));
            Assert.IsFalse(LoopRefireMath.ShouldRearm(10, 1, 0.0));
        }
    }
}
