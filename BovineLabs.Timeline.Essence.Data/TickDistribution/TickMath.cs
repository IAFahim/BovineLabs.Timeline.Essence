using Unity.Mathematics;

namespace BovineLabs.Timeline.Essence.Data
{
    public static class TickMath
    {
        public static bool TryAdvance(in TimelineEssenceTickData data, double localTimeSeconds, bool justActivated,
            ref TimelineEssenceTickState tickState, out int delta)
        {
            delta = 0;

            if (justActivated)
                tickState.Fired = 0;

            if (data.TickCount <= 0 || !data.Curve.IsCreated)
                return false;

            var t = data.Duration > 0f ? math.saturate((float)localTimeSeconds / data.Duration) : 1f;
            var target = math.clamp((int)math.round(data.Curve.Value.Evaluate(t) * data.TickCount), 0,
                data.TickCount);

            delta = target - tickState.Fired;
            if (delta <= 0)
            {
                delta = 0;
                return false;
            }

            tickState.Fired = target;
            return true;
        }
    }
}
