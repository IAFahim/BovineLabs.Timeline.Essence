using System.Runtime.CompilerServices;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Data
{
    public static class TimelineEssenceResolver
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryResolveTarget(
            Target targetMode,
            ushort linkKey,
            Entity binding,
            in ComponentLookup<Targets> targets,
            in ComponentLookup<TargetsCustom> customs,
            in UnsafeComponentLookup<EntityLinkSource> sources,
            in UnsafeBufferLookup<EntityLinkEntry> links,
            out Entity resolved)
        {
            resolved = Entity.Null;

            Entity targetEntity = Entity.Null;
            if (targetMode is Target.Self or Target.None)
            {
                targetEntity = binding;
            }
            else if (targets.TryGetComponent(binding, out var t))
            {
                targetEntity = t.Get(targetMode, binding, customs);
            }

            if (targetEntity == Entity.Null)
            {
                return false;
            }

            if (linkKey == 0)
            {
                resolved = targetEntity;
                return true;
            }

            if (EntityLinkResolver.TryResolve(targetEntity, linkKey, sources, links, out var linked))
            {
                resolved = linked;
                return true;
            }

            resolved = targetEntity;
            return true;
        }
    }
}