using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Essence.Data;
using Unity.Entities;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    public sealed class TimelineEssenceEventClip : DOTSClip, ITimelineClipAsset
    {
        public Target routeTo = Target.Self;
        public EntityLinkSchema routeLink;
        public ConditionEventObject conditionEvent;
        public int value = 1;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            EntityLinkAuthoringUtility.TryGetKey(routeLink, out var linkKey);

            context.Baker.AddComponent(clipEntity, new TimelineEssenceEventData
            {
                RouteTo = routeTo,
                RouteLinkKey = linkKey,
                Event = conditionEvent ? conditionEvent.Key : ConditionKey.Null,
                Value = value
            });

            base.Bake(clipEntity, context);
        }
    }
}