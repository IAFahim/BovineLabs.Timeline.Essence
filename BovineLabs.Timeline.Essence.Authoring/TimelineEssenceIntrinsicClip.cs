using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Essence.Authoring;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.Essence.Data;
using BovineLabs.Timeline.Essence.Data.Builders;
using BovineLabs.Timeline.EntityLinks.Authoring;
using Unity.Entities;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    public sealed class TimelineEssenceIntrinsicClip : DOTSClip, ITimelineClipAsset
    {
        public Target routeTo = Target.Self;
        public EntityLinkSchema routeLink;
        public IntrinsicSchemaObject intrinsic;
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