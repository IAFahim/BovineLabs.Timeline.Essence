using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Data.Schedular;
using BovineLabs.Timeline.Essence.Data;
using Unity.Burst;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateBefore(typeof(TimelineEssenceIntrinsicSystem))]
    [UpdateBefore(typeof(TimelineEssenceEventSystem))]
    [UpdateBefore(typeof(TimelineEssenceTickSystem))] // reset must land before TickSystem consumes justActivated this frame
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct TimelineEssenceLoopRefireSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new RearmJob().ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        [WithAny(typeof(TimelineEssenceIntrinsicData), typeof(TimelineEssenceEventData), typeof(TimelineEssenceTickData))]
        private partial struct RearmJob : IJobEntity
        {
            private void Execute(in LocalTime localTime, in TimeTransform timeTransform, in TimerData timerData,
                EnabledRefRW<ClipActivePrevious> clipActivePrevious)
            {
                if (!clipActivePrevious.ValueRO) return;

                var deltaTicks = timerData.DeltaTime.Value;
                var ticksPastStart = (localTime.Value - timeTransform.ClipIn).Value;

                if (LoopRefireMath.ShouldRearm(deltaTicks, ticksPastStart, timeTransform.Scale))
                    clipActivePrevious.ValueRW = false;
            }
        }
    }
}