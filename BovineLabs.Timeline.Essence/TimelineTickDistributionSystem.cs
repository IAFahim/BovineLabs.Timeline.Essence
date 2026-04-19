namespace BovineLabs.Timeline.Essence.Systems
{
    using BovineLabs.Core.Collections;
    using BovineLabs.Essence;
    using BovineLabs.Essence.Data;
    using BovineLabs.Reaction.Data.Core;
    using BovineLabs.Timeline.Data;
    using BovineLabs.Timeline.Essence.Data.TickDistribution;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Mathematics;

    [BurstCompile]
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    public partial struct TimelineTickDistributionSystem : ISystem
    {
        private IntrinsicWriter.Lookup intrinsicWriterLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            intrinsicWriterLookup.Create(ref state);
            state.RequireForUpdate<EssenceConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            intrinsicWriterLookup.Update(ref state, SystemAPI.GetSingleton<EssenceConfig>());

            state.Dependency = new DistributeJob
            {
                StatsLookup = SystemAPI.GetBufferLookup<Stat>(true),
                TargetsLookup = SystemAPI.GetComponentLookup<Targets>(true),
                TargetsCustoms = SystemAPI.GetComponentLookup<TargetsCustom>(true),
                IntrinsicWriters = intrinsicWriterLookup,
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct DistributeJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<Stat> StatsLookup;
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public ComponentLookup<TargetsCustom> TargetsCustoms;
            
            [NativeDisableParallelForRestriction] public IntrinsicWriter.Lookup IntrinsicWriters;

            private void Execute(
                ref TickDistributionState state,
                in TickDistributionClipData data,
                in TrackBinding binding,
                in LocalTime localTime,
                in TimeTransform timeTransform,
                EnabledRefRO<ClipActivePrevious> previous)
            {
                var boundEntity = binding.Value;
                if (boundEntity == Entity.Null || !TargetsLookup.TryGetComponent(boundEntity, out var targets))
                {
                    return;
                }

                if (!previous.ValueRO)
                {
                    state.AppliedTicks = 0;
                    state.TicksThisFrame = 0;
                }

                var statTarget = targets.Get(data.StatTarget, boundEntity, TargetsCustoms);
                if (statTarget == Entity.Null || !StatsLookup.TryGetBuffer(statTarget, out var stats))
                {
                    state.TicksThisFrame = 0;
                    return;
                }

                var duration = (timeTransform.End - timeTransform.Start) * timeTransform.Scale;
                var t = duration.Value > 0 ? math.saturate((float)(localTime.Value.Value / (double)duration.Value)) : 1f;

                var cdf = data.Curve.Value.EvaluateIgnoreWrapMode(t);
                var totalTicks = stats.AsMap().GetValueFloor(data.TotalTicksStat);

                var expected = (int)math.floor(cdf * totalTicks);
                expected = math.clamp(expected, 0, totalTicks);

                state.TicksThisFrame = expected - state.AppliedTicks;
                state.AppliedTicks = expected;

                if (state.TicksThisFrame <= 0) return;
                var intrinsicTarget = targets.Get(data.IntrinsicTarget, boundEntity, TargetsCustoms);
                if (intrinsicTarget != Entity.Null && IntrinsicWriters.TryGet(intrinsicTarget, out var intrinsicWriter))
                {
                    intrinsicWriter.Add(data.Intrinsic, state.TicksThisFrame);
                }

                if (data.Event == BovineLabs.Reaction.Data.Conditions.ConditionKey.Null) return;
                if (IntrinsicWriters.EventWriter.TryGet(boundEntity, out var eventWriter))
                {
                    eventWriter.Trigger(data.Event, state.TicksThisFrame);
                }
            }
        }
    }
}