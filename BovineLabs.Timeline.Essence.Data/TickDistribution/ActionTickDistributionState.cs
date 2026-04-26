using Unity.Entities;

namespace BovineLabs.Essence.Data.Actions
{
    public struct ActionTickDistributionState : IComponentData
    {
        public float ElapsedTime;
        public int AppliedTicks;
        public bool EndFired;
    }
}