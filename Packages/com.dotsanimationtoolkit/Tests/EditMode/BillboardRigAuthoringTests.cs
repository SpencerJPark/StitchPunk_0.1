// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The rig-side half of amendment A44: the billboard-root rows a rig declares, the stable ids
    /// they carry, and validation rules V21, V22, V23 and V25 (the ragdoll spec's V-R8, added
    /// alongside the Phase D address generalisation). Each validation fixture starts from a rig
    /// that validates clean and breaks exactly one thing, matching
    /// <see cref="ClipValidationTests"/>'s discipline.
    /// </summary>
    public sealed class BillboardRigAuthoringTests
    {
        private const uint TorsoTargetId = 7u;
        private const uint HandTargetId = 3u;
        private const uint MissingTargetId = 999u;
        private const ulong RigKey = 0x4400UL;

        private const string ItemPivotPath = "Root/HandR/ItemPivot";
        private const string OtherPivotPath = "Root/HandL/ItemPivot";
        private const string SpineBoneName = "Spine1";

        private AuthoringTestAssets assets;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
        }

        [TearDown]
        public void TearDown()
        {
            assets.DestroyAll();
        }

        // -----------------------------------------------------------------------------------
        // Baseline.
        // -----------------------------------------------------------------------------------

        private RigAsset CreateValidRig()
        {
            return assets.CreateRig("Rig", RigKey, 2, new uint[] { TorsoTargetId, HandTargetId });
        }

        private static BillboardRootDefinition AddTargetRoot(
            RigAsset rig, string displayName, uint targetId)
        {
            BillboardRootDefinition rootDefinition = new BillboardRootDefinition
            {
                displayName = displayName,
                address = new RigNodeAddress
                {
                    kind = RigNodeAddressKind.RigTarget,
                    targetId = targetId
                }
            };
            rig.billboardRoots.Add(rootDefinition);
            return rootDefinition;
        }

        private static BillboardRootDefinition AddPathRoot(
            RigAsset rig, string displayName, string hierarchyPath)
        {
            BillboardRootDefinition rootDefinition = new BillboardRootDefinition
            {
                displayName = displayName,
                address = new RigNodeAddress
                {
                    kind = RigNodeAddressKind.HierarchyPath,
                    hierarchyPath = hierarchyPath
                }
            };
            rig.billboardRoots.Add(rootDefinition);
            return rootDefinition;
        }

        private static BillboardRootDefinition AddBoneRoot(
            RigAsset rig, string displayName, string boneName)
        {
            BillboardRootDefinition rootDefinition = new BillboardRootDefinition
            {
                displayName = displayName,
                address = new RigNodeAddress
                {
                    kind = RigNodeAddressKind.Bone,
                    boneName = boneName
                }
            };
            rig.billboardRoots.Add(rootDefinition);
            return rootDefinition;
        }

        [Test]
        public void ARigWithNoBillboardRootsValidatesClean()
        {
            RigAsset rig = CreateValidRig();

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "Billboarding is opt-in; a rig that declares no roots must cost nothing and say nothing.");
        }

        [Test]
        public void ARigWithBothAddressKindsValidatesClean()
        {
            RigAsset rig = CreateValidRig();
            AddTargetRoot(rig, "Torso", TorsoTargetId);
            AddPathRoot(rig, "Held Item", ItemPivotPath);

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "A character billboarding as a whole with an independently billboarded held item " +
                "is the case A44 exists for, and it must validate clean.");
        }

        // -----------------------------------------------------------------------------------
        // V21: an address that names nothing.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V21_FiresWhenATargetAddressNamesNoTargetOfThisRig()
        {
            RigAsset rig = CreateValidRig();
            AddTargetRoot(rig, "Ghost", MissingTargetId);

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V21, ValidationSeverity.Error);
        }

        [Test]
        public void V21_FiresWhenATargetAddressIsLeftAtTheReservedZeroId()
        {
            RigAsset rig = CreateValidRig();
            AddTargetRoot(rig, "Unassigned", 0u);

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V21, ValidationSeverity.Error);
        }

        /// <summary>
        /// Pins the documented half-reachability of V21 rather than the absence of a bug.
        /// </summary>
        /// <remarks>
        /// A <see cref="RigNodeAddressKind.HierarchyPath"/> address names a transform of the
        /// authoring prefab, and a <see cref="RigAsset"/> neither references a prefab nor carries a
        /// hierarchy of its own — its target list is flat. Rig-scope validation therefore cannot
        /// resolve a path, and the entity bake owns that half of V21. Without this test the silence
        /// reads as "paths are always valid", which is the reading that would let someone later
        /// delete the bake-side check as redundant.
        /// </remarks>
        [Test]
        public void V21_SaysNothingAboutAPathAddress_BecauseARigCannotSeeThePrefab()
        {
            RigAsset rig = CreateValidRig();
            AddPathRoot(rig, "Nowhere", "Root/ThisDoesNotExist/AtAll");

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "Path addresses are resolved by the bake, which holds the prefab; rig scope cannot " +
                "judge them and must not pretend to.");
        }

        [Test]
        public void V21_TreatsAnEmptyPathAsThePrefabRoot_WhichIsALegalNodeToBillboard()
        {
            RigAsset rig = CreateValidRig();
            AddPathRoot(rig, "Actor Root", string.Empty);

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "An empty path addresses the prefab root — the whole-actor billboard A41 shipped — " +
                "so it is an address, not a missing value.");
        }

        // -----------------------------------------------------------------------------------
        // V22: two roots on one node.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V22_FiresWhenTwoRootsAddressTheSameTarget()
        {
            RigAsset rig = CreateValidRig();
            AddTargetRoot(rig, "Torso", TorsoTargetId);
            AddTargetRoot(rig, "Torso Again", TorsoTargetId);

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V22, ValidationSeverity.Error);
        }

        [Test]
        public void V22_FiresWhenTwoRootsAddressTheSamePath()
        {
            RigAsset rig = CreateValidRig();
            AddPathRoot(rig, "Held Item", ItemPivotPath);
            AddPathRoot(rig, "Held Item Copy", ItemPivotPath);

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V22, ValidationSeverity.Error);
        }

        [Test]
        public void V22_DoesNotFireForDistinctNodesOfEitherKind()
        {
            RigAsset rig = CreateValidRig();
            AddTargetRoot(rig, "Torso", TorsoTargetId);
            AddTargetRoot(rig, "Hand", HandTargetId);
            AddPathRoot(rig, "Right Item", ItemPivotPath);
            AddPathRoot(rig, "Left Item", OtherPivotPath);

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "Four roots on four different nodes is an ordinary rig, not a duplicate.");
        }

        /// <summary>
        /// The two address kinds name disjoint things, so the duplicate key must carry the kind —
        /// target id 7 and the path "7" are not the same node.
        /// </summary>
        [Test]
        public void V22_DoesNotConfuseATargetIdWithAPathThatSpellsTheSameNumber()
        {
            RigAsset rig = CreateValidRig();
            AddTargetRoot(rig, "Torso", TorsoTargetId);
            AddPathRoot(rig, "Oddly Named Node", TorsoTargetId.ToString());

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "A target id and a path are different address spaces; a key that could not tell " +
                "them apart would report a duplicate that is not one.");
        }

        /// <summary>
        /// One fault reports once. Two roots pointing at the same absent target is a single missing
        /// target, and adding a duplicate finding on top buries the fix under its own symptom.
        /// </summary>
        [Test]
        public void V22_StaysSilentWhenTheSharedAddressIsItselfUnresolved()
        {
            RigAsset rig = CreateValidRig();
            AddTargetRoot(rig, "Ghost", MissingTargetId);
            AddTargetRoot(rig, "Ghost Copy", MissingTargetId);

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V21, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // V23: an axis-constrained root with no axis.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V23_FiresWhenAnAxisConstrainedRootHasAZeroLengthAxis()
        {
            RigAsset rig = CreateValidRig();
            BillboardRootDefinition rootDefinition = AddTargetRoot(rig, "Sail", TorsoTargetId);
            rootDefinition.mode = BillboardMode.AxisConstrained;
            rootDefinition.constraintAxis = float3.zero;

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V23, ValidationSeverity.Error);
        }

        [Test]
        public void V23_DoesNotFireForAZeroAxisOnAModeThatIgnoresIt()
        {
            RigAsset rig = CreateValidRig();
            BillboardRootDefinition rootDefinition = AddTargetRoot(rig, "Torso", TorsoTargetId);
            rootDefinition.mode = BillboardMode.ScreenAligned;
            rootDefinition.constraintAxis = float3.zero;

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "Only AxisConstrained reads the axis; every other mode must not be held to a field " +
                "it never consults.");
        }

        [Test]
        public void V23_DoesNotFireForAnAuthoredNonVerticalAxis()
        {
            RigAsset rig = CreateValidRig();
            BillboardRootDefinition rootDefinition = AddTargetRoot(rig, "Book Cover", TorsoTargetId);
            rootDefinition.mode = BillboardMode.AxisConstrained;
            rootDefinition.constraintAxis = new float3(1f, 0f, 0f);

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "An arbitrary axis is the entire point of the mode.");
        }

        // -----------------------------------------------------------------------------------
        // V25 (ragdoll spec V-R8): a billboard root addressed by bone.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V25_FiresWhenARootAddressesABoneByName()
        {
            RigAsset rig = CreateValidRig();
            AddBoneRoot(rig, "Spine", SpineBoneName);

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V25, ValidationSeverity.Error);
        }

        /// <summary>
        /// A row that fails V25 must not also be checked for a V22 address-key duplicate — the same
        /// discipline V21's unresolved-target case already follows, so one authoring mistake is
        /// reported once.
        /// </summary>
        [Test]
        public void V25_DoesNotAlsoFireV22ForTwoBoneRootsOnTheSameBoneName()
        {
            RigAsset rig = CreateValidRig();
            AddBoneRoot(rig, "Spine", SpineBoneName);
            AddBoneRoot(rig, "Spine Again", SpineBoneName);

            List<ValidationMessage> messages = ClipValidation.ValidateRig(rig);
            AssertOnlyCode(messages, ValidationCode.V25, ValidationSeverity.Error);
            Assert.AreEqual(2, messages.Count, "Both bone-addressed rows fire their own V25.");
        }

        [Test]
        public void V25_DoesNotFireForTargetOrPathAddresses()
        {
            RigAsset rig = CreateValidRig();
            AddTargetRoot(rig, "Torso", TorsoTargetId);
            AddPathRoot(rig, "Held Item", ItemPivotPath);

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "The Bone kind exists for the ragdoll body list; a rig target or hierarchy path " +
                "address must not trip the rule meant for it.");
        }

        // -----------------------------------------------------------------------------------
        // Defaults and identity.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The default must be the host game's behaviour, which A44 preserves deliberately: every
        /// root taking the same rotation from the camera's forward vector.
        /// </summary>
        [Test]
        public void ANewBillboardRootDefaultsToScreenAligned()
        {
            BillboardRootDefinition rootDefinition = new BillboardRootDefinition();

            Assert.AreEqual(BillboardMode.ScreenAligned, rootDefinition.mode);
        }

        [Test]
        public void ANewBillboardRootDefaultsToAnUprightAxisAndAnEightWaySnapWheel()
        {
            BillboardRootDefinition rootDefinition = new BillboardRootDefinition();

            Assert.AreEqual(new float3(0f, 1f, 0f), rootDefinition.constraintAxis);
            Assert.AreEqual(8, rootDefinition.snapSteps);
            Assert.IsFalse(rootDefinition.snapEnabled, "Snapping is opt-in.");
            Assert.IsFalse(rootDefinition.clampEnabled, "Clamping is opt-in.");
        }

        [Test]
        public void EnsureStableIdsMintsAnIdForEveryBillboardRoot()
        {
            RigAsset rig = CreateValidRig();
            AddTargetRoot(rig, "Torso", TorsoTargetId);
            AddPathRoot(rig, "Held Item", ItemPivotPath);

            rig.EnsureStableIds();

            Assert.IsTrue(rig.billboardRoots[0].Id.IsValid, "A root built from code must be identified.");
            Assert.IsTrue(rig.billboardRoots[1].Id.IsValid, "A root built from code must be identified.");
            Assert.AreNotEqual(
                rig.billboardRoots[0].Id,
                rig.billboardRoots[1].Id,
                "Ids are what clip billboard tracks bind to, so two roots may never share one.");
        }

        [Test]
        public void EnsureStableIdsLeavesAnAlreadyIdentifiedRootAlone()
        {
            RigAsset rig = CreateValidRig();
            AddTargetRoot(rig, "Torso", TorsoTargetId);
            rig.EnsureStableIds();
            BillboardRootId mintedId = rig.billboardRoots[0].Id;

            rig.EnsureStableIds();

            Assert.AreEqual(
                mintedId,
                rig.billboardRoots[0].Id,
                "The assignment is idempotent; re-minting would orphan every clip keyed against it.");
        }

        /// <summary>
        /// The lists are identified independently of one another. Before A44 the method returned
        /// early on the first null list, which would have made a rig with no sockets ship billboard
        /// roots that no clip could ever bind to.
        /// </summary>
        [Test]
        public void EnsureStableIdsIdentifiesBillboardRootsEvenWhenTheRigHasNoSocketList()
        {
            RigAsset rig = CreateValidRig();
            AddTargetRoot(rig, "Torso", TorsoTargetId);
            rig.sockets = null;

            rig.EnsureStableIds();

            Assert.IsTrue(
                rig.billboardRoots[0].Id.IsValid,
                "A missing socket list must not stop the billboard roots being identified.");
        }

        // -----------------------------------------------------------------------------------
        // Assertion helpers. Copies of ClipValidationTests', so this fixture stands alone.
        // -----------------------------------------------------------------------------------

        private static void AssertNoFindings(IReadOnlyList<ValidationMessage> messages, string because)
        {
            Assert.AreEqual(0, messages.Count, because + " Got: " + Describe(messages));
        }

        private static void AssertOnlyCode(
            IReadOnlyList<ValidationMessage> messages,
            ValidationCode expectedCode,
            ValidationSeverity expectedSeverity)
        {
            AssertContainsCode(messages, expectedCode, expectedSeverity);
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                Assert.AreEqual(
                    expectedCode,
                    messages[messageIndex].code,
                    "The fixture breaks exactly one rule, so no other code may fire: " +
                    Describe(messages));
            }
        }

        private static void AssertContainsCode(
            IReadOnlyList<ValidationMessage> messages,
            ValidationCode expectedCode,
            ValidationSeverity expectedSeverity)
        {
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                if (messages[messageIndex].code == expectedCode &&
                    messages[messageIndex].severity == expectedSeverity)
                {
                    Assert.IsNotNull(
                        messages[messageIndex].text,
                        "Every finding must carry an explanation.");
                    return;
                }
            }
            Assert.Fail(
                "Expected " + expectedCode + " at severity " + expectedSeverity +
                " but got: " + Describe(messages));
        }

        private static string Describe(IReadOnlyList<ValidationMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                return "(no findings)";
            }
            StringBuilder description = new StringBuilder();
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                description.Append("\n  ").Append(messages[messageIndex].ToString());
            }
            return description.ToString();
        }
    }
}
