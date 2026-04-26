using BovineLabs.Core.Collections;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using Unity.Entities;

namespace BovineLabs.Essence.Data.Actions
{
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