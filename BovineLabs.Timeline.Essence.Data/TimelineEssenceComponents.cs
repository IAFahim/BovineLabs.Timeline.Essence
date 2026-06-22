using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Data
{
    public struct TimelineEssenceEventData : IComponentData
    {
        public Target RouteTo;
        public ushort RouteLinkKey;
        public ConditionKey Event;
        public int Value;
    }

    public struct TimelineEssenceIntrinsicData : IComponentData
    {
        public Target RouteTo;
        public ushort RouteLinkKey;
        public IntrinsicKey Intrinsic;
        public int Amount;
    }

    public struct TimelineEssenceStatData : IComponentData
    {
        public Target RouteTo;
        public ushort RouteLinkKey;
        public StatKey Stat;
        public StatModifyType ModifyType;
        public float Value;
    }

    public struct TimelineEssenceStatState : IComponentData
    {
        public Entity AppliedTarget;
    }

    public enum EssenceTickMode : byte
    {
        Event,
        Intrinsic
    }

    public struct TimelineEssenceTickData : IComponentData
    {
        public Target RouteTo;
        public ushort RouteLinkKey;
        public EssenceTickMode Mode;
        public ConditionKey Event;
        public IntrinsicKey Intrinsic;
        public int ValuePerTick;
        public int TickCount;
        public float Duration;
        public BlobAssetReference<DistributionCurveBlob> Curve;
    }

    public struct TimelineEssenceTickState : IComponentData
    {
        public int Fired;
    }
}