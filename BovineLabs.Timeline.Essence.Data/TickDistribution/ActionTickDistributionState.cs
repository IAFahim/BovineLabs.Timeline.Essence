// BovineLabs.Essence.Data/Actions/ActionTickDistributionState.cs
namespace BovineLabs.Essence.Data.Actions
{
    using Unity.Entities;

    public struct ActionTickDistributionState : IComponentData
    {
        public float ElapsedTime;
        public int AppliedTicks;
        public bool EndFired;
    }
}