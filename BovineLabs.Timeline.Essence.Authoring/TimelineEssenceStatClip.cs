using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Essence.Authoring;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Essence.Data;
using Unity.Entities;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    public sealed class TimelineEssenceStatClip : DOTSClip, ITimelineClipAsset
    {
        public Target routeTo = Target.Self;
        public EntityLinkSchema routeLink;
        public StatSchemaObject stat;
        public StatAuthoringType modifyType = StatAuthoringType.Added;
        public float value;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Looping;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            if (stat == null) return;

            var commands = new BakerCommands(context.Baker, clipEntity);

            EntityLinkAuthoringUtility.TryGetKey(routeLink, out var linkKey);

            commands.AddComponent(new TimelineEssenceStatData
            {
                RouteTo = routeTo,
                RouteLinkKey = linkKey,
                Stat = stat.Key,
                ModifyType = StatAuthoringUtil.GetModifier(modifyType),
                Value = modifyType is StatAuthoringType.Subtracted or StatAuthoringType.Reduced
                    or StatAuthoringType.Less
                    ? -value
                    : value
            });

            base.Bake(clipEntity, context);
        }
    }
}