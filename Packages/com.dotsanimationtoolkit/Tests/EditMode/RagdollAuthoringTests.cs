// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The authoring-data half of the Phase D ragdoll work (amendment A50, spec §3): the ragdoll
    /// body rows a rig declares, the stable ids they carry, and validation rules V26–V32 (the
    /// ragdoll spec's V-R1–V-R7). Each validation fixture starts from a rig that validates clean and
    /// breaks exactly one thing, matching <see cref="BillboardRigAuthoringTests"/>'s discipline.
    /// </summary>
    public sealed class RagdollAuthoringTests
    {
        private const uint TorsoTargetId = 7u;
        private const uint HandTargetId = 3u;
        private const uint MissingTargetId = 999u;
        private const ulong RigKey = 0x4500UL;

        private const string HipsPath = "Root/Hips";
        private const string SpinePath = "Root/Hips/Spine";
        private const string OtherLegPath = "Root/OtherHips";
        private const string SpineBoneName = "Spine1";
        private const string ForearmBoneName = "Forearm1";

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

        private static RagdollBodyDefinition AddTargetBody(
            RigAsset rig, string displayName, uint targetId)
        {
            RagdollBodyDefinition bodyDefinition = new RagdollBodyDefinition
            {
                displayName = displayName,
                address = new RigNodeAddress
                {
                    kind = RigNodeAddressKind.RigTarget,
                    targetId = targetId
                }
            };
            rig.ragdollBodies.Add(bodyDefinition);
            return bodyDefinition;
        }

        private static RagdollBodyDefinition AddPathBody(
            RigAsset rig, string displayName, string hierarchyPath)
        {
            RagdollBodyDefinition bodyDefinition = new RagdollBodyDefinition
            {
                displayName = displayName,
                address = new RigNodeAddress
                {
                    kind = RigNodeAddressKind.HierarchyPath,
                    hierarchyPath = hierarchyPath
                }
            };
            rig.ragdollBodies.Add(bodyDefinition);
            return bodyDefinition;
        }

        private static RagdollBodyDefinition AddBoneBody(
            RigAsset rig, string displayName, string boneName)
        {
            RagdollBodyDefinition bodyDefinition = new RagdollBodyDefinition
            {
                displayName = displayName,
                address = new RigNodeAddress
                {
                    kind = RigNodeAddressKind.Bone,
                    boneName = boneName
                }
            };
            rig.ragdollBodies.Add(bodyDefinition);
            return bodyDefinition;
        }

        [Test]
        public void ARigWithNoRagdollBodiesValidatesClean()
        {
            RigAsset rig = CreateValidRig();

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "Ragdolling is opt-in; a rig that declares no bodies must cost nothing and say " +
                "nothing.");
        }

        [Test]
        public void ARigWithAllThreeAddressKindsValidatesClean()
        {
            RigAsset rig = CreateValidRig();
            AddTargetBody(rig, "Torso", TorsoTargetId);
            AddPathBody(rig, "Held Item", HipsPath);
            AddBoneBody(rig, "Spine Bone", SpineBoneName);
            rig.EnsureStableIds();

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "A ragdoll body may weld to a rig target, a bare grouping transform, or a skinned " +
                "bone - the whole point of the address generalisation - and a rig using all three " +
                "must validate clean.");
        }

        // -----------------------------------------------------------------------------------
        // V26 (V-R1): an address that names nothing.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V26_FiresWhenATargetAddressNamesNoTargetOfThisRig()
        {
            RigAsset rig = CreateValidRig();
            AddTargetBody(rig, "Ghost", MissingTargetId);
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V26, ValidationSeverity.Error);
        }

        [Test]
        public void V26_FiresWhenATargetAddressIsLeftAtTheReservedZeroId()
        {
            RigAsset rig = CreateValidRig();
            AddTargetBody(rig, "Unassigned", 0u);
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V26, ValidationSeverity.Error);
        }

        /// <summary>
        /// Pins the documented half-reachability of V26 rather than the absence of a bug - the same
        /// split V21 makes for billboard roots, extended here to a third address kind.
        /// </summary>
        [Test]
        public void V26_SaysNothingAboutAPathOrBoneAddress_BecauseARigCannotSeeThePrefabOrArmature()
        {
            RigAsset rig = CreateValidRig();
            AddPathBody(rig, "Nowhere", "Root/ThisDoesNotExist/AtAll");
            AddBoneBody(rig, "Ghost Bone", "ThisBoneDoesNotExistEither");
            rig.EnsureStableIds();

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "Path and bone addresses are resolved by the bake, which holds the prefab or " +
                "armature; rig scope cannot judge them and must not pretend to.");
        }

        // -----------------------------------------------------------------------------------
        // V27 (V-R2): stable id must exist and must not repeat.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V27_FiresWhenABodyHasNoStableId()
        {
            RigAsset rig = CreateValidRig();
            AddTargetBody(rig, "Torso", TorsoTargetId);
            // Deliberately not calling EnsureStableIds, so the body's id stays 0.

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V27, ValidationSeverity.Error);
        }

        [Test]
        public void V27_FiresWhenTwoBodiesShareAStableId()
        {
            RigAsset rig = CreateValidRig();
            RagdollBodyDefinition firstBody = AddTargetBody(rig, "Torso", TorsoTargetId);
            RagdollBodyDefinition secondBody = AddTargetBody(rig, "Hand", HandTargetId);
            rig.EnsureStableIds();
            secondBody.stableId = firstBody.stableId;

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V27, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // V28 (V-R3): two bodies on one node.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V28_FiresWhenTwoBodiesAddressTheSameTarget()
        {
            RigAsset rig = CreateValidRig();
            AddTargetBody(rig, "Torso", TorsoTargetId);
            AddTargetBody(rig, "Torso Again", TorsoTargetId);
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V28, ValidationSeverity.Error);
        }

        [Test]
        public void V28_FiresWhenTwoBodiesAddressTheSamePath()
        {
            RigAsset rig = CreateValidRig();
            AddPathBody(rig, "Hips", HipsPath);
            AddPathBody(rig, "Hips Again", HipsPath);
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V28, ValidationSeverity.Error);
        }

        [Test]
        public void V28_FiresWhenTwoBodiesAddressTheSameBone()
        {
            RigAsset rig = CreateValidRig();
            AddBoneBody(rig, "Spine", SpineBoneName);
            AddBoneBody(rig, "Spine Again", SpineBoneName);
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V28, ValidationSeverity.Error);
        }

        /// <summary>
        /// The three address kinds name disjoint things, so the duplicate key must carry the kind -
        /// a target id, a path and a bone name that all happen to spell "7" are not the same node.
        /// </summary>
        [Test]
        public void V28_DoesNotConfuseDifferentAddressKindsThatSpellTheSameText()
        {
            RigAsset rig = CreateValidRig();
            AddTargetBody(rig, "Torso", TorsoTargetId);
            AddPathBody(rig, "Oddly Named Node", TorsoTargetId.ToString());
            AddBoneBody(rig, "Oddly Named Bone", TorsoTargetId.ToString());
            rig.EnsureStableIds();

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "Three disjoint address spaces; a key that could not tell them apart would report " +
                "duplicates that are not one.");
        }

        /// <summary>
        /// One fault reports once. Two bodies pointing at the same absent target is a single
        /// missing target, and adding a duplicate finding on top buries the fix under its own
        /// symptom - the same discipline V22 follows against V21's unresolved case.
        /// </summary>
        [Test]
        public void V28_StaysSilentWhenTheSharedAddressIsItselfUnresolved()
        {
            RigAsset rig = CreateValidRig();
            AddTargetBody(rig, "Ghost", MissingTargetId);
            AddTargetBody(rig, "Ghost Copy", MissingTargetId);
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V26, ValidationSeverity.Error);
        }

        /// <summary>
        /// A node that already reports V28 must not also be counted as a second, disconnected root
        /// by V31 - the same "one fault reports once" discipline V25 pins for billboard roots.
        /// </summary>
        [Test]
        public void V28_DoesNotAlsoFireV31ForTwoBodiesOnTheSameHierarchyPath()
        {
            RigAsset rig = CreateValidRig();
            AddPathBody(rig, "Hips", HipsPath);
            AddPathBody(rig, "Hips Again", HipsPath);
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V28, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // V29 (V-R4): box size must be positive on every axis.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V29_FiresWhenABoxSizeComponentIsZero()
        {
            RigAsset rig = CreateValidRig();
            RagdollBodyDefinition bodyDefinition = AddTargetBody(rig, "Torso", TorsoTargetId);
            bodyDefinition.boxSize = new float3(1f, 0f, 1f);
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V29, ValidationSeverity.Error);
        }

        [Test]
        public void V29_FiresWhenABoxSizeComponentIsNegative()
        {
            RigAsset rig = CreateValidRig();
            RagdollBodyDefinition bodyDefinition = AddTargetBody(rig, "Torso", TorsoTargetId);
            bodyDefinition.boxSize = new float3(-0.5f, 1f, 1f);
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V29, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // V30 (V-R5): joint limits.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V30_FiresWhenTheHingeMinimumExceedsTheMaximum()
        {
            RigAsset rig = CreateValidRig();
            RagdollBodyDefinition bodyDefinition = AddTargetBody(rig, "Torso", TorsoTargetId);
            bodyDefinition.limitMinDegrees = 10f;
            bodyDefinition.limitMaxDegrees = -10f;
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V30, ValidationSeverity.Error);
        }

        [Test]
        public void V30_FiresWhenAHingeBoundLeavesNegative180To180()
        {
            RigAsset rig = CreateValidRig();
            RagdollBodyDefinition bodyDefinition = AddTargetBody(rig, "Torso", TorsoTargetId);
            bodyDefinition.limitMinDegrees = -45f;
            bodyDefinition.limitMaxDegrees = 200f;
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V30, ValidationSeverity.Error);
        }

        [Test]
        public void V30_FiresWhenTheSwingLimitLeavesZeroTo180()
        {
            RigAsset rig = CreateValidRig();
            RagdollBodyDefinition bodyDefinition = AddTargetBody(rig, "Torso", TorsoTargetId);
            bodyDefinition.swingLimitDegrees = -1f;
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V30, ValidationSeverity.Error);
        }

        [Test]
        public void V30_FiresWhenTheTwistLimitLeavesZeroTo180()
        {
            RigAsset rig = CreateValidRig();
            RagdollBodyDefinition bodyDefinition = AddTargetBody(rig, "Torso", TorsoTargetId);
            bodyDefinition.twistLimitDegrees = 181f;
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V30, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // V31 (V-R6, Warning): the body graph must be a single tree.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V31_DoesNotFireForASingleHierarchyPathBody()
        {
            RigAsset rig = CreateValidRig();
            AddPathBody(rig, "Hips", HipsPath);
            rig.EnsureStableIds();

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "One body has nothing to disagree with about being the root.");
        }

        [Test]
        public void V31_DoesNotFireWhenOneHierarchyPathBodyIsTheAncestorOfAnother()
        {
            RigAsset rig = CreateValidRig();
            AddPathBody(rig, "Hips", HipsPath);
            AddPathBody(rig, "Spine", SpinePath);
            rig.EnsureStableIds();

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "'Root/Hips/Spine' descends from 'Root/Hips'; this is a single two-body chain.");
        }

        [Test]
        public void V31_FiresWhenTwoHierarchyPathBodiesShareNoAncestry()
        {
            RigAsset rig = CreateValidRig();
            AddPathBody(rig, "Hips", HipsPath);
            AddPathBody(rig, "Other Hips", OtherLegPath);
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V31, ValidationSeverity.Warning);
        }

        [Test]
        public void V31_TreatsAnEmptyPathAsTheRootAncestorOfEveryOtherPath()
        {
            RigAsset rig = CreateValidRig();
            AddPathBody(rig, "Actor Root", string.Empty);
            AddPathBody(rig, "Hips", HipsPath);
            AddPathBody(rig, "Spine", SpinePath);
            rig.EnsureStableIds();

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "An empty path addresses the prefab root, which is a real, single ancestor of " +
                "everything else - the whole-actor case must not read as multiple disconnected " +
                "trees.");
        }

        /// <summary>
        /// Pins the documented scope of V31: a target- or bone-addressed body's true position is
        /// unknown to the rig asset, so it must not be counted as a second, disconnected root.
        /// </summary>
        [Test]
        public void V31_DoesNotCountTargetOrBoneAddressedBodiesTowardTheTree()
        {
            RigAsset rig = CreateValidRig();
            AddPathBody(rig, "Hips", HipsPath);
            AddPathBody(rig, "Spine", SpinePath);
            AddTargetBody(rig, "Hand", HandTargetId);
            AddBoneBody(rig, "Forearm", ForearmBoneName);
            rig.EnsureStableIds();

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "The hierarchy-path pair is a single valid chain; the target- and bone-addressed " +
                "bodies have unknown position at authoring time and must be excluded from the " +
                "count rather than treated as extra, disconnected roots.");
        }

        /// <summary>
        /// The mirror image of the previous test: with fewer than two path-comparable bodies, V31
        /// has nothing to check and must not fire even though the rig's true hierarchy (unknowable
        /// here) might in fact be disconnected.
        /// </summary>
        [Test]
        public void V31_DoesNotFireWhenFewerThanTwoBodiesAreHierarchyPathAddressed()
        {
            RigAsset rig = CreateValidRig();
            AddTargetBody(rig, "Torso", TorsoTargetId);
            AddBoneBody(rig, "Forearm", ForearmBoneName);
            rig.EnsureStableIds();

            AssertNoFindings(
                ClipValidation.ValidateRig(rig),
                "Fully target- and bone-addressed rigs never trip V31 at authoring time; the fully " +
                "accurate check needs the real prefab (Phase D3).");
        }

        // -----------------------------------------------------------------------------------
        // V32 (V-R7): mass must be positive.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V32_FiresWhenMassIsZero()
        {
            RigAsset rig = CreateValidRig();
            RagdollBodyDefinition bodyDefinition = AddTargetBody(rig, "Torso", TorsoTargetId);
            bodyDefinition.mass = 0f;
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V32, ValidationSeverity.Error);
        }

        [Test]
        public void V32_FiresWhenMassIsNegative()
        {
            RigAsset rig = CreateValidRig();
            RagdollBodyDefinition bodyDefinition = AddTargetBody(rig, "Torso", TorsoTargetId);
            bodyDefinition.mass = -2f;
            rig.EnsureStableIds();

            AssertOnlyCode(
                ClipValidation.ValidateRig(rig), ValidationCode.V32, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // Defaults.
        // -----------------------------------------------------------------------------------

        [Test]
        public void ANewRagdollBodyDefinition_DefaultsBothDampingFieldsToTheInheritSentinel()
        {
            RagdollBodyDefinition bodyDefinition = new RagdollBodyDefinition();

            Assert.AreEqual(-1f, bodyDefinition.linearDamping, "Damping defaults to \"inherit\".");
            Assert.AreEqual(-1f, bodyDefinition.angularDamping, "Damping defaults to \"inherit\".");
        }

        [Test]
        public void ANewRagdollBodyDefinition_DefaultsToCollidingWithWorldAndAllSelfGroups()
        {
            RagdollBodyDefinition bodyDefinition = new RagdollBodyDefinition();

            Assert.IsTrue(bodyDefinition.collidesWithWorld, "Spec §3.3: default true.");
            Assert.AreEqual((byte)0xFF, bodyDefinition.selfCollidesWith, "Spec §3.3: default all.");
        }

        [Test]
        public void ANewRagdollBodyDefinition_HasAPositiveBoxSizeAndMass()
        {
            RagdollBodyDefinition bodyDefinition = new RagdollBodyDefinition();

            Assert.Greater(bodyDefinition.boxSize.x, 0f);
            Assert.Greater(bodyDefinition.boxSize.y, 0f);
            Assert.Greater(bodyDefinition.boxSize.z, 0f);
            Assert.Greater(bodyDefinition.mass, 0f);
        }

        [Test]
        public void RagdollRigSettingsDefault_UsesPlanar2DAndTheSpecsSolverDefaults()
        {
            RagdollRigSettings defaultSettings = RagdollRigSettings.Default;

            Assert.AreEqual(RagdollSpace.Planar2D, defaultSettings.space);
            Assert.AreEqual((byte)6, defaultSettings.solverIterations, "Spec §3.2: default 6.");
            Assert.AreEqual(120f, defaultSettings.substepHz, "Spec §3.2: default 120.");
        }

        [Test]
        public void ANewRig_StartsWithTheDefaultRagdollSettings()
        {
            RigAsset rig = assets.Create<RigAsset>("FreshRig");

            Assert.AreEqual(RagdollSpace.Planar2D, rig.ragdollSettings.space);
        }

        // -----------------------------------------------------------------------------------
        // EnsureStableIds.
        // -----------------------------------------------------------------------------------

        [Test]
        public void EnsureStableIdsMintsAnIdForEveryRagdollBody()
        {
            RigAsset rig = CreateValidRig();
            AddTargetBody(rig, "Torso", TorsoTargetId);
            AddPathBody(rig, "Hips", HipsPath);

            rig.EnsureStableIds();

            Assert.IsTrue(rig.ragdollBodies[0].Id.IsValid, "A body built from code must be identified.");
            Assert.IsTrue(rig.ragdollBodies[1].Id.IsValid, "A body built from code must be identified.");
            Assert.AreNotEqual(
                rig.ragdollBodies[0].Id,
                rig.ragdollBodies[1].Id,
                "Ids are what the runtime buffer element binds to, so two bodies may never share one.");
        }

        [Test]
        public void EnsureStableIdsLeavesAnAlreadyIdentifiedBodyAlone()
        {
            RigAsset rig = CreateValidRig();
            AddTargetBody(rig, "Torso", TorsoTargetId);
            rig.EnsureStableIds();
            RagdollBodyId mintedId = rig.ragdollBodies[0].Id;

            rig.EnsureStableIds();

            Assert.AreEqual(
                mintedId,
                rig.ragdollBodies[0].Id,
                "The assignment is idempotent; re-minting would orphan every runtime reference to it.");
        }

        /// <summary>
        /// The lists are identified independently of one another - EnsureStableIds' documented
        /// contract, now covering a third list. Before this guard, an early return on the first
        /// null list would have made a rig with no billboard roots ship ragdoll bodies that nothing
        /// could ever address.
        /// </summary>
        [Test]
        public void EnsureStableIdsIdentifiesRagdollBodiesEvenWhenTheRigHasNoBillboardRootList()
        {
            RigAsset rig = CreateValidRig();
            AddTargetBody(rig, "Torso", TorsoTargetId);
            rig.billboardRoots = null;

            rig.EnsureStableIds();

            Assert.IsTrue(
                rig.ragdollBodies[0].Id.IsValid,
                "A missing billboard-root list must not stop the ragdoll bodies being identified.");
        }

        [Test]
        public void EnsureStableIdsIdentifiesEarlierListsEvenWhenTheRigHasNoRagdollBodyList()
        {
            RigAsset rig = CreateValidRig();
            BillboardRootDefinition rootDefinition = new BillboardRootDefinition
            {
                displayName = "Torso",
                address = new RigNodeAddress { kind = RigNodeAddressKind.RigTarget, targetId = TorsoTargetId }
            };
            rig.billboardRoots.Add(rootDefinition);
            rig.ragdollBodies = null;

            rig.EnsureStableIds();

            Assert.IsTrue(
                rig.billboardRoots[0].Id.IsValid,
                "A missing ragdoll-body list must not stop earlier lists being identified.");
        }

        // -----------------------------------------------------------------------------------
        // Assertion helpers. Copies of BillboardRigAuthoringTests', so this fixture stands alone.
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
