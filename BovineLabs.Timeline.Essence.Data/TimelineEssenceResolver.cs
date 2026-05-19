using System.Runtime.CompilerServices;
using BovineLabs.Reaction.Data.Core;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Data
{
    public static class TimelineEssenceResolver
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryResolveTarget(
            Target target,
            Entity binding,
            in ComponentLookup<Targets> targets,
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
    }
}