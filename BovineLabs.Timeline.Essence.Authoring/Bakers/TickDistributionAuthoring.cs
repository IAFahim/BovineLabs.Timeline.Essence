namespace BovineLabs.Timeline.Essence.Authoring.Bakers
{
    using System;
    using BovineLabs.Essence.Authoring;
    using BovineLabs.Reaction.Authoring.Conditions;
    using BovineLabs.Reaction.Data.Core;
    using UnityEngine;

    [Serializable]
    public class TickDistributionAuthoring
    {
        [Tooltip("X: 0..1 (Time), Y: 0..1 (Tick CDF)")]
        public AnimationCurve Curve = AnimationCurve.Linear(0, 0, 1, 1);

        public StatSchemaObject TotalTicksStat;
        public Target StatTarget = Target.Target;

        [Header("Outputs")]
        public IntrinsicSchemaObject Intrinsic;
        public Target IntrinsicTarget = Target.Target;
        public ConditionEventObject Event;

        [Header("Gizmo")]
        public int GizmoSimulatedTotalTicks = 20;
        public float GizmoSimulatedDuration = 5f;
    }
}