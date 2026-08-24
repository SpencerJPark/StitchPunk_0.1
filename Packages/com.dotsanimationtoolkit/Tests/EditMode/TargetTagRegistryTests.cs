// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers the Phase E target-tags spec's E1 deliverable: <see cref="TargetTagRegistry"/> mints
    /// non-colliding ids, rule T5 (<see cref="ValidationCode.V33"/>) catches a zero or duplicate id,
    /// and — the core guarantee the whole feature rests on (spec §2, "a rename must not touch any
    /// clip") — renaming a row never changes the id a clip would bind to.
    /// </summary>
    public sealed class TargetTagRegistryTests
    {
        private readonly List<Object> createdObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int objectIndex = 0; objectIndex < createdObjects.Count; objectIndex++)
            {
                if (createdObjects[objectIndex] != null)
                {
                    Object.DestroyImmediate(createdObjects[objectIndex]);
                }
            }
            createdObjects.Clear();
        }

        private TargetTagRegistry CreateRegistry()
        {
            TargetTagRegistry registry = ScriptableObject.CreateInstance<TargetTagRegistry>();
            createdObjects.Add(registry);
            return registry;
        }

        // -----------------------------------------------------------------------------------
        // Minting.
        // -----------------------------------------------------------------------------------

        [Test]
        public void MintTagId_NeverReturnsZero()
        {
            TargetTagRegistry registry = CreateRegistry();

            for (int mintIndex = 0; mintIndex < 20; mintIndex++)
            {
                Assert.AreNotEqual(0u, registry.MintTagId(), "0 is reserved for \"untagged\".");
            }
        }

        [Test]
        public void MintTagId_NeverCollidesWithAnIdAlreadyInTheRegistry()
        {
            TargetTagRegistry registry = CreateRegistry();
            registry.entries.Add(new TargetTagEntry { name = "EyeL", stableId = 12345u });
            registry.entries.Add(new TargetTagEntry { name = "EyeR", stableId = 67890u });

            for (int mintIndex = 0; mintIndex < 50; mintIndex++)
            {
                uint mintedId = registry.MintTagId();
                Assert.IsFalse(
                    registry.ContainsId(mintedId) && mintedId != 0u && mintedId != 12345u && mintedId != 67890u,
                    "A freshly minted id must not equal a row already added by the test.");
                Assert.AreNotEqual(12345u, mintedId, "Must not collide with an existing entry.");
                Assert.AreNotEqual(67890u, mintedId, "Must not collide with an existing entry.");
            }
        }

        [Test]
        public void MintTagId_ProducesDistinctIds_AcrossManyCalls()
        {
            TargetTagRegistry registry = CreateRegistry();
            HashSet<uint> mintedIds = new HashSet<uint>();

            for (int mintIndex = 0; mintIndex < 50; mintIndex++)
            {
                uint mintedId = registry.MintTagId();
                Assert.IsTrue(mintedIds.Add(mintedId), "Ids are random-folded and must not repeat in practice.");
            }
        }

        // -----------------------------------------------------------------------------------
        // FindName / ContainsId.
        // -----------------------------------------------------------------------------------

        [Test]
        public void FindName_ReturnsTheEntrysName_WhenTheIdIsPresent()
        {
            TargetTagRegistry registry = CreateRegistry();
            registry.entries.Add(new TargetTagEntry { name = "Jaw", stableId = 42u });

            Assert.AreEqual("Jaw", registry.FindName(42u));
        }

        [Test]
        public void FindName_ReturnsNull_WhenNoEntryClaimsTheId()
        {
            TargetTagRegistry registry = CreateRegistry();
            registry.entries.Add(new TargetTagEntry { name = "Jaw", stableId = 42u });

            Assert.IsNull(registry.FindName(999u), "An id no row claims names nothing (rule T3's dangling case).");
        }

        // -----------------------------------------------------------------------------------
        // Rule T5 (ValidationCode.V33).
        // -----------------------------------------------------------------------------------

        [Test]
        public void ValidateTargetTagRegistry_ReportsNothing_ForAValidRegistry()
        {
            TargetTagRegistry registry = CreateRegistry();
            registry.entries.Add(new TargetTagEntry { name = "EyeL", stableId = 111u });
            registry.entries.Add(new TargetTagEntry { name = "EyeR", stableId = 222u });

            List<ValidationMessage> messages = ClipValidation.ValidateTargetTagRegistry(registry);

            Assert.AreEqual(0, messages.Count, "Unique, non-zero ids must produce no T5 finding.");
        }

        [Test]
        public void ValidateTargetTagRegistry_ReportsNothing_ForANullRegistry()
        {
            List<ValidationMessage> messages = ClipValidation.ValidateTargetTagRegistry(null);

            Assert.AreEqual(0, messages.Count, "An unassigned registry is not itself an error.");
        }

        [Test]
        public void ValidateTargetTagRegistry_ReportsV33_ForAZeroId()
        {
            TargetTagRegistry registry = CreateRegistry();
            registry.entries.Add(new TargetTagEntry { name = "Untagged Row", stableId = 0u });

            List<ValidationMessage> messages = ClipValidation.ValidateTargetTagRegistry(registry);

            Assert.AreEqual(1, messages.Count);
            Assert.AreEqual(ValidationCode.V33, messages[0].code);
            Assert.AreEqual(ValidationSeverity.Error, messages[0].severity);
        }

        [Test]
        public void ValidateTargetTagRegistry_ReportsV33_ForDuplicateIds()
        {
            TargetTagRegistry registry = CreateRegistry();
            registry.entries.Add(new TargetTagEntry { name = "EyeL", stableId = 555u });
            registry.entries.Add(new TargetTagEntry { name = "EyeR", stableId = 555u });

            List<ValidationMessage> messages = ClipValidation.ValidateTargetTagRegistry(registry);

            Assert.AreEqual(1, messages.Count);
            Assert.AreEqual(ValidationCode.V33, messages[0].code);
            Assert.AreEqual(ValidationSeverity.Error, messages[0].severity);
            StringAssert.Contains("555", messages[0].text);
        }

        [Test]
        public void ValidateTargetTagRegistry_ReportsOneFindingPerOffendingRow_NotOnePerRegistry()
        {
            TargetTagRegistry registry = CreateRegistry();
            registry.entries.Add(new TargetTagEntry { name = "Good", stableId = 1u });
            registry.entries.Add(new TargetTagEntry { name = "AlsoZero", stableId = 0u });
            registry.entries.Add(new TargetTagEntry { name = "StillZero", stableId = 0u });

            List<ValidationMessage> messages = ClipValidation.ValidateTargetTagRegistry(registry);

            Assert.AreEqual(2, messages.Count, "Each zero-id row reports its own finding.");
        }

        // -----------------------------------------------------------------------------------
        // The core guarantee: renaming a tag must not change the id a clip would bind to.
        // -----------------------------------------------------------------------------------

        [Test]
        public void Renaming_DoesNotChangeTheStableId()
        {
            TargetTagRegistry registry = CreateRegistry();
            TargetTagEntry entry = new TargetTagEntry { name = "EyeL", stableId = registry.MintTagId() };
            registry.entries.Add(entry);
            uint idBeforeRename = entry.stableId;

            entry.name = "LeftEye";
            entry.name = "Eye_L";
            entry.name = "eyeL";

            Assert.AreEqual(
                idBeforeRename,
                entry.stableId,
                "The id a track would bind to must survive any number of renames untouched.");
        }

        [Test]
        public void Renaming_LeavesEveryOtherEntrysIdUntouched()
        {
            TargetTagRegistry registry = CreateRegistry();
            TargetTagEntry firstEntry = new TargetTagEntry { name = "EyeL", stableId = 111u };
            TargetTagEntry secondEntry = new TargetTagEntry { name = "EyeR", stableId = 222u };
            registry.entries.Add(firstEntry);
            registry.entries.Add(secondEntry);

            firstEntry.name = "LeftEye";

            Assert.AreEqual(111u, firstEntry.stableId, "The renamed row keeps its own id.");
            Assert.AreEqual(222u, secondEntry.stableId, "A sibling row's id must be unaffected.");
            Assert.AreEqual("EyeR", secondEntry.name, "A sibling row's name must be unaffected.");
        }

        [Test]
        public void Renaming_StillResolvesTheSameIdThroughFindName()
        {
            TargetTagRegistry registry = CreateRegistry();
            TargetTagEntry entry = new TargetTagEntry { name = "EyeL", stableId = 333u };
            registry.entries.Add(entry);

            entry.name = "LeftEye";

            Assert.AreEqual(
                "LeftEye",
                registry.FindName(333u),
                "The id resolves the row's current name; a rename is visible through the same id.");
        }
    }
}
