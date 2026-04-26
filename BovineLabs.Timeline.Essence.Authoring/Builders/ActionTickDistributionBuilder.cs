using BovineLabs.Core.EntityCommands;
using BovineLabs.Essence.Data.Actions;

namespace BovineLabs.Essence.Data.Builders
{
    public struct ActionTickDistributionBuilder
    {
        public ActionTickDistribution Config;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(Config);
            builder.AddComponent<ActionTickDistributionState>();
        }
    }
}