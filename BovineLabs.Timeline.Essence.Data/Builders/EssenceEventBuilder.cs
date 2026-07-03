using BovineLabs.Core.EntityCommands;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Timeline.EntityLinks.Data;

namespace BovineLabs.Timeline.Essence.Data.Builders
{
    public struct EssenceEventBuilder
    {
        public EntityLinkRef Route;
        public ConditionKey Event;
        public int Value;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new TimelineEssenceEventData
            {
                Route = Route,
                Event = Event,
                Value = Value
            });

            // Delivery latch starts disabled; armed on the clip's first rising edge at runtime.
            builder.AddComponent<TimelineEssenceDeliveryPending>();
            builder.SetComponentEnabled<TimelineEssenceDeliveryPending>(false);
        }
    }
}