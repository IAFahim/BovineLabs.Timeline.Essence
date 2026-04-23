namespace BovineLabs.Essence.Actions
{
    using BovineLabs.Essence.Data;
    using BovineLabs.Essence.Data.Actions;
    using BovineLabs.Reaction.Data.Active;
    using BovineLabs.Reaction.Data.Conditions;
    using BovineLabs.Reaction.Data.Core;
    using BovineLabs.Reaction.Groups;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Mathematics;

    [BurstCompile]
    [UpdateInGroup(typeof(ActiveSystemGroup))][UpdateAfter(typeof(BovineLabs.Reaction.Actives.ActiveDurationSystem))]
    public partial struct ActionTickDistributionSystem : ISystem
    {
        private IntrinsicWriter.Lookup _intrinsicWriterLookup;
        private BufferLookup<Stat> _statsLookup;
        private ComponentLookup<TargetsCustom> _targetsCustoms;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _intrinsicWriterLookup.Create(ref state);
            _statsLookup = state.GetBufferLookup<Stat>(true);
            _targetsCustoms = state.GetComponentLookup<TargetsCustom>(true);

            state.RequireForUpdate<EssenceConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _intrinsicWriterLookup.Update(ref state, SystemAPI.GetSingleton<EssenceConfig>());
            _statsLookup.Update(ref state);
            _targetsCustoms.Update(ref state);

            // Important: We use Schedule() instead of ScheduleParallel() because IntrinsicWriters and EventWriters
            // modify DynamicBuffers on target entities. Doing this single-threaded prevents race conditions on shared targets.
            state.Dependency = new DistributeJob
            {
                StatsLookup = _statsLookup,
                TargetsCustoms = _targetsCustoms,
                IntrinsicWriters = _intrinsicWriterLookup
            }.Schedule(state.Dependency);
        }

        [BurstCompile][WithAll(typeof(Active))]
        private partial struct DistributeJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<Stat> StatsLookup;
            [ReadOnly] public ComponentLookup<TargetsCustom> TargetsCustoms;

            public IntrinsicWriter.Lookup IntrinsicWriters;

            private void Execute(
                Entity entity,
                in ActiveDuration duration,
                in ActiveDurationRemaining remaining,
                in Targets targets,
                in DynamicBuffer<ActionTickDistribution> distributions,
                ref DynamicBuffer<ActionTickDistributionState> states,
                EnabledRefRO<ActivePrevious> previous)
            {
                var t = duration.Value > 0
                    ? math.saturate(1f - (remaining.Value / duration.Value))
                    : 1f;

                for (var i = 0; i < distributions.Length; i++)
                {
                    var data = distributions[i];
                    ref var state = ref states.ElementAt(i);

                    if (!previous.ValueRO)
                    {
                        state.AppliedTicks = 0;
                    }

                    var statTarget = targets.Get(data.StatTarget, entity, TargetsCustoms);
                    if (statTarget == Entity.Null || !StatsLookup.TryGetBuffer(statTarget, out var stats))
                    {
                        continue;
                    }

                    var cdf = data.Cdf.Value.Evaluate(t);
                    var totalTicks = stats.AsMap().GetValueFloor(data.TotalTicksStat);

                    var expected = (int)math.floor(cdf * totalTicks);
                    expected = math.clamp(expected, 0, totalTicks);

                    var ticksThisFrame = expected - state.AppliedTicks;
                    state.AppliedTicks = expected;

                    if (ticksThisFrame <= 0) continue;

                    if (data.Intrinsic != 0)
                    {
                        var intrinsicTarget = targets.Get(data.IntrinsicTarget, entity, TargetsCustoms);
                        if (intrinsicTarget != Entity.Null &&
                            IntrinsicWriters.TryGet(intrinsicTarget, out var intrinsicWriter))
                        {
                            intrinsicWriter.Add(data.Intrinsic, ticksThisFrame);
                        }
                    }

                    if (data.EventKey != ConditionKey.Null)
                    {
                        if (IntrinsicWriters.EventWriter.TryGet(entity, out var eventWriter))
                        {
                            eventWriter.Trigger(data.EventKey, ticksThisFrame);
                        }
                    }
                }
            }
        }
    }
}