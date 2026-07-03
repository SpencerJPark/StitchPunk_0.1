using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace StitchPunk.Tests
{
    // Pins the BehaviorCommandCatalog sets. Adding a BehaviorCommandType value (or an interpreter
    // arm) without a deliberate catalog + test update fails here on purpose — the catalog is the
    // single source of truth keeping "authorable in a BehaviorSO" and "runnable at runtime" in sync.
    [TestFixture]
    public sealed class BehaviorCommandCatalogTests
    {
        private static readonly HashSet<BehaviorCommandType> ExpectedImplemented =
            new HashSet<BehaviorCommandType>
            {
                BehaviorCommandType.PlayAnimation,
                BehaviorCommandType.WaitTime,
                BehaviorCommandType.Approach,
                BehaviorCommandType.RequestAttack,
                BehaviorCommandType.RequestPickup,
                BehaviorCommandType.ModifyMotivation,
                BehaviorCommandType.FleeFromTarget,
                BehaviorCommandType.ReleaseInteraction,
                BehaviorCommandType.LoopUntil,
                BehaviorCommandType.StopAnimation,
                BehaviorCommandType.PlayActionAnimation,
                BehaviorCommandType.RequestSocialResponse,
                BehaviorCommandType.PlaySound,
            };

        private static readonly HashSet<BehaviorCommandType> ExpectedBlocking =
            new HashSet<BehaviorCommandType>
            {
                BehaviorCommandType.Approach,
                BehaviorCommandType.WaitTime,
                BehaviorCommandType.FleeFromTarget,
                BehaviorCommandType.LoopUntil,
            };

        [Test]
        public void ImplementedSet_MatchesTheInterpreterArms()
        {
            foreach (BehaviorCommandType commandType in Enum.GetValues(typeof(BehaviorCommandType)))
            {
                bool expected = ExpectedImplemented.Contains(commandType);
                Assert.AreEqual(expected, BehaviorCommandCatalog.IsImplemented(commandType),
                    commandType + ": catalog disagrees with the pinned implemented set. If you added or " +
                    "removed an interpreter arm in BehaviorExecutionSystem, update BehaviorCommandCatalog " +
                    "AND this test in the same commit.");
            }
        }

        [Test]
        public void UnimplementedSet_IsExactlyTheFourDeclaredForwardValues()
        {
            List<BehaviorCommandType> unimplemented = new List<BehaviorCommandType>();
            foreach (BehaviorCommandType commandType in Enum.GetValues(typeof(BehaviorCommandType)))
            {
                if (!BehaviorCommandCatalog.IsImplemented(commandType))
                    unimplemented.Add(commandType);
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    BehaviorCommandType.SpawnEntity,
                    BehaviorCommandType.ModifyStat,
                    BehaviorCommandType.StartDialogue,
                    BehaviorCommandType.ApplyForce,
                },
                unimplemented,
                "The set of declared-but-unimplemented commands changed — update the catalog, this test, " +
                "and (if a command was implemented) remove its bake warning expectation.");
        }

        [Test]
        public void BlockingSet_IsExactlyTheFourBlockingCommands()
        {
            foreach (BehaviorCommandType commandType in Enum.GetValues(typeof(BehaviorCommandType)))
            {
                bool expected = ExpectedBlocking.Contains(commandType);
                Assert.AreEqual(expected, BehaviorCommandCatalog.IsBlocking(commandType),
                    commandType + ": blocking classification changed — blocking commands are illegal in " +
                    "interruptionCleanup, so this must match BehaviorExecutionSystem's actual semantics.");
            }
        }
    }
}
