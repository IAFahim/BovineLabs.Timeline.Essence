using System;
using BovineLabs.Core.Collections;
using BovineLabs.Core.Extensions;
using BovineLabs.Essence.Data;
using BovineLabs.Essence.Data.Actions;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Reaction.Data.Active;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Reaction.Groups;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace BovineLabs.Essence.Actions
{
    [UpdateInGroup(typeof(ActiveEnabledSystemGroup))]
    public partial struct ActionTickDistributionSystem : ISystem
    {
        private NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount> intrinsicChanges;
        private NativeParallelMultiHashMapFallback<Entity, EventAmount> eventChanges;
        private NativeList<Entity> uniqueKeys;
        private NativeList<Entity> uniqueEventKeys;
        private BufferLookup<Stat> statsLookup;
        private ComponentLookup<TargetsCustom> targetsCustomsLookup;
        private IntrinsicWriter.Lookup intrinsicWriters;
        private ConditionEventWriter.Lookup eventWriters;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            intrinsicChanges =
                new NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>(64, Allocator.Persistent);
            eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            uniqueEventKeys = new NativeList<Entity>(64, Allocator.Persistent);

            statsLookup = state.GetBufferLookup<Stat>(true);
            targetsCustomsLookup = state.GetComponentLookup<TargetsCustom>(true);
            intrinsicWriters.Create(ref state);
            eventWriters.Create(ref state);
        }

        public void OnDestroy(ref SystemState state)
        {
            intrinsicChanges.Dispose();
            eventChanges.Dispose();
            uniqueKeys.Dispose();
            uniqueEventKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            statsLookup.Update(ref state);
            targetsCustomsLookup.Update(ref state);
            intrinsicWriters.Update(ref state, SystemAPI.GetSingleton<EssenceConfig>());
            eventWriters.Update(ref state);

            state.Dependency = new EvaluateJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                IntrinsicChanges = intrinsicChanges.AsWriter(),
                EventChanges = eventChanges.AsWriter(),
                TargetsCustoms = targetsCustomsLookup,
                Stats = statsLookup
            }.ScheduleParallel(state.Dependency);

            state.Dependency = intrinsicChanges.Apply(state.Dependency, out var intrinsicReader);
            state.Dependency = eventChanges.Apply(state.Dependency, out var eventReader);

            state.Dependency =
                new GetKeysJob { UniqueKeys = uniqueKeys, GroupChanges = intrinsicReader }.Schedule(
                    state.Dependency);
            state.Dependency =
                new GetEventKeysJob { UniqueKeys = uniqueEventKeys, GroupChanges = eventReader }.Schedule(
                    state.Dependency);

            state.Dependency = new ApplyIntrinsicJob
            {
                Keys = uniqueKeys,
                GroupChanges = intrinsicReader,
                IntrinsicWriters = intrinsicWriters
            }.Schedule(uniqueKeys, 64, state.Dependency);

            state.Dependency = new ApplyEventJob
            {
                Keys = uniqueEventKeys,
                GroupChanges = eventReader,
                EventWriters = eventWriters
            }.Schedule(uniqueEventKeys, 64, state.Dependency);

            state.Dependency = intrinsicChanges.Clear(state.Dependency);
            state.Dependency = eventChanges.Clear(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(Active))]
        private partial struct EvaluateJob : IJobEntity
        {
            public float DeltaTime;
            public NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>.ParallelWriter IntrinsicChanges;
            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;
            [ReadOnly] public ComponentLookup<TargetsCustom> TargetsCustoms;
            [ReadOnly] public BufferLookup<Stat> Stats;

            private Entity GetTarget(Target target, Entity self, in Targets targets)
            {
                return target switch
                {
                    Target.Owner => targets.Owner,
                    Target.Source => targets.Source,
                    Target.Target => targets.Target,
                    Target.Self => self,
                    Target.Custom0 => TargetsCustoms.TryGetComponent(self, out var tc0)
                        ? tc0.Target0
                        : Entity.Null,
                    Target.Custom1 => TargetsCustoms.TryGetComponent(self, out var tc1)
                        ? tc1.Target1
                        : Entity.Null,
                    _ => Entity.Null
                };
            }

            private void Execute(
                Entity entity,
                in ActionTickDistribution cfg,
                ref ActionTickDistributionState state,
                in Targets targets,
                EnabledRefRO<ActivePrevious> activePrevious)
            {
                var isNew = !activePrevious.ValueRO;

                if (isNew)
                {
                    state.ElapsedTime = 0f;
                    state.AppliedTicks = 0;
                    state.EndFired = false;
                }

                var fromTarget = GetTarget(cfg.From, entity, targets);
                if (fromTarget == Entity.Null || !Stats.TryGetBuffer(fromTarget, out var statsBuffer)) return;

                var stats = statsBuffer.AsMap();
                var duration = stats.GetValueFloat(cfg.TicDuration);
                var tps = stats.GetValueFloat(cfg.TicPerSecond);

                if (duration <= 0f) return;

                state.ElapsedTime += DeltaTime;
                var t = math.saturate(state.ElapsedTime / duration);

                var cdf = cfg.Curve.Value.EvaluateIgnoreWrapMode(t);
                var totalTicks = duration * tps;

                var expected = (int)math.floor(cdf * totalTicks);
                expected = math.clamp(expected, 0, (int)math.floor(totalTicks));
                var delta = expected - state.AppliedTicks;

                state.AppliedTicks = expected;

                var toTarget = GetTarget(cfg.To, entity, targets);

                if (delta > 0 && toTarget != Entity.Null)
                {
                    if (cfg.TickStore != 0) IntrinsicChanges.Add(toTarget, new IntrinsicAmount(cfg.TickStore, delta));

                    if (cfg.OnTic != ConditionKey.Null) EventChanges.Add(toTarget, new EventAmount(cfg.OnTic, delta));
                }

                if (t >= 1f && !state.EndFired && cfg.OnEnd != ConditionKey.Null && toTarget != Entity.Null)
                {
                    state.EndFired = true;
                    EventChanges.Add(toTarget, new EventAmount(cfg.OnEnd, 1));
                }
            }
        }

        [BurstCompile]
        private struct GetKeysJob : IJob
        {
            public NativeList<Entity> UniqueKeys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, IntrinsicAmount>.ReadOnly GroupChanges;

            public void Execute()
            {
                GroupChanges.GetUniqueKeyArray(UniqueKeys);
            }
        }

        [BurstCompile]
        private struct GetEventKeysJob : IJob
        {
            public NativeList<Entity> UniqueKeys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, EventAmount>.ReadOnly GroupChanges;

            public void Execute()
            {
                GroupChanges.GetUniqueKeyArray(UniqueKeys);
            }
        }

        [BurstCompile]
        private struct ApplyIntrinsicJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeList<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, IntrinsicAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public IntrinsicWriter.Lookup IntrinsicWriters;

            public void Execute(int index)
            {
                var key = Keys[index];
                if (Hint.Unlikely(!IntrinsicWriters.TryGet(key, out var intrinsicWriter))) return;

                var values = new NativeList<IntrinsicAmount>(Allocator.Temp);

                GroupChanges.TryGetFirstValue(key, out var value, out var it);
                values.Add(value);

                while (GroupChanges.TryGetNextValue(out value, ref it))
                {
                    var existingIndex = values.IndexOf(value);
                    if (Hint.Unlikely(existingIndex == -1)) values.Add(value);
                    else values.ElementAt(existingIndex).Amount += value.Amount;
                }

                foreach (var i in values) intrinsicWriter.Add(i.Intrinsic, i.Amount);
                values.Dispose();
            }
        }

        [BurstCompile]
        private struct ApplyEventJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeList<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, EventAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public ConditionEventWriter.Lookup EventWriters;

            public void Execute(int index)
            {
                var key = Keys[index];
                if (Hint.Unlikely(!EventWriters.TryGet(key, out var eventWriter))) return;

                var values = new NativeList<EventAmount>(Allocator.Temp);

                GroupChanges.TryGetFirstValue(key, out var value, out var it);
                values.Add(value);

                while (GroupChanges.TryGetNextValue(out value, ref it))
                {
                    var existingIndex = values.IndexOf(value);
                    if (Hint.Unlikely(existingIndex == -1)) values.Add(value);
                    else values.ElementAt(existingIndex).Amount += value.Amount;
                }

                foreach (var e in values) eventWriter.Trigger(e.Event, e.Amount);
                values.Dispose();
            }
        }

        private struct IntrinsicAmount : IEquatable<IntrinsicAmount>
        {
            public readonly IntrinsicKey Intrinsic;
            public int Amount;

            public IntrinsicAmount(IntrinsicKey intrinsic, int amount)
            {
                Intrinsic = intrinsic;
                Amount = amount;
            }

            public bool Equals(IntrinsicAmount other)
            {
                return Intrinsic.Equals(other.Intrinsic);
            }

            public override int GetHashCode()
            {
                return Intrinsic.GetHashCode();
            }
        }

        private struct EventAmount : IEquatable<EventAmount>
        {
            public readonly ConditionKey Event;
            public int Amount;

            public EventAmount(ConditionKey evt, int amount)
            {
                Event = evt;
                Amount = amount;
            }

            public bool Equals(EventAmount other)
            {
                return Event.Equals(other.Event);
            }

            public override int GetHashCode()
            {
                return Event.GetHashCode();
            }
        }
    }
}