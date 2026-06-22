using BovineLabs.Essence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Essence.Tests
{
    public class TimelineEssenceTickTests
    {
        private static BlobAssetReference<DistributionCurveBlob> Cdf(params float[] values)
        {
            using var b = new BlobBuilder(Allocator.Temp);
            ref var root = ref b.ConstructRoot<DistributionCurveBlob>();
            var cdf = b.Allocate(ref root.Cdf, values.Length);
            for (var i = 0; i < values.Length; i++)
                cdf[i] = values[i];
            return b.CreateBlobAssetReference<DistributionCurveBlob>(Allocator.Persistent);
        }

        private static BlobAssetReference<DistributionCurveBlob> UniformCdf(int n)
        {
            var v = new float[n];
            for (var i = 0; i < n; i++)
                v[i] = i / (float)(n - 1);
            return Cdf(v);
        }

        private static int Simulate(BlobAssetReference<DistributionCurveBlob> curve, int total, int steps,
            out int minDelta)
        {
            minDelta = int.MaxValue;
            var fired = 0;
            for (var s = 0; s <= steps; s++)
            {
                var t = s / (float)steps;
                var target = math.clamp((int)math.round(curve.Value.Evaluate(t) * total), 0, total);
                var delta = target - fired;
                minDelta = math.min(minDelta, delta);
                fired = target;
            }

            return fired;
        }

        [Test]
        public void UniformCurveFiresExactlyTotalAndNeverBackwards()
        {
            using var curve = UniformCdf(16);
            var fired = Simulate(curve, 10, 200, out var minDelta);

            Assert.AreEqual(10, fired, "did not fire exactly the requested number of ticks");
            Assert.GreaterOrEqual(minDelta, 0, "ticks went backwards");
        }

        [Test]
        public void FrontLoadedCurveFiresEarlierThanUniform()
        {
            using var uniform = UniformCdf(5);
            using var frontLoaded = Cdf(0f, 1f, 1f, 1f, 1f);

            Assert.Greater(
                frontLoaded.Value.Evaluate(0.25f),
                uniform.Value.Evaluate(0.25f),
                "front-loaded curve should have a higher cumulative fraction early");

            Assert.AreEqual(8, Simulate(frontLoaded, 8, 200, out _));
        }
    }
}