using BovineLabs.Essence;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Essence.Data.TickDistribution;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Essence.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    public partial struct TimelineTickDistributionSystem : ISystem
    {
        private IntrinsicWriter.Lookup _intrinsicWriterLookup;
        private BufferLookup<Stat> _statsLookup;
        private ComponentLookup<Targets> _targetsLookup;
        private ComponentLookup<TargetsCustom> _targetsCustoms;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _intrinsicWriterLookup.Create(ref state);
            _statsLookup = state.GetBufferLookup<Stat>(true);
            _targetsLookup = state.GetComponentLookup<Targets>(true);
            _targetsCustoms = state.GetComponentLookup<TargetsCustom>(true);

            state.RequireForUpdate<EssenceConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _intrinsicWriterLookup.Update(ref state, SystemAPI.GetSingleton<EssenceConfig>());
            _statsLookup.Update(ref state);
            _targetsLookup.Update(ref state);
            _targetsCustoms.Update(ref state);

            state.Dependency = new DistributeJob
            {
                StatsLookup = _statsLookup,
                TargetsLookup = _targetsLookup,
                TargetsCustoms = _targetsCustoms,
                IntrinsicWriters = _intrinsicWriterLookup
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
                if (boundEntity == Entity.Null || !TargetsLookup.TryGetComponent(boundEntity, out var targets)) return;

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
                var t = duration.Value > 0
                    ? math.saturate((float)(localTime.Value.Value / (double)duration.Value))
                    : 1f;

                var cdf = data.Cdf.Value.Evaluate(t);
                var totalTicks = stats.AsMap().GetValueFloor(data.TotalTicksStat);

                var expected = (int)math.floor(cdf * totalTicks);
                expected = math.clamp(expected, 0, totalTicks);

                state.TicksThisFrame = expected - state.AppliedTicks;
                state.AppliedTicks = expected;

                if (state.TicksThisFrame <= 0) return;

                if (data.Intrinsic != 0)
                {
                    var intrinsicTarget = targets.Get(data.IntrinsicTarget, boundEntity, TargetsCustoms);
                    if (intrinsicTarget != Entity.Null &&
                        IntrinsicWriters.TryGet(intrinsicTarget, out var intrinsicWriter))
                        intrinsicWriter.Add(data.Intrinsic, state.TicksThisFrame);
                }

                if (data.Event != ConditionKey.Null)
                    if (IntrinsicWriters.EventWriter.TryGet(boundEntity, out var eventWriter))
                        eventWriter.Trigger(data.Event, state.TicksThisFrame);
            }
        }
    }
}