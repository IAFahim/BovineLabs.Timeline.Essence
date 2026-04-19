namespace BovineLabs.Timeline.Essence.Authoring
{
    using BovineLabs.Essence.Authoring;
    using BovineLabs.Reaction.Authoring.Conditions;
    using BovineLabs.Reaction.Data.Core;
    using BovineLabs.Timeline.Authoring;
    using BovineLabs.Timeline.Essence.Data.TickDistribution;
    using Unity.Collections;
    using Unity.Entities;
    using UnityEngine;
    using UnityEngine.Timeline;

    public class TickDistributionClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("X: 0..1 (Time), Y: 0..1 (Tick CDF)")]
        public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);

        public StatSchemaObject TotalTicksStat;
        public Target StatTarget = Target.Target;

        [Header("Outputs")]
        public IntrinsicSchemaObject Intrinsic;
        public Target IntrinsicTarget = Target.Target;
        public ConditionEventObject Event;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            if (curve == null || TotalTicksStat == null)
            {
                return;
            }

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<DistributionCurveBlob>();
            
            const int samples = 100;
            var cdfArray = builder.Allocate(ref root.Cdf, samples);
            for (var i = 0; i < samples; i++) cdfArray[i] = curve.Evaluate(i / (float)(samples - 1));

            var blob = builder.CreateBlobAssetReference<DistributionCurveBlob>(Allocator.Persistent);
            builder.Dispose();

            context.Baker.AddBlobAsset(ref blob, out _);

            context.Baker.AddComponent(clipEntity, new TickDistributionClipData
            {
                Cdf = blob,
                TotalTicksStat = TotalTicksStat,
                StatTarget = StatTarget,
                Intrinsic = Intrinsic,
                IntrinsicTarget = IntrinsicTarget,
                Event = Event,
            });

            context.Baker.AddComponent<TickDistributionState>(clipEntity);
            base.Bake(clipEntity, context);
        }
    }
}