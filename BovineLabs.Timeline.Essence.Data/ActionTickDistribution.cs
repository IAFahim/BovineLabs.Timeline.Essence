namespace BovineLabs.Essence.Data.Actions
{
    using BovineLabs.Reaction.Data.Conditions;
    using BovineLabs.Reaction.Data.Core;
    using Unity.Entities;

    [InternalBufferCapacity(1)]
    public struct ActionTickDistribution : IBufferElementData
    {
        public BlobAssetReference<DistributionCurveBlob> Cdf;
        
        public StatKey TotalTicksStat;
        public Target StatTarget;
        
        public IntrinsicKey Intrinsic;
        public Target IntrinsicTarget;
        
        public ConditionKey EventKey;
    }

    [InternalBufferCapacity(1)]
    public struct ActionTickDistributionState : IBufferElementData
    {
        public int AppliedTicks;
    }
}