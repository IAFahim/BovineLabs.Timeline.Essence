using BovineLabs.Essence.Data;
using BovineLabs.Timeline.Essence.Data;
using NUnit.Framework;

namespace BovineLabs.Timeline.Essence.Tests
{
    public class StatModifierMathTests
    {
        [Test]
        public void ZeroStat_ReturnsFalse()
        {
            var built = StatModifierMath.TryBuildStatModifier(
                new StatKey { Value = new BovineLabs.Core.BLId(0) }, StatModifyType.Added, 5.9f, out _);

            Assert.IsFalse(built);
        }

        [Test]
        public void Added_CastsToInt()
        {
            var built = StatModifierMath.TryBuildStatModifier(
                new StatKey { Value = new BovineLabs.Core.BLId(7) }, StatModifyType.Added, 5.9f, out var modifier);

            Assert.IsTrue(built);
            Assert.AreEqual(5, modifier.Value);
        }

        [Test]
        public void Additive_WritesValueFloat()
        {
            var built = StatModifierMath.TryBuildStatModifier(
                new StatKey { Value = new BovineLabs.Core.BLId(7) }, StatModifyType.Additive, 5.9f, out var modifier);

            Assert.IsTrue(built);
            Assert.AreEqual(5.9f, modifier.ValueFloat, 0.0001f);
        }

        [Test]
        public void Type_Preserved()
        {
            var built = StatModifierMath.TryBuildStatModifier(
                new StatKey { Value = new BovineLabs.Core.BLId(42) }, StatModifyType.Added, 1f, out var modifier);

            Assert.IsTrue(built);
            Assert.AreEqual(42, modifier.Type.Value);
            Assert.AreEqual(StatModifyType.Added, modifier.ModifyType);
        }
    }
}
