using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Data.TickDistribution
{
    public struct TickDistributionState : IComponentData
    {
        public int AppliedTicks;
        public int TicksThisFrame;
    }
}