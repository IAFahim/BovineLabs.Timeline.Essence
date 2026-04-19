namespace BovineLabs.Timeline.Essence.Data.TickDistribution
{
    using BovineLabs.Core.Collections;
    using BovineLabs.Essence.Data;
    using BovineLabs.Reaction.Data.Conditions;
    using BovineLabs.Reaction.Data.Core;
    using Unity.Entities;

    public struct TickDistributionClipData : IComponentData
    {
        public BlobAssetReference<BlobCurve> Curve;
        public StatKey TotalTicksStat;
        public Target StatTarget;
        
        public IntrinsicKey Intrinsic;
        public Target IntrinsicTarget;
        public ConditionKey Event;
    }
}