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

    /// <summary>
    /// Enableable latch shared by the Event and Intrinsic clips: enabled while a clip activation still owes a
    /// delivery. Armed on the clip's rising edge, cleared once the payload is actually delivered (binding, target,
    /// and writer all resolved). This decouples delivery from the single-frame ClipActivePrevious edge — which the
    /// core ClipActivePreviousSystem consumes unconditionally — so a transiently-unresolved frame retries across
    /// the whole active window instead of silently dropping the event. A clip only ever carries Event XOR Intrinsic
    /// data, so the two systems' queries partition cleanly on the one component.
    /// </summary>
    public struct TimelineEssenceDeliveryPending : IComponentData, IEnableableComponent
    {
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