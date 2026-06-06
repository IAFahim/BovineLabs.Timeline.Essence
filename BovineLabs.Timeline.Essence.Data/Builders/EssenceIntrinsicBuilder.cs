using BovineLabs.Core.EntityCommands;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Essence.Data;

namespace BovineLabs.Timeline.Essence.Data.Builders
{
    public struct EssenceIntrinsicBuilder
    {
        public Target RouteTo;
        public ushort RouteLinkKey;
        public IntrinsicKey Intrinsic;
        public int Amount;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new TimelineEssenceIntrinsicData
            {
                RouteTo = RouteTo,
                RouteLinkKey = RouteLinkKey,
                Intrinsic = Intrinsic,
                Amount = Amount
            });
        }
    }
}