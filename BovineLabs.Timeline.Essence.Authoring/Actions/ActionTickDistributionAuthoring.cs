namespace BovineLabs.Essence.Authoring.Actions
{
    using System;
    using BovineLabs.Core.Authoring.EntityCommands;
    using BovineLabs.Essence.Data.Actions;
    using BovineLabs.Reaction.Authoring;
    using BovineLabs.Reaction.Authoring.Conditions;
    using BovineLabs.Reaction.Authoring.Core;
    using BovineLabs.Reaction.Data.Conditions;
    using BovineLabs.Reaction.Data.Core;
    using Unity.Collections;
    using Unity.Entities;
    using UnityEngine;

    [ReactionAuthoring]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ReactionAuthoring))]
    public class ActionTickDistributionAuthoring : MonoBehaviour
    {
        public Data[] Distributions = Array.Empty<Data>();

        [Serializable]
        public class Data
        {[Tooltip("X: 0..1 (Time), Y: 0..1 (Tick CDF)")]
            public AnimationCurve Curve = AnimationCurve.Linear(0, 0, 1, 1);

            public StatSchemaObject TotalTicksStat;
            public Target StatTarget = Target.Target;

            [Header("Outputs")]
            public IntrinsicSchemaObject Intrinsic;
            public Target IntrinsicTarget = Target.Target;
            
            public ConditionEventObject Event;
        }

        private class Baker : Baker<ActionTickDistributionAuthoring>
        {
            public override void Bake(ActionTickDistributionAuthoring authoring)
            {
                if (authoring.Distributions.Length == 0) return;

                var entity = this.GetEntity(TransformUsageFlags.None);
                var distributions = this.AddBuffer<ActionTickDistribution>(entity);
                var states = this.AddBuffer<ActionTickDistributionState>(entity);

                foreach (var dist in authoring.Distributions)
                {
                    if (dist.Curve == null || dist.TotalTicksStat == null) continue;

                    var builder = new BlobBuilder(Allocator.Temp);
                    ref var root = ref builder.ConstructRoot<DistributionCurveBlob>();

                    const int samples = 100;
                    var cdfArray = builder.Allocate(ref root.Cdf, samples);
                    for (var i = 0; i < samples; i++)
                    {
                        cdfArray[i] = dist.Curve.Evaluate(i / (float)(samples - 1));
                    }

                    var blob = builder.CreateBlobAssetReference<DistributionCurveBlob>(Allocator.Persistent);
                    builder.Dispose();

                    this.AddBlobAsset(ref blob, out _);

                    distributions.Add(new ActionTickDistribution
                    {
                        Cdf = blob,
                        TotalTicksStat = dist.TotalTicksStat,
                        StatTarget = dist.StatTarget,
                        Intrinsic = dist.Intrinsic,
                        IntrinsicTarget = dist.IntrinsicTarget,
                        EventKey = dist.Event ? dist.Event.Key : ConditionKey.Null
                    });

                    states.Add(new ActionTickDistributionState { AppliedTicks = 0 });
                }
            }
        }
    }
}