using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.Essence.Data;
using Unity.Entities;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    public sealed class TimelineEssenceEventClip : DOTSClip, ITimelineClipAsset
    {
        public Target routeTo = Target.Self;
        public ConditionEventObject conditionEvent;
        public int value = 1;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            context.Baker.AddComponent(clipEntity, new TimelineEssenceEventData
            {
                RouteTo = routeTo,
                Event = conditionEvent ? conditionEvent.Key : ConditionKey.Null,
                Value = value
            });

            base.Bake(clipEntity, context);
        }
    }
}