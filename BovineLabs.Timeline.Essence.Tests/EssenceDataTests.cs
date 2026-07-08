using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Essence.Data;
using NUnit.Framework;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Tests
{
    public class EssenceDataTests
    {
        [Test]
        public void TimelineEssenceEventData_Defaults()
        {
            var data = default(TimelineEssenceEventData);
            Assert.AreEqual(default(Target), data.Route.ReadRootFrom);
            Assert.AreEqual(default(ConditionKey), data.Event);
            Assert.AreEqual(0, data.Value);
        }

        [Test]
        public void TimelineEssenceEventData_IsValueType()
        {
            Assert.IsTrue(typeof(TimelineEssenceEventData).IsValueType);
        }

        [Test]
        public void TimelineEssenceEventData_ImplementsIComponentData()
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(TimelineEssenceEventData)));
        }

        [Test]
        public void TimelineEssenceIntrinsicData_Defaults()
        {
            var data = default(TimelineEssenceIntrinsicData);
            Assert.AreEqual(default(Target), data.Route.ReadRootFrom);
            Assert.AreEqual(default(IntrinsicKey), data.Intrinsic);
            Assert.AreEqual(0, data.Amount);
        }

        [Test]
        public void TimelineEssenceIntrinsicData_IsValueType()
        {
            Assert.IsTrue(typeof(TimelineEssenceIntrinsicData).IsValueType);
        }

        [Test]
        public void TimelineEssenceIntrinsicData_ImplementsIComponentData()
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(TimelineEssenceIntrinsicData)));
        }

        [Test]
        public void TimelineEssenceStatData_Defaults()
        {
            var data = default(TimelineEssenceStatData);
            Assert.AreEqual(default(Target), data.Route.ReadRootFrom);
            Assert.AreEqual(default(StatKey), data.Stat);
            Assert.AreEqual(default(StatModifyType), data.ModifyType);
            Assert.AreEqual(0f, data.Value);
        }

        [Test]
        public void TimelineEssenceStatData_SetValues()
        {
            var data = new TimelineEssenceStatData
            {
                Route = new EntityLinkRef { ReadRootFrom = Target.Owner },
                Stat = new StatKey { Value = new BovineLabs.Core.BLId(42) },
                ModifyType = StatModifyType.Added,
                Value = 3.14f
            };
            Assert.AreEqual(Target.Owner, data.Route.ReadRootFrom);
            Assert.AreEqual(42, data.Stat.Value);
            Assert.AreEqual(StatModifyType.Added, data.ModifyType);
            Assert.AreEqual(3.14f, data.Value, 0.0001f);
        }

        [Test]
        public void TimelineEssenceStatData_IsValueType()
        {
            Assert.IsTrue(typeof(TimelineEssenceStatData).IsValueType);
        }

        [Test]
        public void TimelineEssenceStatData_ImplementsIComponentData()
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(TimelineEssenceStatData)));
        }

        [Test]
        public void DistributionCurveBlob_IsValueType()
        {
            Assert.IsTrue(typeof(DistributionCurveBlob).IsValueType);
        }
    }
}