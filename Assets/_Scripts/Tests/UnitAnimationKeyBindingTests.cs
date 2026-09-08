using NUnit.Framework;

namespace StitchPunk.Tests
{
    // Pins the G5 D1 animation-name convention (the animation name equals the enum name) so
    // UnitLibraryBakingSystem's registry lookups and the Actor Editor's authored names can never
    // silently drift apart from what this project actually resolves at bake time.
    [TestFixture]
    public sealed class UnitAnimationKeyBindingTests
    {
        [Test]
        public void ForAction_ReturnsTheEnumNameVerbatim()
        {
            Assert.AreEqual("MeleeContinuous", AnimationNameConvention.ForAction(ActionType.MeleeContinuous));
        }

        [Test]
        public void ForStance_ComposesIdleAndWalkNames()
        {
            Assert.AreEqual("DefensiveIdle", AnimationNameConvention.ForStanceIdle(StanceType.Defensive));
            Assert.AreEqual("DefensiveWalk", AnimationNameConvention.ForStanceWalk(StanceType.Defensive));
        }
    }
}
