namespace BovineLabs.Timeline.Essence.Data.TickDistribution
{
    using BovineLabs.Core.Collections;
    using BovineLabs.Essence.Data;
    using BovineLabs.Reaction.Data.Conditions;
    using BovineLabs.Reaction.Data.Core;
    using Unity.Entities;

    public struct TickDistributionCurve : IComponentData
    {
        public BlobAssetReference<BlobCurve> Value;
        public StatKey TotalTicksStat;
        public Target StatTarget;
        
        public IntrinsicKey Intrinsic;
        public Target IntrinsicTarget;
        public ConditionKey Event;
    }

    public struct TickDistributionState : IComponentData
    {
        public int AppliedTicks;
        public int TicksThisFrame;
    }
}