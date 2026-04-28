using System;
using BovineLabs.Core.Collections;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Essence.Data;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace BovineLabs.Timeline.Essence
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    public partial struct TimelineEssenceEventSystem : ISystem
    {
        private NativeParallelMultiHashMapFallback<Entity, EventAmount> eventChanges;
        private NativeList<Entity> uniqueKeys;
        private ComponentLookup<Targets> targetsLookup;
        private ComponentLookup<TargetsCustom> customsLookup;
        private ConditionEventWriter.Lookup writers;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            targetsLookup = state.GetComponentLookup<Targets>(true);
            customsLookup = state.GetComponentLookup<TargetsCustom>(true);
            writers.Create(ref state);
        }

        public void OnDestroy(ref SystemState state)
        {
            eventChanges.Dispose();
            uniqueKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            targetsLookup.Update(ref state);
            customsLookup.Update(ref state);
            writers.Update(ref state);

            state.Dependency = new GatherJob
            {
                EventChanges = eventChanges.AsWriter(),
                TargetsLookup = targetsLookup,
                CustomsLookup = customsLookup
            }.ScheduleParallel(state.Dependency);

            state.Dependency = eventChanges.Apply(state.Dependency, out var reader);

            state.Dependency = new GetKeysJob
            {
                UniqueKeys = uniqueKeys,
                GroupChanges = reader
            }.Schedule(state.Dependency);

            state.Dependency = new ApplyJob
            {
                Keys = uniqueKeys,
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
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public ComponentLookup<TargetsCustom> CustomsLookup;

            private void Execute(in TrackBinding binding, in TimelineEssenceEventData data)
            {
                if (data.Event == ConditionKey.Null || binding.Value == Entity.Null) return;

                if (TimelineEssenceResolver.TryResolveTarget(data.RouteTo, binding.Value, TargetsLookup, CustomsLookup, out var target))
                {
                    EventChanges.Add(target, new EventAmount(data.Event, data.Value));
                }
            }
        }

        [BurstCompile]
        private struct GetKeysJob : IJob
        {
            public NativeList<Entity> UniqueKeys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, EventAmount>.ReadOnly GroupChanges;

            public void Execute() => GroupChanges.GetUniqueKeyArray(UniqueKeys);
        }

        [BurstCompile]
        private struct ApplyJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeList<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, EventAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public ConditionEventWriter.Lookup Writers;

            public void Execute(int index)
            {
                var key = Keys[index];
                if (Hint.Unlikely(!Writers.TryGet(key, out var writer))) return;

                var values = new NativeList<EventAmount>(Allocator.Temp);
                GroupChanges.TryGetFirstValue(key, out var value, out var it);
                values.Add(value);

                while (GroupChanges.TryGetNextValue(out value, ref it))
                {
                    var existingIndex = values.IndexOf(value);
                    if (Hint.Unlikely(existingIndex == -1)) values.Add(value);
                    else values.ElementAt(existingIndex).Amount += value.Amount;
                }

                foreach (var e in values) writer.Trigger(e.Event, e.Amount);
                values.Dispose();
            }
        }

        private struct EventAmount : IEquatable<EventAmount>
        {
            public readonly ConditionKey Event;
            public int Amount;

            public EventAmount(ConditionKey evt, int amount) { Event = evt; Amount = amount; }
            public bool Equals(EventAmount other) => Event.Equals(other.Event);
            public override int GetHashCode() => Event.GetHashCode();
        }
    }
}
