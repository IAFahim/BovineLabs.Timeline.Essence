using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Essence.Authoring;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Essence.Data.Builders;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    public sealed class TimelineEssenceIntrinsicClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Which entity the counter change lands on: the bound entity (Self) or a Targets slot.")]
        public Target routeTo = Target.Self;

        [Tooltip("Optional link key; re-routes from the resolved target to its linked entity.")]
        public EntityLinkSchema routeLink;

        [Tooltip("The intrinsic counter to change on clip enter.")]
        public IntrinsicSchemaObject intrinsic;

        [Tooltip("Amount added to the counter (negative to consume). Applied once on enter.")]
        public int amount = 1;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            EntityLinkAuthoringUtility.TryGetKey(routeLink, out var linkKey);

            var builder = new EssenceIntrinsicBuilder
            {
                RouteTo = routeTo,
                RouteLinkKey = linkKey,
                Intrinsic = intrinsic ? intrinsic.Key : default(IntrinsicKey),
                Amount = amount
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);

            base.Bake(clipEntity, context);
        }
    }
}