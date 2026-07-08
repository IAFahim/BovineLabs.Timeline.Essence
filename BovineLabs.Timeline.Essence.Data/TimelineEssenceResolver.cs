using System.Runtime.CompilerServices;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Data
{
    /// <summary>Outcome of a link-aware target resolution, distinguishing a clean resolve from the two link-miss policies.</summary>
    public enum ResolveResult : byte
    {
        /// <summary>Target resolved (either directly, or via a successful link hop, or via FallbackToTarget on a miss).</summary>
        Resolved,

        /// <summary>Not resolvable yet — retry on a later frame (base target unresolved, or link missing under Retry policy).</summary>
        RetryLater,

        /// <summary>Link missing under Drop policy — the caller must consume its one-shot without firing.</summary>
        Dropped,
    }

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

        /// <summary>
        /// Link-aware target resolution honoring a per-clip <see cref="LinkMissBehavior"/>. When the route has a
        /// LinkKey that cannot be resolved, the policy decides between firing at the unlinked target
        /// (FallbackToTarget, legacy), retrying later, or dropping the delivery.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ResolveResult TryResolveLinkedTarget(
            in EntityLinkRef route,
            LinkMissBehavior policy,
            Entity self,
            in UnsafeComponentLookup<Targets> targetsLookup,
            in UnsafeComponentLookup<EntityLinkSource> sources,
            in UnsafeBufferLookup<EntityLinkEntry> links,
            out Entity resolved)
        {
            resolved = Entity.Null;

            if (!TryResolveTarget(route.ReadRootFrom, self, targetsLookup, out var target))
                return ResolveResult.RetryLater;

            if (route.LinkKey == 0)
            {
                resolved = target;
                return ResolveResult.Resolved;
            }

            if (EntityLinkResolver.TryResolve(target, route.LinkKey, sources, links, out var linked) && linked != Entity.Null)
            {
                resolved = linked;
                return ResolveResult.Resolved;
            }

            // LinkKey set but the hop failed: apply the per-clip miss policy.
            switch (policy)
            {
                case LinkMissBehavior.Drop:
                    resolved = Entity.Null;
                    return ResolveResult.Dropped;
                case LinkMissBehavior.Retry:
                    resolved = Entity.Null;
                    return ResolveResult.RetryLater;
                default: // FallbackToTarget (legacy)
                    resolved = target;
                    return ResolveResult.Resolved;
            }
        }

        /// <summary>Backwards-compatible bool overload: resolves with <see cref="LinkMissBehavior.FallbackToTarget"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryResolveLinkedTarget(
            in EntityLinkRef route,
            Entity self,
            in UnsafeComponentLookup<Targets> targetsLookup,
            in UnsafeComponentLookup<EntityLinkSource> sources,
            in UnsafeBufferLookup<EntityLinkEntry> links,
            out Entity resolved)
        {
            return TryResolveLinkedTarget(route, LinkMissBehavior.FallbackToTarget, self, targetsLookup, sources, links,
                out resolved) == ResolveResult.Resolved;
        }
    }
}