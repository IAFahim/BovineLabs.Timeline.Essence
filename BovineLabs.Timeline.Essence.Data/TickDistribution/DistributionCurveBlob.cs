using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Essence.Data
{
    public struct DistributionCurveBlob
    {
        public BlobArray<float> Cdf;

        public float Evaluate(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            var maxIndex = Cdf.Length - 1;
            var floatIndex = t * maxIndex;
            var index = (int)math.floor(floatIndex);

            if (index >= maxIndex) return Cdf[maxIndex];

            return math.lerp(Cdf[index], Cdf[index + 1], floatIndex - index);
        }
    }
}