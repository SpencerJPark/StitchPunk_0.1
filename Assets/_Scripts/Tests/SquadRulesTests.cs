using NUnit.Framework;
using Unity.Mathematics;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class SquadRulesTests
    {
        private const float OnFootCommandRadius = 15f;

        [Test]
        public void ResolveNumberKey_WithUnitsSelectedAndNoCtrl_AssignsSelectionToSquad()
        {
            SquadKeyAction action = SquadRules.ResolveNumberKey(2, 2, true, false);

            Assert.AreEqual(SquadKeyAction.AssignSelectionToSquad, action);
        }

        [Test]
        public void ResolveNumberKey_WithNothingSelected_SwitchesToSquad()
        {
            SquadKeyAction action = SquadRules.ResolveNumberKey(1, 2, false, false);

            Assert.AreEqual(SquadKeyAction.SwitchToSquad, action);
        }

        [Test]
        public void ResolveNumberKey_CtrlWithUnitsSelected_SwitchesToSquad()
        {
            SquadKeyAction action = SquadRules.ResolveNumberKey(2, 2, true, true);

            Assert.AreEqual(SquadKeyAction.SwitchToSquad, action);
        }

        [Test]
        public void ResolveNumberKey_LockedSquadNumber_DoesNothing()
        {
            SquadKeyAction action = SquadRules.ResolveNumberKey(3, 2, true, false);

            Assert.AreEqual(SquadKeyAction.None, action);
        }

        [Test]
        public void NextSquadIndex_CyclesOneTwoOneWithTwoUnlocked()
        {
            int firstNext = SquadRules.NextSquadIndex(1, 2);
            int secondNext = SquadRules.NextSquadIndex(firstNext, 2);

            Assert.AreEqual(2, firstNext);
            Assert.AreEqual(1, secondNext);
        }

        [Test]
        public void NextSquadIndex_WithNoSquadYet_StartsAtSquadOne()
        {
            int nextIndex = SquadRules.NextSquadIndex(0, 2);

            Assert.AreEqual(1, nextIndex);
        }

        [Test]
        public void HearsCommand_CommanderMounted_HearsFromOneHundredMetersAway()
        {
            bool hears = SquadRules.HearsCommand(true, float3.zero, new float3(100f, 0f, 0f), OnFootCommandRadius);

            Assert.IsTrue(hears);
        }

        [Test]
        public void HearsCommand_CommanderOnFoot_HearsWithinRadiusOnly()
        {
            bool heardAtTenMeters = SquadRules.HearsCommand(false, float3.zero, new float3(10f, 0f, 0f), OnFootCommandRadius);
            bool heardAtTwentyMeters = SquadRules.HearsCommand(false, float3.zero, new float3(20f, 0f, 0f), OnFootCommandRadius);

            Assert.IsTrue(heardAtTenMeters);
            Assert.IsFalse(heardAtTwentyMeters);
        }
    }
}
