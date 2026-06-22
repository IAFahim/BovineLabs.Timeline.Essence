using Unity.Collections;

namespace BovineLabs.Essence.Data
{
    public static class CdfIntegration
    {
        public static void BuildNormalizedCdf(in NativeArray<float> density, NativeArray<float> cdf)
        {
            var count = cdf.Length;
            if (count == 0)
                return;

            var cumulative = 0f;
            var previous = density[0];
            cdf[0] = 0f;
            for (var i = 1; i < count; i++)
            {
                var d = density[i];
                cumulative += 0.5f * (d + previous);
                cdf[i] = cumulative;
                previous = d;
            }

            if (cumulative > 0f)
            {
                NormalizeBy(cdf, cumulative);
                return;
            }

            FillIdentity(cdf);
        }

        private static void NormalizeBy(NativeArray<float> cdf, float total)
        {
            for (var i = 0; i < cdf.Length; i++)
                cdf[i] = cdf[i] / total;
        }

        private static void FillIdentity(NativeArray<float> cdf)
        {
            var maxIndex = cdf.Length - 1;
            if (maxIndex <= 0)
                return;

            for (var i = 0; i < cdf.Length; i++)
                cdf[i] = i / (float)maxIndex;
        }
    }
}
