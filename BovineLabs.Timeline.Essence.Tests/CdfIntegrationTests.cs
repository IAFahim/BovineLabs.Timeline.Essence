using BovineLabs.Essence.Data;
using NUnit.Framework;
using Unity.Collections;

namespace BovineLabs.Timeline.Essence.Tests
{
    public class CdfIntegrationTests
    {
        private static float[] Integrate(params float[] density)
        {
            using var src = new NativeArray<float>(density, Allocator.Temp);
            using var cdf = new NativeArray<float>(density.Length, Allocator.Temp);
            CdfIntegration.BuildNormalizedCdf(src, cdf);
            return cdf.ToArray();
        }

        [Test]
        public void Uniform_ProducesLinearRamp()
        {
            var cdf = Integrate(1f, 1f, 1f, 1f, 1f);

            Assert.AreEqual(0f, cdf[0], 0.0001f);
            Assert.AreEqual(1f, cdf[cdf.Length - 1], 0.0001f);
            Assert.AreEqual(0.25f, cdf[1], 0.0001f);
            Assert.AreEqual(0.5f, cdf[2], 0.0001f);
            Assert.AreEqual(0.75f, cdf[3], 0.0001f);
        }

        [Test]
        public void ZeroDensity_FallsBackToIdentity()
        {
            var cdf = Integrate(0f, 0f, 0f, 0f, 0f);

            for (var i = 0; i < cdf.Length; i++)
                Assert.AreEqual(i / 4f, cdf[i], 0.0001f);
        }

        [Test]
        public void FrontLoaded_RisesFasterEarly()
        {
            var cdf = Integrate(4f, 3f, 2f, 1f, 0f);

            Assert.AreEqual(0f, cdf[0], 0.0001f);
            Assert.AreEqual(1f, cdf[cdf.Length - 1], 0.0001f);
            Assert.Greater(cdf[1], 0.25f);
            Assert.Greater(cdf[2], 0.5f);
        }

        [Test]
        public void Endpoints_AlwaysZeroAndOne()
        {
            var cdf = Integrate(0.2f, 5f, 0.1f, 3f, 1f);

            Assert.AreEqual(0f, cdf[0], 0.0001f);
            Assert.AreEqual(1f, cdf[cdf.Length - 1], 0.0001f);
        }

        [Test]
        public void Monotonic_NeverDecreases()
        {
            var cdf = Integrate(0.5f, 2f, 1f, 4f, 0.3f, 1f);

            for (var i = 1; i < cdf.Length; i++)
                Assert.GreaterOrEqual(cdf[i], cdf[i - 1]);
        }
    }
}
