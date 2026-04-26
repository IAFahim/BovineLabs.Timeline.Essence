using BovineLabs.Core.Collections;
using BovineLabs.Essence.Data.Actions;
using BovineLabs.Reaction.Authoring;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Essence.Authoring.Actions
{
    [ReactionAuthoring]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ReactionAuthoring))]
    public class ActionTickDistributionAuthoring : MonoBehaviour
    {
        public AnimationCurve Curve = AnimationCurve.Linear(0, 0, 1, 1);
        public Target From = Target.Source;
        public StatSchemaObject TicPerSecond;
        public StatSchemaObject TicDuration;
        public ConditionEventObject OnEnd;
        public Target To = Target.Target;
        public IntrinsicSchemaObject TickStore;
        public ConditionEventObject OnTic;

        private class Baker : Baker<ActionTickDistributionAuthoring>
        {
            public override void Bake(ActionTickDistributionAuthoring authoring)
            {
                if (authoring.Curve == null || authoring.TicDuration == null || authoring.TicPerSecond == null) return;

                var blob = BlobCurve.Create(authoring.Curve);
                AddBlobAsset(ref blob, out _);

                var entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new ActionTickDistribution
                {
                    Curve = blob,
                    From = authoring.From,
                    TicPerSecond = authoring.TicPerSecond,
                    TicDuration = authoring.TicDuration,
                    OnEnd = authoring.OnEnd != null ? authoring.OnEnd.Key : ConditionKey.Null,
                    To = authoring.To,
                    TickStore = authoring.TickStore,
                    OnTic = authoring.OnTic != null ? authoring.OnTic.Key : ConditionKey.Null
                });

                AddComponent<ActionTickDistributionState>(entity);
            }
        }
    }
}