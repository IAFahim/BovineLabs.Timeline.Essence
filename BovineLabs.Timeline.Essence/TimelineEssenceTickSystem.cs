using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Essence;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Essence.Data;
using BovineLabs.Reaction.Data.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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
        private IntrinsicWriter.SingletonData _intrinsicWriterSingletonData;
        private IntrinsicWriter.Lookup _intrinsicWriters;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EssenceConfig>();

            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _linkSourceLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _linkLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
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
            _eventWriters.Update(ref state);
            _intrinsicWriters.Update(ref state, _intrinsicWriterSingletonData);

            // Two single-writer jobs: IntrinsicWriter.Lookup nests a ConditionEventWriter, so it cannot share a
            // job with a standalone ConditionEventWriter.Lookup (the EventsDirty lookup would alias). Each job
            // handles one payload mode; the shared TickState write serializes them.
            state.Dependency = new EventTickJob
            {
                TargetsLookup = _targetsLookup,
                LinkSources = _linkSourceLookup,
                Links = _linkLookup,
                EventWriters = _eventWriters,
            }.Schedule(state.Dependency);

            state.Dependency = new IntrinsicTickJob
            {
                TargetsLookup = _targetsLookup,
                LinkSources = _linkSourceLookup,
                Links = _linkLookup,
                IntrinsicWriters = _intrinsicWriters,
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

                if (!TickMath.TryAdvance(data, localTime, !previous.ValueRO, ref tickState, out var delta))
                    return;

                if (data.Event == ConditionKey.Null)
                    return;

                if (TimelineEssenceResolver.TryResolveLinkedTarget(data.RouteTo, data.RouteLinkKey, binding.Value,
                        TargetsLookup, LinkSources, Links, out var entity) &&
                    EventWriters.TryGet(entity, out var writer))
                    writer.Trigger(data.Event, data.ValuePerTick * delta);
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

                if (!TickMath.TryAdvance(data, localTime, !previous.ValueRO, ref tickState, out var delta))
                    return;

                if (data.Intrinsic.Value == 0)
                    return;

                if (TimelineEssenceResolver.TryResolveLinkedTarget(data.RouteTo, data.RouteLinkKey, binding.Value,
                        TargetsLookup, LinkSources, Links, out var entity) &&
                    IntrinsicWriters.TryGet(entity, out var writer))
                    writer.Add(data.Intrinsic, data.ValuePerTick * delta);
            }
        }
    }

    internal static class TickMath
    {
        // Advance the CDF distribution: reset on the activation edge, then fire the positive delta of
        // round(CDF(t) * TickCount). Returns false (delta = 0) when nothing should fire this frame.
        public static bool TryAdvance(in TimelineEssenceTickData data, in LocalTime localTime, bool justActivated,
            ref TimelineEssenceTickState tickState, out int delta)
        {
            delta = 0;

            if (justActivated)
                tickState.Fired = 0;

            if (data.TickCount <= 0 || !data.Curve.IsCreated)
                return false;

            var t = data.Duration > 0f ? math.saturate((float)(double)localTime.Value / data.Duration) : 1f;
            var target = math.clamp((int)math.round(data.Curve.Value.Evaluate(t) * data.TickCount), 0,
                data.TickCount);

            delta = target - tickState.Fired;
            if (delta <= 0)
            {
                delta = 0;
                return false;
            }

            tickState.Fired = target;
            return true;
        }
    }
}
