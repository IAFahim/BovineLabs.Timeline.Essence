using BovineLabs.Core.EntityCommands;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Core;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Data.Builders
{
    public struct EssenceStatBuilder
    {
        public Target RouteTo;
        public ushort RouteLinkKey;
        public StatKey Stat;
        public StatModifyType ModifyType;
        public float Value;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new TimelineEssenceStatData
            {
                RouteTo = RouteTo,
                RouteLinkKey = RouteLinkKey,
                Stat = Stat,
                ModifyType = ModifyType,
                Value = Value
            });

            builder.AddComponent(new TimelineEssenceStatState
            {
                AppliedTarget = Entity.Null
            });
        }
    }
}