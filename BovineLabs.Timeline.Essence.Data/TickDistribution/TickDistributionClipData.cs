namespace BovineLabs.Timeline.Essence.Data.TickDistribution
{
    using BovineLabs.Essence.Data;
    using BovineLabs.Reaction.Data.Conditions;
    using BovineLabs.Reaction.Data.Core;
    using Unity.Entities;

    public struct TickDistributionClipData : IComponentData
    {
        public BlobAssetReference<DistributionCurveBlob> Cdf;
        public StatKey TotalTicksStat;
        public Target StatTarget;
        
        public IntrinsicKey Intrinsic;
        public Target IntrinsicTarget;
        public ConditionKey Event;
    }
}