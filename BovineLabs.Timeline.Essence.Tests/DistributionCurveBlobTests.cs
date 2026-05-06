using BovineLabs.Essence.Data;
using BovineLabs.Testing;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Tests
{
    public class DistributionCurveBlobTests : ECSTestsFixture
    {
        private BlobAssetReference<DistributionCurveBlob> CreateBlob(params float[] cdfValues)
        {
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<DistributionCurveBlob>();
            var array = builder.Allocate(ref root.Cdf, cdfValues.Length);

            for (var i = 0; i < cdfValues.Length; i++)
                array[i] = cdfValues[i];

            return builder.CreateBlobAssetReference<DistributionCurveBlob>(Allocator.Persistent);
        }

        [Test]
        public void Evaluate_Zero_ReturnsZero()
        {
            using var blob = CreateBlob(0f, 0.5f, 1f);
            Assert.AreEqual(0f, blob.Value.Evaluate(0f));
        }

        [Test]
        public void Evaluate_One_ReturnsOne()
        {
            using var blob = CreateBlob(0f, 0.5f, 1f);
            Assert.AreEqual(1f, blob.Value.Evaluate(1f));
        }

        [Test]
        public void Evaluate_Negative_ReturnsZero()
        {
            using var blob = CreateBlob(0f, 0.5f, 1f);
            Assert.AreEqual(0f, blob.Value.Evaluate(-1f));
        }

        [Test]
        public void Evaluate_AboveOne_ReturnsOne()
        {
            using var blob = CreateBlob(0f, 0.5f, 1f);
            Assert.AreEqual(1f, blob.Value.Evaluate(2f));
        }

        [Test]
        public void Evaluate_Midpoint_InterpolatesLinearly()
        {
            using var blob = CreateBlob(0f, 1f);
            var result = blob.Value.Evaluate(0.5f);
            Assert.AreEqual(0.5f, result, 0.0001f);
        }

        [Test]
        public void Evaluate_ExactSamplePoint_ReturnsCdfValue()
        {
            using var blob = CreateBlob(0f, 0.25f, 0.5f, 0.75f, 1f);
            Assert.AreEqual(0.25f, blob.Value.Evaluate(0.25f), 0.0001f);
            Assert.AreEqual(0.5f, blob.Value.Evaluate(0.5f), 0.0001f);
            Assert.AreEqual(0.75f, blob.Value.Evaluate(0.75f), 0.0001f);
        }

        [Test]
        public void Evaluate_BetweenSamples_Interpolates()
        {
            using var blob = CreateBlob(0f, 0f, 1f, 1f);
            var result = blob.Value.Evaluate(0.5f);
            Assert.AreEqual(0.5f, result, 0.0001f);
        }

        [Test]
        public void Evaluate_SingleElement_Clamps()
        {
            using var blob = CreateBlob(0.42f);
            Assert.AreEqual(0f, blob.Value.Evaluate(0f));
            Assert.AreEqual(0.42f, blob.Value.Evaluate(0.5f));
            Assert.AreEqual(1f, blob.Value.Evaluate(1f));
        }

        [Test]
        public void Evaluate_UniformLinear_MatchesIdentity()
        {
            using var blob = CreateBlob(0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f);
            for (var t = 0f; t <= 1f; t += 0.1f)
            {
                var result = blob.Value.Evaluate(t);
                Assert.AreEqual(t, result, 0.01f, $"Failed at t={t}");
            }
        }

        [Test]
        public void Evaluate_TwoElements_HalfPoint()
        {
            using var blob = CreateBlob(0.2f, 0.8f);
            var result = blob.Value.Evaluate(0.5f);
            Assert.AreEqual(0.5f, result, 0.0001f);
        }

        [Test]
        public void Evaluate_EmptyCdf_ReturnsZero()
        {
            using var blob = CreateBlob();
            Assert.AreEqual(0f, blob.Value.Evaluate(0.5f));
        }

        [Test]
        public void Evaluate_SingleElementCdf_ReturnsOne()
        {
            using var blob = CreateBlob(1f);
            Assert.AreEqual(1f, blob.Value.Evaluate(0.5f));
        }
    }
}