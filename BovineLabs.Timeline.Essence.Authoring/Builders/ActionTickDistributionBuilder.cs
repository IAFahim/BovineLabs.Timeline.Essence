// BovineLabs.Essence.Data/Builders/ActionTickDistributionBuilder.cs
namespace BovineLabs.Essence.Data.Builders
{
    using System;
    using BovineLabs.Core.EntityCommands;
    using BovineLabs.Essence.Data.Actions;

    public struct ActionTickDistributionBuilder
    {
        public ActionTickDistribution Config;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(this.Config);
            builder.AddComponent<ActionTickDistributionState>();
        }
    }
}