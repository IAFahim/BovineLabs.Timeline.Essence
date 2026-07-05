using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Essence;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Essence.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(EntityLinkTargetPatchSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct TimelineEssenceTickSystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _linkSourceLookup;
        private UnsafeBufferLookup<EntityLinkEntry> _linkLookup;
        private ConditionEventWriter.Lookup _eventWriters;
        private ConditionEventWriter.SingletonData _eventWritersSingletonData;
        private IntrinsicWriter.SingletonData _intrinsicWriterSingletonData;
        private IntrinsicWriter.Lookup _intrinsicWriters;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EssenceConfig>();

            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _linkSourceLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _linkLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _eventWritersSingletonData.Create(ref state);
            _eventWriters.Create(ref state);
            _intrinsicWriterSingletonData.Create(ref state);
            _intrinsicWriters.Create(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _targetsLookup.Update(ref state);
            _linkSourceLookup.Update(ref state);
            _linkLookup.Update(ref state);
            _eventWriters.Update(ref state, _eventWritersSingletonData);
            _intrinsicWriters.Update(ref state, _intrinsicWriterSingletonData);

            state.Dependency = new EventTickJob
            {
                TargetsLookup = _targetsLookup,
                LinkSources = _linkSourceLookup,
                Links = _linkLookup,
                EventWriters = _eventWriters
            }.Schedule(state.Dependency);

            state.Dependency = new IntrinsicTickJob
            {
                TargetsLookup = _targetsLookup,
                LinkSources = _linkSourceLookup,
                Links = _linkLookup,
                IntrinsicWriters = _intrinsicWriters
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct EventTickJob : IJobEntity
        {
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;
            public ConditionEventWriter.Lookup EventWriters;

            private void Execute(in TrackBinding binding, in TimelineEssenceTickData data, in LocalTime localTime,
                EnabledRefRO<ClipActivePrevious> previous, ref TimelineEssenceTickState tickState)
            {
                if (data.Mode != EssenceTickMode.Event || binding.Value == Entity.Null)
                    return;

                if (!TickMath.TryAdvance(data, (double)localTime.Value, !previous.ValueRO, ref tickState, out var delta))
                    return;

                if (data.Event == ConditionKey.Null)
                    return;

                // Zero payload: the ticks were legitimately consumed but there is nothing to fire, and
                // ConditionEventWriter.Trigger asserts value != 0 (throws in editor/dev builds). Do NOT un-commit.
                var value = data.ValuePerTick * delta;
                if (value == 0)
                    return;

                if (TimelineEssenceResolver.TryResolveLinkedTarget(data.Route, binding.Value,
                        TargetsLookup, LinkSources, Links, out var entity) &&
                    EventWriters.TryGet(entity, out var writer))
                    writer.Trigger(data.Event, value);
                else
                    tickState.Fired -= delta; // target/writer not resolved yet: un-commit so these ticks retry next frame instead of being silently lost
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct IntrinsicTickJob : IJobEntity
        {
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;
            public IntrinsicWriter.Lookup IntrinsicWriters;

            private void Execute(in TrackBinding binding, in TimelineEssenceTickData data, in LocalTime localTime,
                EnabledRefRO<ClipActivePrevious> previous, ref TimelineEssenceTickState tickState)
            {
                if (data.Mode != EssenceTickMode.Intrinsic || binding.Value == Entity.Null)
                    return;

                if (!TickMath.TryAdvance(data, (double)localTime.Value, !previous.ValueRO, ref tickState, out var delta))
                    return;

                if (data.Intrinsic.Value == 0)
                    return;

                if (TimelineEssenceResolver.TryResolveLinkedTarget(data.Route, binding.Value,
                        TargetsLookup, LinkSources, Links, out var entity) &&
                    IntrinsicWriters.TryGet(entity, out var writer))
                    writer.Add(data.Intrinsic, data.ValuePerTick * delta);
                else
                    tickState.Fired -= delta; // target/writer not resolved yet: un-commit so these ticks retry next frame instead of being silently lost
            }
        }
    }
}