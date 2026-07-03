using BovineLabs.Core.EntityCommands;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Timeline.EntityLinks.Data;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Data.Builders
{
    public struct EssenceTickBuilder
    {
        public EntityLinkRef Route;
        public EssenceTickMode Mode;
        public ConditionKey Event;
        public IntrinsicKey Intrinsic;
        public int ValuePerTick;
        public int TickCount;
        public float Duration;
        public BlobAssetReference<DistributionCurveBlob> Curve;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new TimelineEssenceTickData
            {
                Route = Route,
                Mode = Mode,
                Event = Event,
                Intrinsic = Intrinsic,
                ValuePerTick = ValuePerTick,
                TickCount = TickCount,
                Duration = Duration,
                Curve = Curve
            });
            builder.AddComponent(new TimelineEssenceTickState { Fired = 0 });
        }
    }
}