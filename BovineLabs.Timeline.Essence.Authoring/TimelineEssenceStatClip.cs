using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Essence.Authoring;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Essence.Data;
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

        [Tooltip("What to do when routeLink is set but the link cannot be resolved: FallbackToTarget fires at the " +
                 "unlinked target (legacy, can misdirect effects), Retry waits for the link, Drop consumes the clip without firing.")]
        public LinkMissBehavior linkMissBehavior = LinkMissBehavior.FallbackToTarget;

        [Tooltip("Which entity the modifier lands on: the bound entity (Self) or a Targets slot. The modifier " +
                 "applies to the entity resolved when the clip activates; binding/link changes mid-clip are ignored until the clip deactivates.")]
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

            if (stat.Key == 0)
            {
                Debug.LogError(
                    $"{nameof(TimelineEssenceStatClip)} '{name}': stat schema '{stat.name}' has key 0 — asset not imported/registered; re-import it.",
                    this);
                return;
            }

            if (modifyType is StatAuthoringType.Added or StatAuthoringType.Subtracted && (int)value == 0 && value != 0)
            {
                Debug.LogError(
                    $"{nameof(TimelineEssenceStatClip)} '{name}': Added/Subtracted uses whole x100 fixed-point units; {value} truncates to 0 — did you mean {value * 100:0}?",
                    this);
                return;
            }

            if (value == 0)
            {
                Debug.LogWarning(
                    $"{nameof(TimelineEssenceStatClip)} '{name}': value is 0 — the modifier is a no-op.",
                    this);
            }

            var builder = new EssenceStatBuilder
            {
                Route = EntityLinkAuthoringUtility.BakeRef(context.Baker, routeLink, routeTo),
                Stat = stat.Key,
                ModifyType = StatAuthoringUtil.GetModifier(modifyType),
                Value = modifyType is StatAuthoringType.Subtracted or StatAuthoringType.Reduced
                    or StatAuthoringType.Less
                    ? -value
                    : value,
                LinkMiss = linkMissBehavior
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);

            base.Bake(clipEntity, context);
        }
    }
}