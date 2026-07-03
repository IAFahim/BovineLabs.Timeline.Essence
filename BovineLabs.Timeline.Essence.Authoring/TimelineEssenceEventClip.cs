using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Essence.Data.Builders;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    public sealed class TimelineEssenceEventClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Optional link key; re-routes from the resolved target to its linked entity.")]
        public EntityLinkSchema routeLink;

        [Tooltip("Which entity the event fires at: the bound entity (Self) or a Targets slot.")]
        public Target routeTo = Target.Self;

        [Tooltip("The condition event to fire on clip enter.")]
        public ConditionEventObject conditionEvent;

        [Tooltip("Payload value sent with the event.")]
        public int value = 1;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            if (conditionEvent == null)
            {
                Debug.LogError(
                    $"{nameof(TimelineEssenceEventClip)} '{name}' has no ConditionEvent assigned; the clip will not fire any event.",
                    this);
                return;
            }
            var builder = new EssenceEventBuilder
            {
                Route = EntityLinkAuthoringUtility.BakeRef(context.Baker, routeLink, routeTo),
                Event = conditionEvent ? conditionEvent.Key : ConditionKey.Null,
                Value = value
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);

            base.Bake(clipEntity, context);
        }
    }
}