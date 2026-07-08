using BovineLabs.Core.EntityCommands;
using BovineLabs.Essence.Data;
using BovineLabs.Timeline.EntityLinks.Data;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Data.Builders
{
    public struct EssenceStatBuilder
    {
        public EntityLinkRef Route;
        public StatKey Stat;
        public StatModifyType ModifyType;
        public float Value;
        public LinkMissBehavior LinkMiss;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new TimelineEssenceStatData
            {
                Route = Route,
                Stat = Stat,
                ModifyType = ModifyType,
                Value = Value,
                LinkMiss = LinkMiss
            });

            builder.AddComponent(new TimelineEssenceStatState
            {
                AppliedTarget = Entity.Null
            });
        }
    }
}