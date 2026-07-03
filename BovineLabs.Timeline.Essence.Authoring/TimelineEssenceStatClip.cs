using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Essence.Authoring;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Essence.Data.Builders;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    public sealed class TimelineEssenceStatClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Optional link key; re-routes from the resolved target to its linked entity.")]
        public EntityLinkSchema routeLink;

        [Tooltip("Which entity the modifier lands on: the bound entity (Self) or a Targets slot.")]
        public Target routeTo = Target.Self;

        [Tooltip("The stat to modify while the clip is active.")]
        public StatSchemaObject stat;

        [Tooltip("How value is applied (Added/Subtracted/Increased/Reduced/More/Less).")]
        public StatAuthoringType modifyType = StatAuthoringType.Added;

        [Tooltip(
            "Modifier amount. For Added/Subtracted the stat reads value/100 (x100 fixed-point): typing 5 yields +0.05 unless you scale by 100.")]
        public float value;

        public override double duration => 1;

        public ClipCaps clipCaps => ClipCaps.Looping;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            if (stat == null)
            {
                Debug.LogError(
                    $"{nameof(TimelineEssenceStatClip)} '{name}' has no Stat assigned; the clip will not modify any stat.",
                    this);
                return;
            }

            var builder = new EssenceStatBuilder
            {
                Route = EntityLinkAuthoringUtility.BakeRef(context.Baker, routeLink, routeTo),
                Stat = stat.Key,
                ModifyType = StatAuthoringUtil.GetModifier(modifyType),
                Value = modifyType is StatAuthoringType.Subtracted or StatAuthoringType.Reduced
                    or StatAuthoringType.Less
                    ? -value
                    : value
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);

            base.Bake(clipEntity, context);
        }
    }
}