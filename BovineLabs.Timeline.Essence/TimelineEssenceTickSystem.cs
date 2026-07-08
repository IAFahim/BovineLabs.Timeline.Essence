using BovineLabs.Core;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Essence;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Core;
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
            state.RequireForUpdate<TimelineEssenceTickData>();
            // Tick clips fire ConditionEvents via ConditionEventWriter, whose generated code fetches these; require
            // them so a bare world (no Reaction bootstrap) never throws inside the writer.
            state.RequireForUpdate<ConditionConfig>();
            state.RequireForUpdate<ConditionEventPayloadAllocator>();

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

            // Warn once, on the falling edge, when a tick clip ended having fired fewer ticks than its CDF target
            // (target/writer never resolved, or ticks dropped) — parity with the Event/Intrinsic DiagnoseMissedJob.
            var hasLogger = SystemAPI.TryGetSingleton<BLLogger>(out var logger);
            state.Dependency = new DiagnoseMissedTickJob
            {
                LogEnabled = hasLogger,
                Logger = logger
            }.ScheduleParallel(state.Dependency);
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

                if (data.Event.Equals(ConditionKey.Null))
                    return;

                // Zero payload: the ticks were legitimately consumed but there is nothing to fire, and
                // ConditionEventWriter.Trigger asserts value != 0 (throws in editor/dev builds). Do NOT un-commit.
                var value = data.ValuePerTick * delta;
                if (value == 0)
                    return;

                var result = TimelineEssenceResolver.TryResolveLinkedTarget(data.Route, data.LinkMiss, binding.Value,
                    TargetsLookup, LinkSources, Links, out var entity);

                if (result == ResolveResult.Resolved && EventWriters.TryGet(entity, out var writer))
                    writer.Trigger(data.Event, value);
                else if (result == ResolveResult.Dropped)
                {
                    // Link missing under Drop policy: ticks stay committed (consumed), nothing fires, no retry.
                }
                else
                    tickState.Fired -= delta; // RetryLater, or resolved-but-writer-missing: un-commit so these ticks retry next frame
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

                if (data.Intrinsic.Value.IsNull)
                    return;

                var result = TimelineEssenceResolver.TryResolveLinkedTarget(data.Route, data.LinkMiss, binding.Value,
                    TargetsLookup, LinkSources, Links, out var entity);

                if (result == ResolveResult.Resolved && IntrinsicWriters.TryGet(entity, out var writer))
                    writer.Add(data.Intrinsic, data.ValuePerTick * delta);
                else if (result == ResolveResult.Dropped)
                {
                    // Link missing under Drop policy: ticks stay committed (consumed), nothing fires, no retry.
                }
                else
                    tickState.Fired -= delta; // RetryLater, or resolved-but-writer-missing: un-commit so these ticks retry next frame
            }
        }

        // Falling edge (ClipActive just disabled, ClipActivePrevious still on): a tick clip that ended having fired
        // fewer ticks than its CDF end target never fully resolved its target/writer (or dropped ticks). Warn once.
        [BurstCompile]
        [WithAll(typeof(ClipActivePrevious))]
        [WithDisabled(typeof(ClipActive))]
        private partial struct DiagnoseMissedTickJob : IJobEntity
        {
            public bool LogEnabled;
            public BLLogger Logger;

            private void Execute(Entity entity, in TimelineEssenceTickData data, in TimelineEssenceTickState st)
            {
                if (!LogEnabled)
                    return;

                if (data.TickCount > 0 && data.Curve.IsCreated && st.Fired < data.TickCount)
                {
                    var msg = new FixedString512Bytes();
                    msg.Append((FixedString128Bytes)"[Essence] Tick clip ");
                    msg.Append(entity.ToFixedString());
                    msg.Append((FixedString128Bytes)" ended having fired ");
                    msg.Append(st.Fired);
                    msg.Append((FixedString32Bytes)"/");
                    msg.Append(data.TickCount);
                    msg.Append((FixedString128Bytes)" ticks: target/writer never resolved (or ticks dropped).");
                    Logger.LogWarning512(msg);
                }
            }
        }
    }
}