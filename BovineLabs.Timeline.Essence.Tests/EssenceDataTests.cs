using BovineLabs.Essence.Data;
using BovineLabs.Essence.Data.Actions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Essence.Data;
using NUnit.Framework;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Tests
{
    public class EssenceDataTests
    {
        [Test]
        public void ActionTickDistributionState_Defaults()
        {
            var state = default(ActionTickDistributionState);
            Assert.AreEqual(0f, state.ElapsedTime);
            Assert.AreEqual(0, state.AppliedTicks);
            Assert.IsFalse(state.EndFired);
        }

        [Test]
        public void ActionTickDistributionState_IsValueType()
        {
            Assert.IsTrue(typeof(ActionTickDistributionState).IsValueType);
        }

        [Test]
        public void ActionTickDistributionState_ImplementsIComponentData()
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(ActionTickDistributionState)));
        }

        [Test]
        public void ActionTickDistributionState_SetValues()
        {
            var state = new ActionTickDistributionState
            {
                ElapsedTime = 1.5f,
                AppliedTicks = 3,
                EndFired = true
            };
            Assert.AreEqual(1.5f, state.ElapsedTime);
            Assert.AreEqual(3, state.AppliedTicks);
            Assert.IsTrue(state.EndFired);
        }

        [Test]
        public void ActionTickDistribution_IsValueType()
        {
            Assert.IsTrue(typeof(ActionTickDistribution).IsValueType);
        }

        [Test]
        public void ActionTickDistribution_ImplementsIComponentData()
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(ActionTickDistribution)));
        }

        [Test]
        public void ActionTickDistribution_Defaults()
        {
            var data = default(ActionTickDistribution);
            Assert.IsFalse(data.Curve.IsCreated);
            Assert.AreEqual(default(Target), data.From);
            Assert.AreEqual(default(StatKey), data.TicPerSecond);
            Assert.AreEqual(default(StatKey), data.TicDuration);
            Assert.AreEqual(default(ConditionKey), data.OnEnd);
            Assert.AreEqual(default(Target), data.To);
            Assert.AreEqual(default(IntrinsicKey), data.TickStore);
            Assert.AreEqual(default(ConditionKey), data.OnTic);
        }

        [Test]
        public void TimelineEssenceEventData_Defaults()
        {
            var data = default(TimelineEssenceEventData);
            Assert.AreEqual(default(Target), data.RouteTo);
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
            Assert.AreEqual(default(Target), data.RouteTo);
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
            Assert.AreEqual(default(Target), data.RouteTo);
            Assert.AreEqual(default(StatKey), data.Stat);
            Assert.AreEqual(default(StatModifyType), data.ModifyType);
            Assert.AreEqual(0f, data.Value);
        }

        [Test]
        public void TimelineEssenceStatData_SetValues()
        {
            var data = new TimelineEssenceStatData
            {
                RouteTo = Target.Owner,
                Stat = new StatKey { Value = 42 },
                ModifyType = StatModifyType.Added,
                Value = 3.14f
            };
            Assert.AreEqual(Target.Owner, data.RouteTo);
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
