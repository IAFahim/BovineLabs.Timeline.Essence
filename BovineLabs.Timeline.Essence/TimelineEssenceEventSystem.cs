using System;
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
    [Unity.Entities.WorldSystemFilter(Unity.Entities.WorldSystemFilterFlags.LocalSimulation | Unity.Entities.WorldSystemFilterFlags.ClientSimulation | Unity.Entities.WorldSystemFilterFlags.ServerSimulation)]
    public partial struct TimelineEssenceEventSystem : ISystem
    {
        private NativeParallelMultiHashMapFallback<Entity, EventAmount> eventChanges;
        private NativeParallelHashSet<Entity> uniqueKeySet;
        private NativeList<Entity> uniqueKeys;

        private ComponentLookup<Targets> targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> linkSourceLookup;
        private UnsafeBufferLookup<EntityLinkEntry> linkLookup;
        private ConditionEventWriter.Lookup writers;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            uniqueKeySet = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            targetsLookup = state.GetComponentLookup<Targets>(true);
            linkSourceLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            linkLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            writers.Create(ref state);
        }

        public void OnDestroy(ref SystemState state)
        {
            eventChanges.Dispose();
            uniqueKeySet.Dispose();
            uniqueKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            targetsLookup.Update(ref state);
            linkSourceLookup.Update(ref state);
            linkLookup.Update(ref state);
            writers.Update(ref state);
            uniqueKeySet.Clear();

            state.Dependency = new GatherJob
            {
                EventChanges = eventChanges.AsWriter(),
                UniqueKeys = uniqueKeySet.AsParallelWriter(),
                TargetsLookup = targetsLookup,
                LinkSources = linkSourceLookup,
                Links = linkLookup
            }.ScheduleParallel(state.Dependency);

            state.Dependency = eventChanges.Apply(state.Dependency, out var reader);

            state.Dependency = new GetKeysJob
            {
                UniqueKeys = uniqueKeys,
                UniqueKeySet = uniqueKeySet
            }.Schedule(state.Dependency);

            state.Dependency = new ApplyJob
            {
                Keys = uniqueKeys.AsDeferredJobArray(),
                GroupChanges = reader,
                Writers = writers
            }.Schedule(uniqueKeys, 64, state.Dependency);

            state.Dependency = eventChanges.Clear(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        [WithDisabled(typeof(ClipActivePrevious))]
        private partial struct GatherJob : IJobEntity
        {
            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;
            public NativeParallelHashSet<Entity>.ParallelWriter UniqueKeys;

            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;

            private void Execute(in TrackBinding binding, in TimelineEssenceEventData data)
            {
                if (data.Event == ConditionKey.Null || binding.Value == Entity.Null) return;

                if (TimelineEssenceResolver.TryResolveLinkedTarget(data.RouteTo, data.RouteLinkKey, binding.Value,
                        TargetsLookup, LinkSources, Links, out var target))
                {
                    EventChanges.Add(target, new EventAmount(data.Event, data.Value));
                    UniqueKeys.Add(target);
                }
            }
        }

        // ... (GetKeysJob, ApplyJob, and EventAmount stay the same as before) ...
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

                foreach (var e in values) writer.Trigger(e.Event, e.Amount);
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

                writer.Trigger(value.Event, value.Amount);
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