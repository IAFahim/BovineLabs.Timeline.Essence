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
        [Tooltip("Which entity the ticks land on: the bound entity (Self) or a Targets slot.")]
        public Target routeTo = Target.Self;

        [Tooltip("Optional link key; re-routes from the resolved target to its linked entity.")]
        public EntityLinkSchema routeLink;

        [Tooltip("What each tick does: fire a ConditionEvent, or change an Intrinsic counter.")]
        public EssenceTickMode mode = EssenceTickMode.Event;

        [Tooltip("Event fired per tick (used when Mode = Event).")]
        public ConditionEventObject conditionEvent;

        [Tooltip("Intrinsic changed per tick (used when Mode = Intrinsic).")]
        public IntrinsicSchemaObject intrinsic;

        [Tooltip("Value applied per tick (event payload, or intrinsic delta).")]
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
            EntityLinkAuthoringUtility.TryGetKey(routeLink, out var linkKey);

            var curve = BuildCurve();
            if (curve.IsCreated)
                context.Baker.AddBlobAsset(ref curve, out _);

            var builder = new EssenceTickBuilder
            {
                RouteTo = routeTo,
                RouteLinkKey = linkKey,
                Mode = mode,
                Event = mode == EssenceTickMode.Event && conditionEvent ? conditionEvent.Key : ConditionKey.Null,
                Intrinsic = mode == EssenceTickMode.Intrinsic && intrinsic ? intrinsic.Key : default(IntrinsicKey),
                ValuePerTick = valuePerTick,
                TickCount = math.max(0, tickCount),
                Duration = math.max(0.0001f, windowSeconds),
                Curve = curve
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);

            base.Bake(clipEntity, context);
        }

        private BlobAssetReference<DistributionCurveBlob> BuildCurve()
        {
            const int samples = 32;
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<DistributionCurveBlob>();
            var cdf = builder.Allocate(ref root.Cdf, samples);

            var running = new float[samples];
            var previous = math.max(0f, density?.Evaluate(0f) ?? 1f);
            var cumulative = 0f;
            running[0] = 0f;
            for (var i = 1; i < samples; i++)
            {
                var t = i / (float)(samples - 1);
                var d = math.max(0f, density?.Evaluate(t) ?? 1f);
                cumulative += 0.5f * (d + previous);
                running[i] = cumulative;
                previous = d;
            }

            if (cumulative > 0f)
                for (var i = 0; i < samples; i++)
                    cdf[i] = running[i] / cumulative;
            else
                for (var i = 0; i < samples; i++)
                    cdf[i] = i / (float)(samples - 1);

            return builder.CreateBlobAssetReference<DistributionCurveBlob>(Allocator.Persistent);
        }
    }
}