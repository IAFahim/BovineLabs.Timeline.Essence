namespace BovineLabs.Reaction.Data.TickDistribution
{
    using Unity.Entities;
    using Unity.Mathematics;

    public struct DistributionCurveBlob
    {
        public BlobArray<float> Cdf;

        public float Evaluate(float t)
        {
            if (t <= 0f)
            {
                return 0f;
            }

            if (t >= 1f)
            {
                return 1f;
            }

            var maxIndex = this.Cdf.Length - 1;
            var floatIndex = t * maxIndex;
            var index = (int)math.floor(floatIndex);

            if (index >= maxIndex)
            {
                return this.Cdf[maxIndex];
            }

            var frac = floatIndex - index;
            return math.lerp(this.Cdf[index], this.Cdf[index + 1], frac);
        }
    }
}