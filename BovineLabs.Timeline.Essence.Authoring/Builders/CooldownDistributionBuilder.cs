namespace BovineLabs.Timeline.Essence.Authoring.Builders
{
    using BovineLabs.Core.Collections;
    using BovineLabs.Core.EntityCommands;
    using BovineLabs.Timeline.Essence.Authoring.Bakers;
    using BovineLabs.Timeline.Essence.Data.TickDistribution;
    using Unity.Entities;

    public struct CooldownDistributionBuilder
    {
        private TickDistributionAuthoring authoring;

        public CooldownDistributionBuilder(TickDistributionAuthoring authoring)
        {
            this.authoring = authoring;
        }

        public void ApplyTo<T>(ref T builder) where T : struct, IEntityCommands
        {
            if (this.authoring.Curve == null || this.authoring.TotalTicksStat == null)
            {
                return;
            }

            var blob = BlobCurve.Create(this.authoring.Curve);
            builder.AddBlobAsset(ref blob, out _);

            builder.AddComponent(new TickDistributionCurve
            {
                Value = blob,
                TotalTicksStat = this.authoring.TotalTicksStat,
                StatTarget = this.authoring.StatTarget,
                Intrinsic = this.authoring.Intrinsic,
                IntrinsicTarget = this.authoring.IntrinsicTarget,
                Event = this.authoring.Event,
            });
            
            builder.AddComponent<TickDistributionState>();
        }
    }
}