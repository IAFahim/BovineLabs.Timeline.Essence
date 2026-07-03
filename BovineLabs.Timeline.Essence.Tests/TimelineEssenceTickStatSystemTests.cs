using BovineLabs.Core.Iterators;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Testing;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Essence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Tests
{
    // Integration tests for the two sibling systems that shared the silent-miss root cause but weren't in the
    // original fix scope. Drives the real systems so the late-resolution retry behaviour is locked, not just reasoned.
    public class TimelineEssenceTickStatSystemTests : ECSTestsFixture
    {
        private static readonly ConditionKey EventKey = new() { Value = 100 };

        private static BlobAssetReference<DistributionCurveBlob> FullCurve()
        {
            using var b = new BlobBuilder(Allocator.Temp);
            ref var root = ref b.ConstructRoot<DistributionCurveBlob>();
            var cdf = b.Allocate(ref root.Cdf, 2);
            cdf[0] = 0f;
            cdf[1] = 1f; // Evaluate(1) == 1 -> fires the whole TickCount
            return b.CreateBlobAssetReference<DistributionCurveBlob>(Allocator.Persistent);
        }

        // --- Tick: a missed delivery must not consume the ticks (TickMath used to commit Fired before the writer resolved) ---

        [Test]
        public void Tick_MissedDelivery_RollsBackAndDeliversEveryTickOnce()
        {
            // Type-based creation: TimelineEssenceTickSystem embeds IntrinsicWriter facet fields that fail the C#
            // `unmanaged` generic constraint (still Burst-fine), so the generic CreateSystem<T> overload won't bind.
            var sys = this.World.CreateSystem(typeof(TimelineEssenceTickSystem));
            // RequireForUpdate<EssenceConfig>; an Event-mode tick never dereferences the blob, so a default singleton is enough.
            this.Manager.AddComponentData(this.Manager.CreateEntity(), new EssenceConfig());
            // The IntrinsicWriter facet (created unconditionally even for Event-mode ticks) fetches this singleton.
            this.World.CreateSystem(typeof(BovineLabs.Essence.ConditionIntrinsicWriteSystem));

            var curve = FullCurve();
            var target = this.Manager.CreateEntity(); // no ConditionEventWriter yet
            var clip = this.Manager.CreateEntity(
                typeof(TrackBinding), typeof(TimelineEssenceTickData), typeof(LocalTime),
                typeof(ClipActive), typeof(ClipActivePrevious), typeof(TimelineEssenceTickState));
            this.Manager.SetComponentData(clip, new TrackBinding { Value = target });
            this.Manager.SetComponentData(clip, new TimelineEssenceTickData
            {
                Route = new EntityLinkRef { ReadRootFrom = Target.Self }, Mode = EssenceTickMode.Event, Event = EventKey,
                ValuePerTick = 1, TickCount = 10, Duration = 0f, Curve = curve, // Duration 0 -> t==1 -> all 10 ticks due now
            });
            this.Manager.SetComponentEnabled<ClipActive>(clip, true);
            this.Manager.SetComponentEnabled<ClipActivePrevious>(clip, false); // just activated -> Fired resets to 0

            // Frame 1: 10 ticks are due but the target has no writer -> delivery misses and Fired MUST roll back to 0.
            sys.Update(this.WorldUnmanaged);
            this.Manager.CompleteAllTrackedJobs();
            Assert.AreEqual(0, this.Manager.GetComponentData<TimelineEssenceTickState>(clip).Fired,
                "a missed tick delivery must not be committed");
            Assert.IsFalse(this.Manager.HasBuffer<ConditionEvent>(target));
            this.Manager.SetComponentEnabled<ClipActivePrevious>(clip, true); // mimic core ClipActivePreviousSystem

            // Writer streams in late.
            this.Manager.AddBuffer<ConditionEvent>(target).Initialize();
            this.Manager.AddComponent<EventsDirty>(target);
            this.Manager.SetComponentEnabled<EventsDirty>(target, true);

            // Frame 2: writer present -> every tick that was rolled back is delivered now, none lost.
            sys.Update(this.WorldUnmanaged);
            this.Manager.CompleteAllTrackedJobs();
            Assert.AreEqual(10, this.Manager.GetComponentData<TimelineEssenceTickState>(clip).Fired);
            Assert.IsTrue(this.Manager.GetBuffer<ConditionEvent>(target).AsMap().TryGetValue(EventKey, out int v));
            Assert.AreEqual(10, v); // ValuePerTick(1) * 10 ticks

            curve.Dispose();
        }

        // --- Stat: a while-active modifier must retry until the target buffer resolves (was edge-only -> silent drop) ---

        private void StatTick(SystemHandle sys, ComponentSystemBase ecb, Entity clip)
        {
            sys.Update(this.WorldUnmanaged);
            ecb.Update(); // play back the cleanup ECB
            this.Manager.CompleteAllTrackedJobs();
            this.Manager.SetComponentEnabled<ClipActivePrevious>(clip, this.Manager.IsComponentEnabled<ClipActive>(clip));
        }

        private Entity MakeStatClip(Entity target)
        {
            var clip = this.Manager.CreateEntity(
                typeof(TrackBinding), typeof(TimelineEssenceStatData),
                typeof(ClipActive), typeof(ClipActivePrevious), typeof(TimelineEssenceStatState));
            this.Manager.SetComponentData(clip, new TrackBinding { Value = target });
            this.Manager.SetComponentData(clip, new TimelineEssenceStatData
            {
                Route = new EntityLinkRef { ReadRootFrom = Target.Self }, Stat = 1, ModifyType = StatModifyType.Added, Value = 5f,
            });
            this.Manager.SetComponentEnabled<ClipActive>(clip, true);
            this.Manager.SetComponentEnabled<ClipActivePrevious>(clip, false);
            return clip;
        }

        private Entity MakeStatTarget()
        {
            var target = this.Manager.CreateEntity();
            this.Manager.AddBuffer<StatModifiers>(target);
            this.Manager.AddComponentData(target, new StatChanged());
            return target;
        }

        [Test]
        public void Stat_RetriesUntilBufferResolves_ThenAppliesExactlyOnce()
        {
            var ecb = this.World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
            var sys = this.World.CreateSystem<TimelineEssenceStatSystem>();

            var target = this.Manager.CreateEntity(); // no StatModifiers buffer yet
            var clip = this.MakeStatClip(target);

            // Frame 1: buffer missing -> pre-check fails -> must NOT latch AppliedTarget (no phantom apply).
            this.StatTick(sys, ecb, clip);
            Assert.AreEqual(Entity.Null, this.Manager.GetComponentData<TimelineEssenceStatState>(clip).AppliedTarget,
                "must not latch before the target buffer exists");

            // Buffer streams in.
            this.Manager.AddBuffer<StatModifiers>(target);
            this.Manager.AddComponentData(target, new StatChanged());

            // Frame 2: resolvable now -> applies exactly one modifier.
            this.StatTick(sys, ecb, clip);
            Assert.AreEqual(target, this.Manager.GetComponentData<TimelineEssenceStatState>(clip).AppliedTarget);
            Assert.AreEqual(1, this.Manager.GetBuffer<StatModifiers>(target).Length, "should apply once resolvable");

            // Frame 3 (still active): must not re-add.
            this.StatTick(sys, ecb, clip);
            Assert.AreEqual(1, this.Manager.GetBuffer<StatModifiers>(target).Length, "must not double-apply while active");
        }

        [Test]
        public void Stat_ClearsOnDeactivate_AndReappliesOnReactivate()
        {
            var ecb = this.World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
            var sys = this.World.CreateSystem<TimelineEssenceStatSystem>();

            var target = this.MakeStatTarget();
            var clip = this.MakeStatClip(target);

            this.StatTick(sys, ecb, clip);
            Assert.AreEqual(1, this.Manager.GetBuffer<StatModifiers>(target).Length, "applied on activation");

            // Deactivate: removal runs and AppliedTarget clears so a re-activation can re-apply.
            this.Manager.SetComponentEnabled<ClipActive>(clip, false);
            this.StatTick(sys, ecb, clip);
            Assert.AreEqual(0, this.Manager.GetBuffer<StatModifiers>(target).Length, "removed on deactivation");
            Assert.AreEqual(Entity.Null, this.Manager.GetComponentData<TimelineEssenceStatState>(clip).AppliedTarget);

            // Re-activate: applies again.
            this.Manager.SetComponentEnabled<ClipActive>(clip, true);
            this.StatTick(sys, ecb, clip);
            Assert.AreEqual(1, this.Manager.GetBuffer<StatModifiers>(target).Length, "re-applied on re-activation");
        }
    }
}
