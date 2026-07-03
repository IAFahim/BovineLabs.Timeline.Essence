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
        internal static bool TryResolveTarget(
            Target target,
            Entity binding,
            in UnsafeComponentLookup<Targets> targets,
            out Entity resolved)
        {
            if (target is Target.Self or Target.None)
            {
                resolved = binding;
                return true;
            }

            if (targets.TryGetComponent(binding, out var t))
            {
                resolved = t.Get(target, binding);
                return resolved != Entity.Null;
            }

            resolved = Entity.Null;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryResolveLinkedTarget(
            in EntityLinkRef route,
            Entity self,
            in UnsafeComponentLookup<Targets> targetsLookup,
            in UnsafeComponentLookup<EntityLinkSource> sources,
            in UnsafeBufferLookup<EntityLinkEntry> links,
            out Entity resolved)
        {
            resolved = Entity.Null;

            if (!TryResolveTarget(route.ReadRootFrom, self, targetsLookup, out var target))
                return false;

            if (route.LinkKey == 0)
            {
                resolved = target;
                return true;
            }

            if (EntityLinkResolver.TryResolve(target, route.LinkKey, sources, links, out var linked) && linked != Entity.Null)
            {
                resolved = linked;
                return true;
            }

            resolved = target;
            return true;
        }
    }
}