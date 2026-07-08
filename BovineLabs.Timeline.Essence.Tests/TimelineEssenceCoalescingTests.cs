// Depends on the upstream ConditionEventWriter accumulation fix (mirrored in Library/PackageCache; permanent after
// the tertle-monorepo fork push) — if these tests start failing with a duplicate-write error, the PackageCache mirror
// was re-resolved before the fork was bumped. The fix makes same-frame int-payload writes to the same ConditionKey
// on one target ACCUMULATE (sum; entry removed when the sum is 0) instead of the second write being rejected.
using BovineLabs.Core.Iterators;
using BovineLabs.Core.ObjectManagement;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Essence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.TestTools;

namespace BovineLabs.Timeline.Essence.Tests
{
    // Two payload writers to the same ConditionKey + target in one frame must coalesce into a single summed map entry.
    public class TimelineEssenceCoalescingTests : TimelineEssenceTestFixture
    {
        private static readonly ConditionKey Key = new() { Value = new BLId(100) };

        private static BlobAssetReference<DistributionCurveBlob> FullCurve()
        {
            using var b = new BlobBuilder(Allocator.Temp);
            ref var root = ref b.ConstructRoot<DistributionCurveBlob>();
            var cdf = b.Allocate(ref root.Cdf, 2);
            cdf[0] = 0f;
            cdf[1] = 1f; // Evaluate(1) == 1 -> fires the whole TickCount
            return b.CreateBlobAssetReference<DistributionCurveBlob>(Allocator.Persistent);
        }

        private Entity MakeTickClip(Entity target, BlobAssetReference<DistributionCurveBlob> curve, int valuePerTick)
        {
            var clip = this.Manager.CreateEntity(
                typeof(TrackBinding), typeof(TimelineEssenceTickData), typeof(LocalTime),
                typeof(ClipActive), typeof(ClipActivePrevious), typeof(TimelineEssenceTickState));
            this.Manager.SetComponentData(clip, new TrackBinding { Value = target });
            this.Manager.SetComponentData(clip, new TimelineEssenceTickData
            {
                Route = new EntityLinkRef { ReadRootFrom = Target.Self }, Mode = EssenceTickMode.Event, Event = Key,
                ValuePerTick = valuePerTick, TickCount = 1, Duration = 0f, Curve = curve, // Duration 0 -> t==1 -> the 1 tick fires now
            });
            this.Manager.SetComponentEnabled<ClipActive>(clip, true);
            this.Manager.SetComponentEnabled<ClipActivePrevious>(clip, false); // just activated -> Fired resets to 0
            return clip;
        }

        private Entity MakeEventClip(Entity target, int value)
        {
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
            this.Manager.SetComponentEnabled<TimelineEssenceDeliveryPending>(clip, false);
            return clip;
        }

        // Requirements shared by the tick system's writer facets (never dereferenced for an Event-mode tick).
        private SystemHandle CreateTickSystem()
        {
            this.Manager.AddComponentData(this.Manager.CreateEntity(), new EssenceConfig());
            this.World.CreateSystem(typeof(BovineLabs.Essence.ConditionIntrinsicWriteSystem));
            return this.World.CreateSystem(typeof(TimelineEssenceTickSystem));
        }

        [Test]
        public void TwoTickClips_SameKeyAndTarget_SumIntoOneEntry_NoError()
        {
            var sys = this.CreateTickSystem();
            var curve = FullCurve();

            var target = this.Manager.CreateEntity();
            this.GiveTargetAWriter(target);

            var clipA = this.MakeTickClip(target, curve, 3);
            var clipB = this.MakeTickClip(target, curve, 4);

            sys.Update(this.WorldUnmanaged);
            this.Manager.CompleteAllTrackedJobs();

            var map = this.Manager.GetBuffer<ConditionEvent>(target).AsMap();
            Assert.AreEqual(1, map.Count, "same-frame same-key writes must coalesce into one entry");
            Assert.IsTrue(map.TryGetValue(Key, out var payload));
            Assert.AreEqual(7, payload.Read<int>(), "payload must be the sum of both clips (3 + 4)");

            // Both clips must have fully committed their tick regardless of the coalescing.
            Assert.AreEqual(1, this.Manager.GetComponentData<TimelineEssenceTickState>(clipA).Fired);
            Assert.AreEqual(1, this.Manager.GetComponentData<TimelineEssenceTickState>(clipB).Fired);

            LogAssert.NoUnexpectedReceived(); // int accumulation is silent — no duplicate-write error

            curve.Dispose();
        }

        [Test]
        public void EventClipPlusTickClip_SameKeyAndTarget_SumIntoOneEvent()
        {
            var tickSys = this.CreateTickSystem();
            var eventSys = this.World.CreateSystem<TimelineEssenceEventSystem>();
            var curve = FullCurve();

            var target = this.Manager.CreateEntity();
            this.GiveTargetAWriter(target);

            var tickClip = this.MakeTickClip(target, curve, 3); // writes 3
            var eventClip = this.MakeEventClip(target, 4);       // writes 4

            // Both systems write into the SAME ConditionEvent buffer within one frame -> the writer accumulates.
            tickSys.Update(this.WorldUnmanaged);
            this.Manager.CompleteAllTrackedJobs();
            eventSys.Update(this.WorldUnmanaged);
            this.Manager.CompleteAllTrackedJobs();

            var map = this.Manager.GetBuffer<ConditionEvent>(target).AsMap();
            Assert.AreEqual(1, map.Count, "an event + a tick to the same key/target must coalesce");
            Assert.IsTrue(map.TryGetValue(Key, out var payload));
            Assert.AreEqual(7, payload.Read<int>(), "payload must be the sum of the tick (3) and event (4)");

            Assert.AreEqual(1, this.Manager.GetComponentData<TimelineEssenceTickState>(tickClip).Fired);
            Assert.IsFalse(this.Manager.IsComponentEnabled<TimelineEssenceDeliveryPending>(eventClip),
                "event clip should have delivered and cleared its latch");

            LogAssert.NoUnexpectedReceived();

            curve.Dispose();
        }
    }
}
