using BovineLabs.Core.Iterators;
using BovineLabs.Core.ObjectManagement;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Testing;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Essence.Data;
using NUnit.Framework;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Tests
{
    // Integration test for the real TimelineEssenceEventSystem (not just the pure EssenceDeliveryGate): it drives the
    // ECS query + enableable wiring that pure-logic tests can't see — which is exactly where the missing-[WithPresent]
    // bug hid. Frame transitions mimic the core ClipActivePreviousSystem (copies ClipActive -> ClipActivePrevious each frame).
    public class TimelineEssenceEventSystemTests : TimelineEssenceTestFixture
    {
        private static readonly ConditionKey Key = new() { Value = new BLId(100) };

        private (Entity clip, Entity target) MakeClip(int value = 5)
        {
            var target = this.Manager.CreateEntity();
            var clip = this.Manager.CreateEntity(
                typeof(TrackBinding), typeof(TimelineEssenceEventData),
                typeof(ClipActive), typeof(ClipActivePrevious), typeof(TimelineEssenceDeliveryPending));

            this.Manager.SetComponentData(clip, new TrackBinding { Value = target });
            this.Manager.SetComponentData(clip, new TimelineEssenceEventData
            {
                Route = new EntityLinkRef { ReadRootFrom = Target.Self, LinkKey = 0 }, Event = Key, Value = value,
            });
            this.Manager.SetComponentEnabled<ClipActive>(clip, true);
            this.Manager.SetComponentEnabled<ClipActivePrevious>(clip, false); // rising edge this frame
            this.Manager.SetComponentEnabled<TimelineEssenceDeliveryPending>(clip, false); // baked-disabled latch
            return (clip, target);
        }

        private void Tick(SystemHandle sys, Entity clip)
        {
            sys.Update(this.WorldUnmanaged);
            this.Manager.CompleteAllTrackedJobs();
            // Mimic ClipActivePreviousSystem (OrderLast): ClipActivePrevious <- ClipActive after the gather ran.
            this.Manager.SetComponentEnabled<ClipActivePrevious>(clip, this.Manager.IsComponentEnabled<ClipActive>(clip));
        }

        private bool Delivered(Entity target, out int value)
        {
            if (this.Manager.GetBuffer<ConditionEvent>(target).AsMap().TryGetValue(Key, out var payload))
            {
                value = payload.Read<int>();
                return true;
            }

            value = 0;
            return false;
        }

        [Test]
        public void RetriesUntilWriterResolves_ThenFiresExactlyOnce()
        {
            var sys = this.World.CreateSystem<TimelineEssenceEventSystem>();
            var (clip, target) = this.MakeClip();

            // Frame 1: edge fires but the target has no ConditionEventWriter yet -> must stay armed (retry), not drop.
            this.Tick(sys, clip);
            Assert.IsTrue(this.Manager.IsComponentEnabled<TimelineEssenceDeliveryPending>(clip), "should retry while the writer is missing");

            // Writer streams in late.
            this.GiveTargetAWriter(target);

            // Frame 2: past the edge, still active + pending -> must fire once and clear the latch.
            this.Tick(sys, clip);
            Assert.IsFalse(this.Manager.IsComponentEnabled<TimelineEssenceDeliveryPending>(clip), "should clear after delivery");
            Assert.IsTrue(this.Delivered(target, out var v2), "should have delivered once the writer existed");
            Assert.AreEqual(5, v2);

            // Frame 3: not pending, no new edge -> skip (no double fire).
            this.Tick(sys, clip);
            var map = this.Manager.GetBuffer<ConditionEvent>(target).AsMap();
            map.TryGetValue(Key, out var v3);
            Assert.AreEqual(5, v3, "must not double-fire");
            Assert.AreEqual(1, map.Count);
        }

        [Test]
        public void FiresOnceOnEdge_WhenAlreadyResolved()
        {
            var sys = this.World.CreateSystem<TimelineEssenceEventSystem>();
            var (clip, target) = this.MakeClip();
            this.GiveTargetAWriter(target);

            this.Tick(sys, clip);
            Assert.IsFalse(this.Manager.IsComponentEnabled<TimelineEssenceDeliveryPending>(clip));
            Assert.IsTrue(this.Delivered(target, out var v), "edge frame should deliver immediately when resolvable");
            Assert.AreEqual(5, v);
        }

        [Test]
        public void DiagnoseClearsLatch_WhenClipEndsStillOwing()
        {
            var sys = this.World.CreateSystem<TimelineEssenceEventSystem>();
            var (clip, target) = this.MakeClip();

            // Active but never resolvable -> stays armed.
            this.Tick(sys, clip);
            Assert.IsTrue(this.Manager.IsComponentEnabled<TimelineEssenceDeliveryPending>(clip));

            // Clip ends while still owing -> DiagnoseMissedJob clears the latch so the next activation re-arms cleanly.
            this.Manager.SetComponentEnabled<ClipActive>(clip, false);
            this.Tick(sys, clip);
            Assert.IsFalse(this.Manager.IsComponentEnabled<TimelineEssenceDeliveryPending>(clip), "ended clip should not stay latched");
            Assert.IsFalse(this.Manager.HasBuffer<ConditionEvent>(target), "nothing should have been delivered to an unresolved target");
        }
    }
}
