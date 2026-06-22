using BovineLabs.Essence.Data;
using BovineLabs.Timeline.Essence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Tests
{
    public class TickMathTests
    {
        private static BlobAssetReference<DistributionCurveBlob> UniformCdf(int n)
        {
            using var b = new BlobBuilder(Allocator.Temp);
            ref var root = ref b.ConstructRoot<DistributionCurveBlob>();
            var cdf = b.Allocate(ref root.Cdf, n);
            for (var i = 0; i < n; i++)
                cdf[i] = i / (float)(n - 1);
            return b.CreateBlobAssetReference<DistributionCurveBlob>(Allocator.Persistent);
        }

        private static TimelineEssenceTickData Data(BlobAssetReference<DistributionCurveBlob> curve, int tickCount,
            float duration)
        {
            return new TimelineEssenceTickData
            {
                Mode = EssenceTickMode.Event,
                ValuePerTick = 1,
                TickCount = tickCount,
                Duration = duration,
                Curve = curve,
            };
        }

        [Test]
        public void JustActivated_ResetsFired()
        {
            using var curve = UniformCdf(8);
            var data = Data(curve, 10, 1f);
            var state = new TimelineEssenceTickState { Fired = 10 };

            var advanced = TickMath.TryAdvance(data, 0.0, true, ref state, out var delta);

            Assert.IsFalse(advanced, "no forward delta at t=0 after reset");
            Assert.AreEqual(0, delta);
            Assert.AreEqual(0, state.Fired, "Fired reset on activation");
        }

        [Test]
        public void TickCountZero_ReturnsFalse()
        {
            using var curve = UniformCdf(8);
            var data = Data(curve, 0, 1f);
            var state = new TimelineEssenceTickState();

            Assert.IsFalse(TickMath.TryAdvance(data, 0.5, false, ref state, out var delta));
            Assert.AreEqual(0, delta);
        }

        [Test]
        public void UncreatedCurve_ReturnsFalse()
        {
            var data = Data(default, 10, 1f);
            var state = new TimelineEssenceTickState();

            Assert.IsFalse(TickMath.TryAdvance(data, 0.5, false, ref state, out var delta));
            Assert.AreEqual(0, delta);
        }

        [Test]
        public void DeltaNotPositive_ReturnsFalse()
        {
            using var curve = UniformCdf(8);
            var data = Data(curve, 10, 1f);
            var state = new TimelineEssenceTickState { Fired = 10 };

            Assert.IsFalse(TickMath.TryAdvance(data, 1.0, false, ref state, out var delta));
            Assert.AreEqual(0, delta);
            Assert.AreEqual(10, state.Fired, "Fired not advanced backwards");
        }

        [Test]
        public void ForwardProgress_AdvancesFired()
        {
            using var curve = UniformCdf(8);
            var data = Data(curve, 10, 1f);
            var state = new TimelineEssenceTickState { Fired = 0 };

            var advanced = TickMath.TryAdvance(data, 0.5, false, ref state, out var delta);

            Assert.IsTrue(advanced);
            Assert.AreEqual(5, delta);
            Assert.AreEqual(5, state.Fired);
        }

        [Test]
        public void DurationNotPositive_EvaluatesAtEnd()
        {
            using var curve = UniformCdf(8);
            var data = Data(curve, 10, 0f);
            var state = new TimelineEssenceTickState { Fired = 0 };

            var advanced = TickMath.TryAdvance(data, 0.0, false, ref state, out var delta);

            Assert.IsTrue(advanced);
            Assert.AreEqual(10, delta);
            Assert.AreEqual(10, state.Fired);
        }
    }
}
