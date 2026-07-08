using BovineLabs.Core.Iterators;
using BovineLabs.Nerve.ObjectManagement;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Essence.Data;
using NUnit.Framework;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Tests
{
    // Exercises the per-clip LinkMissBehavior policy on an Event clip whose route carries a LinkKey. Retry holds the
    // one-shot latch (retries), Drop consumes it without firing, FallbackToTarget fires at the unlinked base target,
    // and a Retry that later becomes resolvable fires exactly once at the LINKED entity.
    public class TimelineEssenceLinkMissTests : TimelineEssenceTestFixture
    {
        private static readonly ConditionKey Key = new() { Value = new BovineLabs.Core.BLId(100) };
        private const ushort LinkKey = 7;

        private (Entity clip, Entity binding) MakeLinkedEventClip(LinkMissBehavior policy, int value = 5)
        {
            var binding = this.Manager.CreateEntity();
            var clip = this.Manager.CreateEntity(
                typeof(TrackBinding), typeof(TimelineEssenceEventData),
                typeof(ClipActive), typeof(ClipActivePrevious), typeof(TimelineEssenceDeliveryPending));

            this.Manager.SetComponentData(clip, new TrackBinding { Value = binding });
            this.Manager.SetComponentData(clip, new TimelineEssenceEventData
            {
                Route = new EntityLinkRef { ReadRootFrom = Target.Self, LinkKey = LinkKey }, Event = Key, Value = value,
                LinkMiss = policy,
            });
            this.Manager.SetComponentEnabled<ClipActive>(clip, true);
            this.Manager.SetComponentEnabled<ClipActivePrevious>(clip, false); // rising edge
            this.Manager.SetComponentEnabled<TimelineEssenceDeliveryPending>(clip, false);
            return (clip, binding);
        }

        private void Tick(SystemHandle sys, Entity clip)
        {
            sys.Update(this.WorldUnmanaged);
            this.Manager.CompleteAllTrackedJobs();
            this.Manager.SetComponentEnabled<ClipActivePrevious>(clip, this.Manager.IsComponentEnabled<ClipActive>(clip));
        }

        [Test]
        public void Retry_UnresolvableLink_StaysLatched_NothingDelivered()
        {
            var sys = this.World.CreateSystem<TimelineEssenceEventSystem>();
            var (clip, binding) = this.MakeLinkedEventClip(LinkMissBehavior.Retry);
            this.GiveTargetAWriter(binding); // the link itself, not the writer, is the missing piece

            this.Tick(sys, clip);

            Assert.IsTrue(this.Manager.IsComponentEnabled<TimelineEssenceDeliveryPending>(clip),
                "Retry must keep the latch armed while the link is missing");
            Assert.AreEqual(0, this.Manager.GetBuffer<ConditionEvent>(binding).AsMap().Count,
                "nothing may be delivered under Retry with an unresolved link");
        }

        [Test]
        public void Drop_UnresolvableLink_ConsumesLatch_NothingDelivered()
        {
            var sys = this.World.CreateSystem<TimelineEssenceEventSystem>();
            var (clip, binding) = this.MakeLinkedEventClip(LinkMissBehavior.Drop);
            this.GiveTargetAWriter(binding);

            this.Tick(sys, clip);

            Assert.IsFalse(this.Manager.IsComponentEnabled<TimelineEssenceDeliveryPending>(clip),
                "Drop must consume the one-shot latch");
            Assert.AreEqual(0, this.Manager.GetBuffer<ConditionEvent>(binding).AsMap().Count,
                "Drop must not deliver anywhere");

            // Still active on a later frame: the latch stays clear (no delayed fire).
            this.Tick(sys, clip);
            Assert.IsFalse(this.Manager.IsComponentEnabled<TimelineEssenceDeliveryPending>(clip));
            Assert.AreEqual(0, this.Manager.GetBuffer<ConditionEvent>(binding).AsMap().Count);
        }

        [Test]
        public void FallbackToTarget_UnresolvableLink_DeliversAtBaseTarget()
        {
            var sys = this.World.CreateSystem<TimelineEssenceEventSystem>();
            var (clip, binding) = this.MakeLinkedEventClip(LinkMissBehavior.FallbackToTarget);
            this.GiveTargetAWriter(binding);

            this.Tick(sys, clip);

            Assert.IsFalse(this.Manager.IsComponentEnabled<TimelineEssenceDeliveryPending>(clip),
                "Fallback delivers, so the latch clears");
            Assert.IsTrue(this.Manager.GetBuffer<ConditionEvent>(binding).AsMap().TryGetValue(Key, out var payload),
                "Fallback (legacy) fires at the unlinked base target");
            Assert.AreEqual(5, payload.Read<int>());
        }

        [Test]
        public void Retry_LinkBecomesResolvable_DeliversOnceAtLinkedEntity()
        {
            var sys = this.World.CreateSystem<TimelineEssenceEventSystem>();
            var (clip, binding) = this.MakeLinkedEventClip(LinkMissBehavior.Retry);

            var linked = this.Manager.CreateEntity();
            this.GiveTargetAWriter(linked); // only the LINKED entity carries a writer, never the base target

            // Frame 1: link entry not present yet -> Retry holds the latch, nothing delivered.
            this.Tick(sys, clip);
            Assert.IsTrue(this.Manager.IsComponentEnabled<TimelineEssenceDeliveryPending>(clip));

            // Make the link resolvable: the base target (== binding, ReadRootFrom.Self) carries the link map.
            var links = this.Manager.AddBuffer<EntityLinkEntry>(binding);
            links.Add(new EntityLinkEntry { Key = LinkKey, Target = linked });

            // Frame 2: resolves to the linked entity and fires exactly once there.
            this.Tick(sys, clip);
            Assert.IsFalse(this.Manager.IsComponentEnabled<TimelineEssenceDeliveryPending>(clip), "latch clears after delivery");
            Assert.IsTrue(this.Manager.GetBuffer<ConditionEvent>(linked).AsMap().TryGetValue(Key, out var payload),
                "must deliver at the linked entity");
            Assert.AreEqual(5, payload.Read<int>());
            Assert.IsFalse(this.Manager.HasBuffer<ConditionEvent>(binding), "must NOT deliver at the base target");

            // Frame 3: no new edge, latch clear -> no double fire.
            this.Tick(sys, clip);
            var map = this.Manager.GetBuffer<ConditionEvent>(linked).AsMap();
            Assert.AreEqual(1, map.Count);
            map.TryGetValue(Key, out var again);
            Assert.AreEqual(5, again.Read<int>());
        }
    }
}
