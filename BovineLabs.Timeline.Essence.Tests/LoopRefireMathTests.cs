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
        public void PastStartBeyondWindow_ReturnsFalse()
        {
            Assert.IsFalse(LoopRefireMath.ShouldRearm(10, 11, 1.0));
        }

        // The frame AFTER a fresh non-looping clip's activation: ticksPastStart == one full frame == deltaTicks.
        // Must NOT re-arm, else the one-shot event fires a spurious second time (the reported flakiness).
        [Test]
        public void FreshActivationFrameAfterEnter_DoesNotRearm()
        {
            Assert.IsFalse(LoopRefireMath.ShouldRearm(10, 10, 1.0));
        }

        // A genuine director-loop wrap re-enters at a remainder strictly < one frame; must still re-arm.
        [Test]
        public void GenuineLoopWrap_StillRearms()
        {
            Assert.IsTrue(LoopRefireMath.ShouldRearm(10, 0, 1.0));
            Assert.IsTrue(LoopRefireMath.ShouldRearm(10, 9, 1.0));
        }

        [Test]
        public void NegativeDelta_AbsFolds()
        {
            Assert.AreEqual(
                LoopRefireMath.ShouldRearm(10, 10, 1.0),
                LoopRefireMath.ShouldRearm(-10, 10, 1.0));
        }

        // Frozen scale (0) → window collapses to 0 → a non-advancing clip never re-arms.
        [Test]
        public void ScaleZero_NeverRearms()
        {
            Assert.IsFalse(LoopRefireMath.ShouldRearm(10, 0, 0.0));
            Assert.IsFalse(LoopRefireMath.ShouldRearm(10, 1, 0.0));
        }

        // --- Edge safety (proven-correct or at least no spurious fire) ---

        // Reverse playback (negative clip scale): window goes negative, the ticksPastStart>=0 guard wins.
        // A reverse-played event clip never re-fires — undefined semantics, but SAFE (no spurious fire, no crash).
        [Test]
        public void NegativeScale_NeverRearms()
        {
            Assert.IsFalse(LoopRefireMath.ShouldRearm(10, 0, -1.0));
            Assert.IsFalse(LoopRefireMath.ShouldRearm(10, 5, -1.0));
        }

        // Integer scale > 1 (frameAdvance stays integer ⇒ still exact, same as scale 1):
        // the post-activation frame (ticksPastStart == 20) is excluded; a real wrap (19) still re-arms.
        [Test]
        public void IntegerScaleTwo_ExcludesNextFrame_KeepsWrap()
        {
            Assert.IsFalse(LoopRefireMath.ShouldRearm(10, 20, 2.0)); // N+1, must not double-fire
            Assert.IsTrue(LoopRefireMath.ShouldRearm(10, 19, 2.0));  // wrap remainder, must re-arm
            Assert.IsTrue(LoopRefireMath.ShouldRearm(10, 0, 2.0));
        }

        // Far past the start (steady-state mid-clip) never re-arms.
        [Test]
        public void DeepInsideClip_NeverRearms()
        {
            Assert.IsFalse(LoopRefireMath.ShouldRearm(10, 5000, 1.0));
        }
    }
}
