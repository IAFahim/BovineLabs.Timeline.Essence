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

            // A fresh non-looping clip's frame AFTER activation sits at ticksPastStart == one full frame
            // (== deltaTicks at scale 1). The window upper bound is EXCLUSIVE and must NOT admit it, or the
            // edge re-arms and the one-shot event fires a spurious second time (the "sometimes fires twice"
            // flakiness). A genuine director-loop wrap always re-enters at a remainder strictly < one frame,
            // so `< windowTicks` still re-arms real loops. ponytail: scale!=1 with non-integer deltaTicks*scale
            // can still slip the N+1 frame under ceil(); the fully-robust cure is per-clip wrap detection
            // (persist previous localTime, rearm on decrease) — not worth the extra component for scale==1 clips.
            var windowTicks = (long)math.ceil(deltaTicks * scale);
            return ticksPastStart >= 0 && ticksPastStart < windowTicks;
        }
    }
}
