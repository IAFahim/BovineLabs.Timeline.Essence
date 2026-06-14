using System;
using BovineLabs.Core.Collections;
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
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ActionTickDistributionSystem : ISystem
    {
        private NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount> intrinsicChanges;
        private NativeParallelMultiHashMapFallback<Entity, EventAmount> eventChanges;
        private NativeParallelHashSet<Entity> intrinsicTargets;
        private NativeParallelHashSet<Entity> eventTargets;
        private NativeList<Entity> uniqueKeys;
        private NativeList<Entity> uniqueEventKeys;
        private BufferLookup<Stat> statsLookup;
        private IntrinsicWriter.Lookup intrinsicWriters;
        private ConditionEventWriter.Lookup eventWriters;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            intrinsicChanges =
                new NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>(64, Allocator.Persistent);
            eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            intrinsicTargets = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            eventTargets = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            uniqueEventKeys = new NativeList<Entity>(64, Allocator.Persistent);

            statsLookup = state.GetBufferLookup<Stat>(true);
            intrinsicWriters.Create(ref state);
            eventWriters.Create(ref state);
        }

        public void OnDestroy(ref SystemState state)
        {
            intrinsicChanges.Dispose();
            eventChanges.Dispose();
            intrinsicTargets.Dispose();
            eventTargets.Dispose();
            uniqueKeys.Dispose();
            uniqueEventKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            statsLookup.Update(ref state);
            intrinsicWriters.Update(ref state, SystemAPI.GetSingleton<EssenceConfig>());
            eventWriters.Update(ref state);
            intrinsicTargets.Clear();
            eventTargets.Clear();

            state.Dependency = new EvaluateJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                IntrinsicChanges = intrinsicChanges.AsWriter(),
                EventChanges = eventChanges.AsWriter(),
                IntrinsicTargets = intrinsicTargets.AsParallelWriter(),
                EventTargets = eventTargets.AsParallelWriter(),
                Stats = statsLookup
            }.ScheduleParallel(state.Dependency);

            state.Dependency = intrinsicChanges.Apply(state.Dependency, out var intrinsicReader);
            state.Dependency = eventChanges.Apply(state.Dependency, out var eventReader);

            state.Dependency =
                new GetKeysJob { UniqueKeys = uniqueKeys, UniqueKeySet = intrinsicTargets }.Schedule(
                    state.Dependency);
            state.Dependency =
                new GetEventKeysJob { UniqueKeys = uniqueEventKeys, UniqueKeySet = eventTargets }.Schedule(
                    state.Dependency);

            state.Dependency = new ApplyIntrinsicJob
            {
                Keys = uniqueKeys.AsDeferredJobArray(),
                GroupChanges = intrinsicReader,
                IntrinsicWriters = intrinsicWriters
            }.Schedule(uniqueKeys, 64, state.Dependency);

            state.Dependency = new ApplyEventJob
            {
                Keys = uniqueEventKeys.AsDeferredJobArray(),
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
            public NativeParallelHashSet<Entity>.ParallelWriter IntrinsicTargets;
            public NativeParallelHashSet<Entity>.ParallelWriter EventTargets;
            [ReadOnly] public BufferLookup<Stat> Stats;

            private Entity GetTarget(Target target, Entity self, in Targets targets)
            {
                return target switch
                {
                    Target.Owner => targets.Owner,
                    Target.Source => targets.Source,
                    Target.Target => targets.Target,
                    Target.Self => self,
                    Target.Custom => targets.Custom,
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
                    if (cfg.TickStore != 0)
                    {
                        IntrinsicChanges.Add(toTarget, new IntrinsicAmount(cfg.TickStore, delta));
                        IntrinsicTargets.Add(toTarget);
                    }

                    if (cfg.OnTic != ConditionKey.Null)
                    {
                        EventChanges.Add(toTarget, new EventAmount(cfg.OnTic, delta));
                        EventTargets.Add(toTarget);
                    }
                }

                if (t >= 1f && !state.EndFired && cfg.OnEnd != ConditionKey.Null && toTarget != Entity.Null)
                {
                    state.EndFired = true;
                    EventChanges.Add(toTarget, new EventAmount(cfg.OnEnd, 1));
                    EventTargets.Add(toTarget);
                }
            }
        }

        [BurstCompile]
        private struct GetKeysJob : IJob
        {
            public NativeList<Entity> UniqueKeys;
            [ReadOnly] public NativeParallelHashSet<Entity> UniqueKeySet;

            public void Execute()
            {
                UniqueKeys.Clear();
                foreach (var key in UniqueKeySet)
                    UniqueKeys.Add(key);
            }
        }

        [BurstCompile]
        private struct GetEventKeysJob : IJob
        {
            public NativeList<Entity> UniqueKeys;
            [ReadOnly] public NativeParallelHashSet<Entity> UniqueKeySet;

            public void Execute()
            {
                UniqueKeys.Clear();
                foreach (var key in UniqueKeySet)
                    UniqueKeys.Add(key);
            }
        }

        [BurstCompile]
        private struct ApplyIntrinsicJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, IntrinsicAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public IntrinsicWriter.Lookup IntrinsicWriters;

            public void Execute(int index)
            {
                var key = Keys[index];
                if (Hint.Unlikely(!IntrinsicWriters.TryGet(key, out var intrinsicWriter))) return;

                var values = new FixedList4096Bytes<IntrinsicAmount>();

                if (GroupChanges.TryGetFirstValue(key, out var value, out var it))
                {
                    AddOrAccumulate(ref values, value, ref intrinsicWriter);

                    while (GroupChanges.TryGetNextValue(out value, ref it))
                        AddOrAccumulate(ref values, value, ref intrinsicWriter);
                }

                foreach (var i in values) intrinsicWriter.Add(i.Intrinsic, i.Amount);
            }

            private static void AddOrAccumulate(ref FixedList4096Bytes<IntrinsicAmount> values, IntrinsicAmount value,
                ref IntrinsicWriter intrinsicWriter)
            {
                for (var i = 0; i < values.Length; i++)
                    if (values[i].Intrinsic.Equals(value.Intrinsic))
                    {
                        var existing = values[i];
                        existing.Amount += value.Amount;
                        values[i] = existing;
                        return;
                    }

                if (values.Length < values.Capacity)
                {
                    values.Add(value);
                    return;
                }

                intrinsicWriter.Add(value.Intrinsic, value.Amount);
            }
        }

        [BurstCompile]
        private struct ApplyEventJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, EventAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public ConditionEventWriter.Lookup EventWriters;

            public void Execute(int index)
            {
                var key = Keys[index];
                if (Hint.Unlikely(!EventWriters.TryGet(key, out var eventWriter))) return;

                var values = new FixedList4096Bytes<EventAmount>();

                if (GroupChanges.TryGetFirstValue(key, out var value, out var it))
                {
                    AddOrAccumulate(ref values, value, ref eventWriter);

                    while (GroupChanges.TryGetNextValue(out value, ref it))
                        AddOrAccumulate(ref values, value, ref eventWriter);
                }

                foreach (var e in values) eventWriter.Trigger(e.Event, e.Amount);
            }

            private static void AddOrAccumulate(ref FixedList4096Bytes<EventAmount> values, EventAmount value,
                ref ConditionEventWriter eventWriter)
            {
                for (var i = 0; i < values.Length; i++)
                    if (values[i].Event.Equals(value.Event))
                    {
                        var existing = values[i];
                        existing.Amount += value.Amount;
                        values[i] = existing;
                        return;
                    }

                if (values.Length < values.Capacity)
                {
                    values.Add(value);
                    return;
                }

                eventWriter.Trigger(value.Event, value.Amount);
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