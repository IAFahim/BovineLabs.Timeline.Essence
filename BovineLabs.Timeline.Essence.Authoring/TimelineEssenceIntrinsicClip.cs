using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Essence.Authoring;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Essence.Data;
using BovineLabs.Timeline.Essence.Data.Builders;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    public sealed class TimelineEssenceIntrinsicClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Optional link key; re-routes from the resolved target to its linked entity.")]
        public EntityLinkSchema routeLink;

        [Tooltip("What to do when routeLink is set but the link cannot be resolved: FallbackToTarget fires at the " +
                 "unlinked target (legacy, can misdirect effects), Retry waits for the link, Drop consumes the clip without firing.")]
        public LinkMissBehavior linkMissBehavior = LinkMissBehavior.FallbackToTarget;

        [Tooltip("Which entity the counter change lands on: the bound entity (Self) or a Targets slot.")]
        public Target routeTo = Target.Self;

        [Tooltip("The intrinsic counter to change on clip enter.")]
        public IntrinsicSchemaObject intrinsic;

        [Tooltip("Amount added to the counter (negative to consume). Applied once on enter.")]
        public int amount = 1;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            if (intrinsic == null)
            {
                Debug.LogError(
                    $"{nameof(TimelineEssenceIntrinsicClip)} '{name}' has no Intrinsic assigned; the clip will not change any counter.",
                    this);
                return;
            }

            if (intrinsic.Key.ID == 0)
            {
                Debug.LogError(
                    $"{nameof(TimelineEssenceIntrinsicClip)} '{name}': intrinsic schema '{intrinsic.name}' has key 0 — asset not imported/registered; re-import it.",
                    this);
                return;
            }

            if (amount == 0)
            {
                Debug.LogWarning(
                    $"{nameof(TimelineEssenceIntrinsicClip)} '{name}': amount is 0 — the clip changes the counter by nothing.",
                    this);
            }

            var builder = new EssenceIntrinsicBuilder
            {
                Route = EntityLinkAuthoringUtility.BakeRef(context.Baker, routeLink, routeTo),
                Intrinsic = intrinsic ? (IntrinsicKey)intrinsic.Key.ID : default(IntrinsicKey),
                Amount = amount,
                LinkMiss = linkMissBehavior
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);

            base.Bake(clipEntity, context);
        }
    }
}