using BovineLabs.Essence.Authoring;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.Essence.Data;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    public sealed class TimelineEssenceIntrinsicClip : DOTSClip, ITimelineClipAsset
    {
        public Target routeTo = Target.Self;
        public IntrinsicSchemaObject intrinsic;
        public int amount = 1;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            context.Baker.AddComponent(clipEntity, new TimelineEssenceIntrinsicData
            {
                RouteTo = routeTo,
                Intrinsic = intrinsic ? intrinsic.Key : default(IntrinsicKey),
                Amount = amount
            });

            base.Bake(clipEntity, context);
        }
    }
}
