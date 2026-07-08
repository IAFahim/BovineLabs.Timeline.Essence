using System;
using BovineLabs.Core;
using BovineLabs.Core.Collections;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Essence.Data;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace BovineLabs.Timeline.Essence
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(EntityLinkTargetPatchSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct TimelineEssenceEventSystem : ISystem
    {
        private NativeParallelMultiHashMapFallback<Entity, EventAmount> _eventChanges;
        private NativeParallelHashSet<Entity> _uniqueKeySet;
        private NativeList<Entity> _uniqueKeys;

        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _linkSourceLookup;
        private UnsafeBufferLookup<EntityLinkEntry> _linkLookup;
        private ConditionEventWriter.Lookup _writers;
        private ConditionEventWriter.SingletonData _writersSingletonData;

        // Writer-existence pre-check: a ConditionEventWriter needs both the ConditionEvent buffer and the
        // EventsDirty enableable. Checking them up-front folds the ApplyJob "no writer" silent-drop into the retry.
        private BufferLookup<ConditionEvent> _conditionEvents;
        private ComponentLookup<EventsDirty> _eventsDirty;

        private EntityQuery _activeClipQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // Gate scheduling on clip presence and on the Reaction bootstrap singletons the ConditionEventWriter
            // needs — the writer's generated code fetches these, so requiring them avoids a throw in a bare world.
            state.RequireForUpdate<TimelineEssenceEventData>();
            state.RequireForUpdate<ConditionConfig>();
            state.RequireForUpdate<ConditionEventPayloadAllocator>();

            _eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            _uniqueKeySet = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            _uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            _activeClipQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TrackBinding, TimelineEssenceEventData, ClipActive>()
                .WithPresent<TimelineEssenceDeliveryPending>()
                .Build(ref state);
            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _linkSourceLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _linkLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _conditionEvents = state.GetBufferLookup<ConditionEvent>(true);
            _eventsDirty = state.GetComponentLookup<EventsDirty>(true);
            _writersSingletonData.Create(ref state);
            _writers.Create(ref state);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _eventChanges.Dispose();
            _uniqueKeySet.Dispose();
            _uniqueKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _targetsLookup.Update(ref state);
            _linkSourceLookup.Update(ref state);
            _linkLookup.Update(ref state);
            _conditionEvents.Update(ref state);
            _eventsDirty.Update(ref state);
            _writers.Update(ref state, _writersSingletonData);
            _uniqueKeySet.Clear();

            var activeClipCount = _activeClipQuery.CalculateEntityCount();
            if (_uniqueKeySet.Capacity < activeClipCount) _uniqueKeySet.Capacity = activeClipCount;

            state.Dependency = new GatherJob
            {
                EventChanges = _eventChanges.AsWriter(),
                UniqueKeys = _uniqueKeySet.AsParallelWriter(),
                TargetsLookup = _targetsLookup,
                LinkSources = _linkSourceLookup,
                Links = _linkLookup,
                ConditionEvents = _conditionEvents,
                EventsDirty = _eventsDirty
            }.ScheduleParallel(state.Dependency);

            // A clip that ends still owing a delivery never resolved its target/binding/writer — surface it once
            // (instead of a silent drop) and clear the latch so the next activation re-arms cleanly.
            var hasLogger = SystemAPI.TryGetSingleton<BLLogger>(out var logger);
            state.Dependency = new DiagnoseMissedJob
            {
                LogEnabled = hasLogger,
                Logger = logger
            }.ScheduleParallel(state.Dependency);

            state.Dependency = _eventChanges.Apply(state.Dependency, out var reader);

            state.Dependency = new GetKeysJob
            {
                UniqueKeys = _uniqueKeys,
                UniqueKeySet = _uniqueKeySet
            }.Schedule(state.Dependency);

            state.Dependency = new ApplyJob
            {
                Keys = _uniqueKeys.AsDeferredJobArray(),
                GroupChanges = reader,
                Writers = _writers,
                LogEnabled = hasLogger,
                Logger = logger
            }.Schedule(_uniqueKeys, 64, state.Dependency);

            state.Dependency = _eventChanges.Clear(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        // EnabledRef params would otherwise implicitly filter to ENABLED — but the edge frame has
        // ClipActivePrevious disabled and the latch starts disabled, so match them by presence and read the state.
        [WithPresent(typeof(ClipActivePrevious))]
        [WithPresent(typeof(TimelineEssenceDeliveryPending))]
        private partial struct GatherJob : IJobEntity
        {
            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;
            public NativeParallelHashSet<Entity>.ParallelWriter UniqueKeys;

            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;
            [ReadOnly] public BufferLookup<ConditionEvent> ConditionEvents;
            [ReadOnly] public ComponentLookup<EventsDirty> EventsDirty;

            private void Execute(
                in TrackBinding binding,
                in TimelineEssenceEventData data,
                EnabledRefRO<ClipActivePrevious> clipActivePrevious,
                EnabledRefRW<TimelineEssenceDeliveryPending> pending)
            {
                var isEdge = !clipActivePrevious.ValueRO;
                // Value==0 is a no-op the ConditionEventWriter rejects (Check.Assume), so treat it as a dead config (Drop), not a transient miss.
                var hasPayload = !data.Event.Equals(ConditionKey.Null) && data.Value != 0;

                // Resolve the target only when something is (or just became) owed — and treat a missing
                // ConditionEventWriter (no ConditionEvent buffer / EventsDirty) as not-yet-resolved so it retries
                // instead of being dropped in ApplyJob.
                var resolved = false;
                var target = Entity.Null;
                if ((isEdge || pending.ValueRO) && hasPayload && binding.Value != Entity.Null)
                {
                    var result = TimelineEssenceResolver.TryResolveLinkedTarget(data.Route, data.LinkMiss,
                        binding.Value, TargetsLookup, LinkSources, Links, out target);

                    if (result == ResolveResult.Dropped)
                    {
                        // Link missing under Drop policy: consume the one-shot latch without firing anywhere.
                        pending.ValueRW = false;
                        return;
                    }

                    if (result == ResolveResult.Resolved
                        && ConditionEvents.HasBuffer(target) && EventsDirty.HasComponent(target))
                    {
                        resolved = true;
                    }
                }

                var outcome = EssenceDeliveryGate.Evaluate(isEdge, pending.ValueRO, hasPayload, resolved, out var next);
                pending.ValueRW = next;

                if (outcome == EssenceDeliveryGate.Outcome.Fire)
                {
                    EventChanges.Add(target, new EventAmount(data.Event, data.Value));
                    UniqueKeys.Add(target);
                }
            }
        }

        // Clip ended while still owing a delivery: the target/binding/writer never resolved during the whole
        // active window. Clear the latch (so the next activation re-arms) and warn once instead of dropping silently.
        [BurstCompile]
        [WithAll(typeof(TimelineEssenceEventData))]
        [WithDisabled(typeof(ClipActive))]
        [WithAll(typeof(TimelineEssenceDeliveryPending))] // only walk clips that still owe a delivery, not every inactive clip
        private partial struct DiagnoseMissedJob : IJobEntity
        {
            public bool LogEnabled;
            public BLLogger Logger;

            private void Execute(Entity entity, in TimelineEssenceEventData data,
                EnabledRefRW<TimelineEssenceDeliveryPending> pending)
            {
                pending.ValueRW = false;

                if (LogEnabled)
                {
                    var msg = new FixedString512Bytes();
                    msg.Append((FixedString128Bytes)"[Essence] Event clip ");
                    msg.Append(entity.ToFixedString());
                    msg.Append((FixedString128Bytes)" (event id ");
                    msg.Append(data.Event.Value.ID);
                    msg.Append((FixedString512Bytes)") ended without delivering: target/binding/writer never resolved. Check routeTo and that the target carries a ConditionEventWriter (EventWriterAuthoring).");
                    Logger.LogWarning512(msg);
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
        private struct ApplyJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, EventAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public ConditionEventWriter.Lookup Writers;
            public bool LogEnabled;
            public BLLogger Logger;

            public void Execute(int index)
            {
                var key = Keys[index];
                if (Hint.Unlikely(!Writers.TryGet(key, out var writer))) return;

                var values = new FixedList4096Bytes<EventAmount>();

                if (GroupChanges.TryGetFirstValue(key, out var value, out var it))
                {
                    AddOrAccumulate(ref values, value, ref writer);

                    while (GroupChanges.TryGetNextValue(out value, ref it))
                        AddOrAccumulate(ref values, value, ref writer);
                }

                // Skip net-zero accumulations (e.g. a +N and a -N on the same target+key): ConditionEventWriter.Trigger asserts value != 0.
                foreach (var e in values)
                {
                    if (e.Amount != 0)
                    {
                        writer.Trigger(e.Event, e.Amount);
                    }
                    else if (LogEnabled)
                    {
                        // Same-frame clips on this target+key cancelled to a zero sum: the delivery is silently
                        // consumed (both one-shots already latched Fire). Surface it so it isn't a mystery no-op.
                        var msg = new FixedString512Bytes();
                        msg.Append((FixedString128Bytes)"[Essence] Same-frame event writes to ");
                        msg.Append(key.ToFixedString());
                        msg.Append((FixedString128Bytes)" (event id ");
                        msg.Append(e.Event.Value.ID);
                        msg.Append((FixedString128Bytes)") cancelled to zero — nothing fired.");
                        Logger.LogWarning512(msg);
                    }
                }
            }

            private static void AddOrAccumulate(ref FixedList4096Bytes<EventAmount> values, EventAmount value,
                ref ConditionEventWriter writer)
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

                if (value.Amount != 0) writer.Trigger(value.Event, value.Amount);
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