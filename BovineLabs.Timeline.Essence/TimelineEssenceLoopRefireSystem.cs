using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Data.Schedular;
using BovineLabs.Timeline.Essence.Data;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Essence
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateBefore(typeof(TimelineEssenceIntrinsicSystem))]
    [UpdateBefore(typeof(TimelineEssenceEventSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct TimelineEssenceLoopRefireSystem : ISystem
    {
        /// <inheritdoc />
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new RearmJob().ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        [WithAny(typeof(TimelineEssenceIntrinsicData), typeof(TimelineEssenceEventData))]
        private partial struct RearmJob : IJobEntity
        {
            private void Execute(in LocalTime localTime, in TimeTransform timeTransform, in TimerData timerData,
                EnabledRefRW<ClipActivePrevious> clipActivePrevious)
            {
                if (!clipActivePrevious.ValueRO)
                {
                    return;
                }

                var deltaTicks = timerData.DeltaTime.Value;
                if (deltaTicks < 0)
                {
                    deltaTicks = -deltaTicks;
                }

                if (deltaTicks == 0)
                {
                    return;
                }

                var ticksPastStart = (localTime.Value - timeTransform.ClipIn).Value;
                var windowTicks = (long)math.ceil(deltaTicks * timeTransform.Scale) + 1;
                if (ticksPastStart >= 0 && ticksPastStart < windowTicks)
                {
                    clipActivePrevious.ValueRW = false;
                }
            }
        }
    }
}
