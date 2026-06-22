using System.Runtime.CompilerServices;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Core;

namespace BovineLabs.Timeline.Essence.Data
{
    public static class StatModifierMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryBuildStatModifier(StatKey stat, StatModifyType modifyType, float value, out StatModifier modifier)
        {
            if (stat.Value == 0)
            {
                modifier = default;
                return false;
            }

            modifier = new StatModifier { Type = stat, ModifyType = modifyType };

            if (modifyType == StatModifyType.Added)
                modifier.Value = (int)value;
            else
                modifier.ValueFloat = value;

            return true;
        }
    }
}
