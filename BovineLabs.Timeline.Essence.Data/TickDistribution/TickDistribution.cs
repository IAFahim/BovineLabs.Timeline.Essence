namespace BovineLabs.Timeline.Essence.Data.TickDistribution
{
    using Unity.Entities;
    
    public struct TickDistributionState : IComponentData
    {
        public int AppliedTicks;
        public int TicksThisFrame;
    }
}