using BovineLabs.Core.EntityCommands;
using BovineLabs.Essence.Data;
using BovineLabs.Timeline.EntityLinks.Data;

namespace BovineLabs.Timeline.Essence.Data.Builders
{
    public struct EssenceIntrinsicBuilder
    {
        public EntityLinkRef Route;
        public IntrinsicKey Intrinsic;
        public int Amount;
        public LinkMissBehavior LinkMiss;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new TimelineEssenceIntrinsicData
            {
                Route = Route,
                Intrinsic = Intrinsic,
                Amount = Amount,
                LinkMiss = LinkMiss
            });

            // Delivery latch starts disabled; armed on the clip's first rising edge at runtime.
            builder.AddComponent<TimelineEssenceDeliveryPending>();
            builder.SetComponentEnabled<TimelineEssenceDeliveryPending>(false);
        }
    }
}