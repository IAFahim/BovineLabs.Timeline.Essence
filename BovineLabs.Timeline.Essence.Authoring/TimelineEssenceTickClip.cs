using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Essence.Authoring;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Essence.Data;
using BovineLabs.Timeline.Essence.Data.Builders;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    public sealed class TimelineEssenceTickClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Optional link key; re-routes from the resolved target to its linked entity.")]
        public EntityLinkSchema routeLink;

        [Tooltip("What to do when routeLink is set but the link cannot be resolved: FallbackToTarget fires at the " +
                 "unlinked target (legacy, can misdirect effects), Retry waits for the link, Drop consumes the clip without firing.")]
        public LinkMissBehavior linkMissBehavior = LinkMissBehavior.FallbackToTarget;

        [Tooltip("Which entity the ticks land on: the bound entity (Self) or a Targets slot.")]
        public Target routeTo = Target.Self;

        [Tooltip("What each tick does: fire a ConditionEvent, or change an Intrinsic counter.")]
        public EssenceTickMode mode = EssenceTickMode.Event;

        [Tooltip("Event fired per tick (used when Mode = Event). Ticks elapsed in one frame are delivered as ONE " +
                 "event whose value is the SUM (valuePerTick x ticks). Consume with ConditionFeature.Accumulate or " +
                 "GreaterThanEqual — an Equal comparison will miss batched ticks at low FPS.")]
        public ConditionEventObject conditionEvent;

        [Tooltip("Intrinsic changed per tick (used when Mode = Intrinsic).")]
        public IntrinsicSchemaObject intrinsic;

        [Tooltip("Value applied per tick (event payload, or intrinsic delta). Ticks elapsed in one frame are " +
                 "delivered as ONE event whose value is the SUM (valuePerTick x ticks). Consume with " +
                 "ConditionFeature.Accumulate or GreaterThanEqual — an Equal comparison will miss batched ticks at low FPS.")]
        public int valuePerTick = 1;

        [Tooltip("Total number of ticks distributed across the window.")]
        public int tickCount = 5;

        [Tooltip("Seconds over which the ticks are distributed.")]
        public float windowSeconds = 1f;

        [Tooltip("Tick density over normalized time (flat = even spread). Baked into a CDF; the area under " +
                 "the curve is normalized, so only its shape matters.")]
        public AnimationCurve density = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        public override double duration => math.max(0.0001f, windowSeconds);
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            // Surface inert configurations loudly instead of baking a clip that silently does nothing.
            if (mode == EssenceTickMode.Event && conditionEvent == null)
                Debug.LogError($"TimelineEssenceTickClip '{name}': Event mode with no Condition Event assigned — the clip will do nothing.", conditionEvent);
            else if (mode == EssenceTickMode.Intrinsic && intrinsic == null)
                Debug.LogError($"TimelineEssenceTickClip '{name}': Intrinsic mode with no Intrinsic assigned — the clip will do nothing.", intrinsic);
            else if (tickCount <= 0)
                Debug.LogError($"TimelineEssenceTickClip '{name}': tickCount is {tickCount} — the clip will fire no ticks.");
            else if (valuePerTick == 0)
                Debug.LogError($"TimelineEssenceTickClip '{name}': valuePerTick is 0 — ticks would be consumed with no effect.");

            // Skip curve/blob creation for a dead tickCount; TickMath.TryAdvance returns false safely for an uncreated curve.
            var curve = tickCount > 0 ? BuildCurve() : default;
            if (curve.IsCreated)
                context.Baker.AddBlobAsset(ref curve, out _);

            var builder = new EssenceTickBuilder
            {
                Route = EntityLinkAuthoringUtility.BakeRef(context.Baker, routeLink, routeTo),
                Mode = mode,
                Event = mode == EssenceTickMode.Event && conditionEvent ? new ConditionKey(conditionEvent.Key) : ConditionKey.Null,
                Intrinsic = mode == EssenceTickMode.Intrinsic && intrinsic ? (IntrinsicKey)intrinsic.Key.ID : default(IntrinsicKey),
                ValuePerTick = valuePerTick,
                TickCount = math.max(0, tickCount),
                Duration = math.max(0.0001f, windowSeconds),
                Curve = curve,
                LinkMiss = linkMissBehavior
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);

            base.Bake(clipEntity, context);
        }

        private BlobAssetReference<DistributionCurveBlob> BuildCurve()
        {
            const int samples = 32;

            var sampled = new NativeArray<float>(samples, Allocator.Temp);
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)(samples - 1);
                sampled[i] = math.max(0f, density?.Evaluate(t) ?? 1f);
            }

            var normalized = new NativeArray<float>(samples, Allocator.Temp);
            CdfIntegration.BuildNormalizedCdf(sampled, normalized);
            sampled.Dispose();

            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<DistributionCurveBlob>();
            var cdf = builder.Allocate(ref root.Cdf, samples);
            for (var i = 0; i < samples; i++)
                cdf[i] = normalized[i];

            normalized.Dispose();
            return builder.CreateBlobAssetReference<DistributionCurveBlob>(Allocator.Persistent);
        }
    }
}