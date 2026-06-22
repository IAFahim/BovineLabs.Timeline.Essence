using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Essence.Data
{
    public static class LoopRefireMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldRearm(long deltaTicks, long ticksPastStart, double scale)
        {
            if (deltaTicks < 0) deltaTicks = -deltaTicks;

            if (deltaTicks == 0) return false;

            var windowTicks = (long)math.ceil(deltaTicks * scale) + 1;
            return ticksPastStart >= 0 && ticksPastStart < windowTicks;
        }
    }
}
