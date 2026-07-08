using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Essence.Data;
using NUnit.Framework;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Tests
{
    // Locks the immediate (main-thread) StatModifierCleanup attach: a stat clip that applies its modifier and is then
    // destroyed in the SAME frame — before any further system update — must still have a removal path, so the target's
    // StatModifiers buffer ends up empty. With the old next-frame ECB attach, the cleanup shadow was never created for
    // a same-frame-destroyed clip and the modifier leaked forever.
    public class TimelineEssenceStatDestroyTests : TimelineEssenceTestFixture
    {
        private Entity MakeStatTarget()
        {
            var target = this.Manager.CreateEntity();
            this.Manager.AddBuffer<StatModifiers>(target);
            this.Manager.AddComponentData(target, new StatChanged());
            return target;
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

        [Test]
        public void StatClip_DestroyedSameFrameAsApply_ModifierIsCleanedUp()
        {
            var ecb = this.World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
            var sys = this.World.CreateSystem<TimelineEssenceStatSystem>();

            var target = this.MakeStatTarget();
            var clip = this.MakeStatClip(target);

            // Frame 1: apply the modifier. The cleanup safety-net is attached on the main thread INSIDE this update
            // (not via a next-frame ECB), so it already exists the instant the clip is destroyed below. Deliberately
            // do NOT play the BeginSim ECB here — that is what distinguishes the immediate attach from the old path.
            sys.Update(this.WorldUnmanaged);
            this.Manager.CompleteAllTrackedJobs();
            Assert.AreEqual(1, this.Manager.GetBuffer<StatModifiers>(target).Length, "modifier applied on activation");

            // Destroy the clip the same frame, before any further system update.
            this.Manager.DestroyEntity(clip);

            // Frame 2: the surviving cleanup shadow drives GatherDestroyedJob, which removes the modifier from the target.
            sys.Update(this.WorldUnmanaged);
            ecb.Update();
            this.Manager.CompleteAllTrackedJobs();

            Assert.AreEqual(0, this.Manager.GetBuffer<StatModifiers>(target).Length,
                "a same-frame-destroyed stat clip must not leak its modifier");
        }
    }
}
