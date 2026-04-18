namespace BovineLabs.Timeline.Essence.Systems
{
    using BovineLabs.Core.Collections;
    using BovineLabs.Essence;
    using BovineLabs.Essence.Data;
    using BovineLabs.Reaction.Actives;
    using BovineLabs.Reaction.Conditions;
    using BovineLabs.Reaction.Data.Active;
    using BovineLabs.Reaction.Data.Conditions;
    using BovineLabs.Reaction.Data.Core;
    using BovineLabs.Reaction.Groups;
    using BovineLabs.Timeline.Essence.Data.TickDistribution;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Mathematics;

    [BurstCompile]
    [UpdateInGroup(typeof(TimerSystemGroup))]
    [UpdateAfter(typeof(ActiveDurationSystem))]
    public partial struct TickDistributionSystem : ISystem
    {
        private IntrinsicWriter.Lookup intrinsicWriterLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.intrinsicWriterLookup.Create(ref state);
            state.RequireForUpdate<EssenceConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            this.intrinsicWriterLookup.Update(ref state, SystemAPI.GetSingleton<EssenceConfig>());

            new DistributeJob
            {
                StatsLookup = SystemAPI.GetBufferLookup<Stat>(true),
                TargetsCustoms = SystemAPI.GetComponentLookup<TargetsCustom>(true),
                IntrinsicWriters = this.intrinsicWriterLookup,
            }.ScheduleParallel();
        }

        [BurstCompile]
        private partial struct DistributeJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<Stat> StatsLookup;
            [ReadOnly] public ComponentLookup<TargetsCustom> TargetsCustoms;
            
            [NativeDisableParallelForRestriction] public IntrinsicWriter.Lookup IntrinsicWriters;

            private void Execute(
                Entity entity,
                ref TickDistributionState state, 
                in TickDistributionCurve curve, 
                in ActiveDuration duration, 
                in ActiveDurationRemaining remaining, 
                in Targets targets,
                EnabledRefRO<Active> active,
                EnabledRefRO<ActivePrevious> previous,
                EnabledRefRO<ActiveOnDuration> onDuration)
            {
                if (active.ValueRO && !previous.ValueRO)
                {
                    state.AppliedTicks = 0;
                    state.TicksThisFrame = 0;
                }

                if (!onDuration.ValueRO)
                {
                    state.TicksThisFrame = 0;
                    return;
                }

                var statTarget = targets.Get(curve.StatTarget, entity, this.TargetsCustoms);
                if (statTarget == Entity.Null || !this.StatsLookup.TryGetBuffer(statTarget, out var stats))
                {
                    state.TicksThisFrame = 0;
                    return;
                }

                var t = math.saturate(1f - (remaining.Value / math.max(0.0001f, duration.Value)));
                var cdf = curve.Value.Value.EvaluateIgnoreWrapMode(t);

                var totalTicks = stats.AsMap().GetValueFloor(curve.TotalTicksStat);

                var expected = (int)math.floor(cdf * totalTicks);
                expected = math.clamp(expected, 0, totalTicks);

                state.TicksThisFrame = expected - state.AppliedTicks;
                state.AppliedTicks = expected;

                if (state.TicksThisFrame > 0)
                {
                    if (curve.Intrinsic != 0)
                    {
                        var intrinsicTarget = targets.Get(curve.IntrinsicTarget, entity, this.TargetsCustoms);
                        if (intrinsicTarget != Entity.Null && this.IntrinsicWriters.TryGet(intrinsicTarget, out var intrinsicWriter))
                        {
                            intrinsicWriter.Add(curve.Intrinsic, state.TicksThisFrame);
                        }
                    }

                    if (curve.Event != ConditionKey.Null)
                    {
                        if (this.IntrinsicWriters.EventWriter.TryGet(entity, out var eventWriter))
                        {
                            eventWriter.Trigger(curve.Event, state.TicksThisFrame);
                        }
                    }
                }
            }
        }
    }
}