namespace BovineLabs.Essence.Actions
{
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
            this.intrinsicChanges =
                new NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>(64, Allocator.Persistent);
            this.eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            this.uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            this.uniqueEventKeys = new NativeList<Entity>(64, Allocator.Persistent);

            this.statsLookup = state.GetBufferLookup<Stat>(true);
            this.targetsCustomsLookup = state.GetComponentLookup<TargetsCustom>(true);
            this.intrinsicWriters.Create(ref state);
            this.eventWriters.Create(ref state);
        }

        public void OnDestroy(ref SystemState state)
        {
            this.intrinsicChanges.Dispose();
            this.eventChanges.Dispose();
            this.uniqueKeys.Dispose();
            this.uniqueEventKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            this.statsLookup.Update(ref state);
            this.targetsCustomsLookup.Update(ref state);
            this.intrinsicWriters.Update(ref state, SystemAPI.GetSingleton<EssenceConfig>());
            this.eventWriters.Update(ref state);

            state.Dependency = new EvaluateJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                IntrinsicChanges = this.intrinsicChanges.AsWriter(),
                EventChanges = this.eventChanges.AsWriter(),
                TargetsCustoms = this.targetsCustomsLookup,
                Stats = this.statsLookup
            }.ScheduleParallel(state.Dependency);

            state.Dependency = this.intrinsicChanges.Apply(state.Dependency, out var intrinsicReader);
            state.Dependency = this.eventChanges.Apply(state.Dependency, out var eventReader);

            state.Dependency =
                new GetKeysJob { UniqueKeys = this.uniqueKeys, GroupChanges = intrinsicReader }.Schedule(
                    state.Dependency);
            state.Dependency =
                new GetEventKeysJob { UniqueKeys = this.uniqueEventKeys, GroupChanges = eventReader }.Schedule(
                    state.Dependency);

            state.Dependency = new ApplyIntrinsicJob
            {
                Keys = this.uniqueKeys,
                GroupChanges = intrinsicReader,
                IntrinsicWriters = this.intrinsicWriters,
            }.Schedule(this.uniqueKeys, 64, state.Dependency);

            state.Dependency = new ApplyEventJob
            {
                Keys = this.uniqueEventKeys,
                GroupChanges = eventReader,
                EventWriters = this.eventWriters,
            }.Schedule(this.uniqueEventKeys, 64, state.Dependency);

            state.Dependency = this.intrinsicChanges.Clear(state.Dependency);
            state.Dependency = this.eventChanges.Clear(state.Dependency);
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
                    Target.Custom0 => this.TargetsCustoms.TryGetComponent(self, out var tc0)
                        ? tc0.Target0
                        : Entity.Null,
                    Target.Custom1 => this.TargetsCustoms.TryGetComponent(self, out var tc1)
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
                if (fromTarget == Entity.Null || !this.Stats.TryGetBuffer(fromTarget, out var statsBuffer)) return;

                var stats = statsBuffer.AsMap();
                var duration = stats.GetValueFloat(cfg.TicDuration);
                var tps = stats.GetValueFloat(cfg.TicPerSecond);

                if (duration <= 0f) return;

                state.ElapsedTime += this.DeltaTime;
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
                        this.IntrinsicChanges.Add(toTarget, new IntrinsicAmount(cfg.TickStore, delta));
                    }

                    if (cfg.OnTic != ConditionKey.Null)
                    {
                        this.EventChanges.Add(toTarget, new EventAmount(cfg.OnTic, delta));
                    }
                }

                if (t >= 1f && !state.EndFired && cfg.OnEnd != ConditionKey.Null && toTarget != Entity.Null)
                {
                    state.EndFired = true;
                    this.EventChanges.Add(toTarget, new EventAmount(cfg.OnEnd, 1));
                }
            }
        }

        [BurstCompile]
        private struct GetKeysJob : IJob
        {
            public NativeList<Entity> UniqueKeys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, IntrinsicAmount>.ReadOnly GroupChanges;
            public void Execute() => this.GroupChanges.GetUniqueKeyArray(this.UniqueKeys);
        }

        [BurstCompile]
        private struct GetEventKeysJob : IJob
        {
            public NativeList<Entity> UniqueKeys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, EventAmount>.ReadOnly GroupChanges;
            public void Execute() => this.GroupChanges.GetUniqueKeyArray(this.UniqueKeys);
        }

        [BurstCompile]
        private struct ApplyIntrinsicJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeList<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, IntrinsicAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public IntrinsicWriter.Lookup IntrinsicWriters;

            public void Execute(int index)
            {
                var key = this.Keys[index];
                if (Hint.Unlikely(!this.IntrinsicWriters.TryGet(key, out var intrinsicWriter))) return;

                var values = new NativeList<IntrinsicAmount>(Allocator.Temp);

                this.GroupChanges.TryGetFirstValue(key, out var value, out var it);
                values.Add(value);

                while (this.GroupChanges.TryGetNextValue(out value, ref it))
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
                var key = this.Keys[index];
                if (Hint.Unlikely(!this.EventWriters.TryGet(key, out var eventWriter))) return;

                var values = new NativeList<EventAmount>(Allocator.Temp);

                this.GroupChanges.TryGetFirstValue(key, out var value, out var it);
                values.Add(value);

                while (this.GroupChanges.TryGetNextValue(out value, ref it))
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
                this.Intrinsic = intrinsic;
                this.Amount = amount;
            }

            public bool Equals(IntrinsicAmount other) => this.Intrinsic.Equals(other.Intrinsic);
            public override int GetHashCode() => this.Intrinsic.GetHashCode();
        }

        private struct EventAmount : IEquatable<EventAmount>
        {
            public readonly ConditionKey Event;
            public int Amount;

            public EventAmount(ConditionKey evt, int amount)
            {
                this.Event = evt;
                this.Amount = amount;
            }

            public bool Equals(EventAmount other) => this.Event.Equals(other.Event);
            public override int GetHashCode() => this.Event.GetHashCode();
        }
    }
}