using BovineLabs.Core.EntityCommands;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;

namespace BovineLabs.Timeline.Essence.Data.Builders
{
    public struct EssenceEventBuilder
    {
        public Target RouteTo;
        public ushort RouteLinkKey;
        public ConditionKey Event;
        public int Value;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new TimelineEssenceEventData
            {
                RouteTo = RouteTo,
                RouteLinkKey = RouteLinkKey,
                Event = Event,
                Value = Value
            });

            // Delivery latch starts disabled; armed on the clip's first rising edge at runtime.
            builder.AddComponent<TimelineEssenceDeliveryPending>();
            builder.SetComponentEnabled<TimelineEssenceDeliveryPending>(false);
        }
    }
}