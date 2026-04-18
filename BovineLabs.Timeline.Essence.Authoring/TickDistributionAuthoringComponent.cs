namespace BovineLabs.Timeline.Essence.Authoring
{
    using BovineLabs.Core.Authoring.EntityCommands;
    using BovineLabs.Reaction.Authoring.Core;
    using BovineLabs.Timeline.Essence.Authoring.Bakers;
    using BovineLabs.Timeline.Essence.Authoring.Builders;
    using Unity.Entities;
    using UnityEngine;

    [RequireComponent(typeof(ReactionAuthoring))]
    [DisallowMultipleComponent]
    public class TickDistributionAuthoringComponent : MonoBehaviour
    {
        [SerializeField] 
        private TickDistributionAuthoring distribution = new();

        private void OnDrawGizmosSelected()
        {
            var auth = this.distribution;
            if (auth.Curve == null || auth.GizmoSimulatedTotalTicks <= 0 || auth.GizmoSimulatedDuration <= 0)
            {
                return;
            }

            var pos = this.transform.position;
            Gizmos.color = Color.white;
            Gizmos.DrawLine(pos, pos + Vector3.right * auth.GizmoSimulatedDuration);

            Gizmos.color = Color.red;
            var applied = 0;
            var steps = 200;
            
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var cdf = Mathf.Clamp01(auth.Curve.Evaluate(t));
                var expected = Mathf.FloorToInt(cdf * auth.GizmoSimulatedTotalTicks);
                expected = Mathf.Min(expected, auth.GizmoSimulatedTotalTicks);

                if (expected > applied)
                {
                    var x = t * auth.GizmoSimulatedDuration;
                    var diff = expected - applied;
                    Gizmos.DrawLine(pos + new Vector3(x, -0.5f, 0), pos + new Vector3(x, 0.5f + (diff - 1) * 0.2f, 0));
                    applied = expected;
                }
            }
        }

        private sealed class Baker : Baker<TickDistributionAuthoringComponent>
        {
            public override void Bake(TickDistributionAuthoringComponent authoring)
            {
                var builder = new CooldownDistributionBuilder(authoring.distribution);
                var commands = new BakerCommands(this, this.GetEntity(TransformUsageFlags.None));
                builder.ApplyTo(ref commands);
            }
        }
    }
}