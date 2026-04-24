// BovineLabs.Essence.Data/Actions/ActionTickDistribution.cs
namespace BovineLabs.Essence.Data.Actions
{
    using BovineLabs.Core.Collections;
    using BovineLabs.Reaction.Data.Conditions;
    using BovineLabs.Reaction.Data.Core;
    using Unity.Entities;

    public struct ActionTickDistribution : IComponentData
    {
        public BlobAssetReference<BlobCurve> Curve;
        public Target From;
        public StatKey TicPerSecond;
        public StatKey TicDuration;
        public ConditionKey OnEnd;
        public Target To;
        public IntrinsicKey TickStore;
        public ConditionKey OnTic;
    }
}