// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// What Phase D3 (amendment A50) actually bakes: the actor's <see cref="RagdollBody"/> and
    /// <see cref="RagdollRestPose"/> buffers, their depth ordering, each body's resolved
    /// <c>parentBodyIndex</c>, and the bake-time conversions (degrees to radians, mass to inverse
    /// mass, the −1 damping sentinel) that fill in <see cref="RagdollBodyParams"/>.
    /// </summary>
    public sealed class RagdollBakingTests
    {
        private ActorBakeFixture fixture;
        private BakingTestWorld bakingWorld;

        [SetUp]
        public void SetUp()
        {
            fixture = new ActorBakeFixture();
            bakingWorld = new BakingTestWorld("RagdollBakingTests");
        }

        [TearDown]
        public void TearDown()
        {
            bakingWorld.Dispose();
            fixture.DestroyAll();
        }

        private ClipSetAsset CreateClipSet(out RigAsset rig)
        {
            rig = fixture.CreateRig("Rig");
            return fixture.CreateClipSet("Set", rig, 0x4500UL);
        }

        private static RagdollBodyDefinition AddTargetBody(
            RigAsset rig, string displayName, uint targetId, uint bodyId)
        {
            RagdollBodyDefinition definition = new RagdollBodyDefinition
            {
                displayName = displayName,
                stableId = bodyId,
                address = new RigNodeAddress
                {
                    kind = RigNodeAddressKind.RigTarget,
                    targetId = targetId
                }
            };
            rig.ragdollBodies.Add(definition);
            return definition;
        }

        private static RagdollBodyDefinition AddPathBody(
            RigAsset rig, string displayName, string path, uint bodyId)
        {
            RagdollBodyDefinition definition = new RagdollBodyDefinition
            {
                displayName = displayName,
                stableId = bodyId,
                address = new RigNodeAddress
                {
                    kind = RigNodeAddressKind.HierarchyPath,
                    hierarchyPath = path
                }
            };
            rig.ragdollBodies.Add(definition);
            return definition;
        }

        private static RagdollBodyDefinition AddBoneBody(
            RigAsset rig, string displayName, string boneName, uint bodyId)
        {
            RagdollBodyDefinition definition = new RagdollBodyDefinition
            {
                displayName = displayName,
                stableId = bodyId,
                address = new RigNodeAddress
                {
                    kind = RigNodeAddressKind.Bone,
                    boneName = boneName
                }
            };
            rig.ragdollBodies.Add(definition);
            return definition;
        }

        private void AssertToolkitWarnings(int expectedCount, string expectedFragment)
        {
            IReadOnlyList<string> recordedWarnings = bakingWorld.ToolkitWarnings;
            Assert.AreEqual(
                expectedCount,
                recordedWarnings.Count,
                "Expected exactly " + expectedCount + " toolkit warning(s), got: " +
                string.Join(" | ", recordedWarnings));
            if (expectedFragment == null)
            {
                return;
            }
            bool matched = false;
            for (int warningIndex = 0; warningIndex < recordedWarnings.Count; warningIndex++)
            {
                if (recordedWarnings[warningIndex].Contains(expectedFragment))
                {
                    matched = true;
                    break;
                }
            }
            Assert.IsTrue(
                matched,
                "No toolkit warning mentioned '" + expectedFragment + "'. Got: " +
                string.Join(" | ", recordedWarnings));
        }

        /// <summary>
        /// <see cref="RagdollRestPose"/>'s own half of <c>DataContractTests</c>' G6 inventory
        /// (spec §5.3). Asserted here rather than there because its two fields are
        /// <c>Unity.Transforms</c> types, and the EditMode test assembly deliberately does not
        /// reference <c>Unity.Transforms</c> (amendment A17, architecture §1.3) — this PlayMode
        /// assembly already does, for the same baking-pipeline reasons that keep it here.
        /// </summary>
        [Test]
        public void RagdollRestPoseFields_MatchTheSection53Inventory()
        {
            FieldInfo[] fields = typeof(RagdollRestPose).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Assert.AreEqual(2, fields.Length, "RagdollRestPose must declare exactly its two contracted fields.");

            FieldInfo localTransformField = typeof(RagdollRestPose).GetField("localTransform");
            Assert.IsNotNull(localTransformField);
            Assert.AreEqual(typeof(LocalTransform), localTransformField.FieldType);

            FieldInfo postTransformMatrixField = typeof(RagdollRestPose).GetField("postTransformMatrix");
            Assert.IsNotNull(postTransformMatrixField);
            Assert.AreEqual(typeof(PostTransformMatrix), postTransformMatrixField.FieldType);
        }

        // -----------------------------------------------------------------------------------
        // Opt-in.
        // -----------------------------------------------------------------------------------

        [Test]
        public void AnActorWhoseRigDeclaresNoBodies_BakesNoRagdollComponentsAtAll()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            Assert.IsFalse(
                bakingWorld.EntityManager.HasComponent<RagdollActor>(actorEntity),
                "Ragdolling is opt-in; a rig that never ragdolls must cost nothing.");
            Assert.IsFalse(bakingWorld.EntityManager.HasBuffer<RagdollBody>(actorEntity));
            Assert.IsFalse(bakingWorld.EntityManager.HasBuffer<RagdollRestPose>(actorEntity));
            Assert.IsFalse(bakingWorld.EntityManager.HasComponent<RagdollState>(actorEntity));
            Assert.IsFalse(bakingWorld.EntityManager.HasBuffer<RagdollWorldContact>(actorEntity));
        }

        // -----------------------------------------------------------------------------------
        // The buffers.
        // -----------------------------------------------------------------------------------

        [Test]
        public void EachDeclaredBodyBecomesABufferElementNamingItsNodeEntity()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddTargetBody(rig, "Torso", ActorBakeFixture.TorsoTargetId, 100u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);

            Assert.IsTrue(bakingWorld.EntityManager.HasComponent<RagdollActor>(actorEntity));
            Assert.IsFalse(
                bakingWorld.EntityManager.IsComponentEnabled<RagdollActor>(actorEntity),
                "RagdollActor is baked disabled — the animation drives the pose until a host says otherwise.");

            DynamicBuffer<RagdollBody> bodyElements =
                bakingWorld.EntityManager.GetBuffer<RagdollBody>(actorEntity);
            Assert.AreEqual(1, bodyElements.Length);
            Assert.AreEqual(100u, bodyElements[0].bodyId.Value);
            Assert.AreEqual(
                bakingWorld.GetPrimaryEntity(ActorBakeFixture.FindPart(actorGameObject, "Torso")),
                bodyElements[0].node);
            Assert.AreEqual(-1, bodyElements[0].parentBodyIndex, "The only body has no ragdolled ancestor.");
            Assert.AreEqual(-1, bodyElements[0].parameters.parentBodyIndex, "The two copies must agree.");

            DynamicBuffer<RagdollRestPose> restPoseElements =
                bakingWorld.EntityManager.GetBuffer<RagdollRestPose>(actorEntity);
            Assert.AreEqual(
                bodyElements.Length,
                restPoseElements.Length,
                "The rest-pose buffer is baked parallel to the body buffer (structural-free capture).");

            Assert.IsTrue(bakingWorld.EntityManager.HasComponent<RagdollState>(actorEntity));
            RagdollState state = bakingWorld.EntityManager.GetComponentData<RagdollState>(actorEntity);
            Assert.AreEqual(
                RagdollStateFlags.CaptureNeeded,
                state.flags,
                "An actor baked disabled must capture the first time it is ever enabled.");

            Assert.IsTrue(bakingWorld.EntityManager.HasBuffer<RagdollWorldContact>(actorEntity));
            Assert.AreEqual(
                0,
                bakingWorld.EntityManager.GetBuffer<RagdollWorldContact>(actorEntity).Length);
        }

        /// <summary>
        /// The solver relies on a body's parent already being integrated by the time the child
        /// reads through it. Reorder this buffer and chains solve backwards (spec §5.2).
        /// </summary>
        [Test]
        public void TheBufferIsOrderedShallowestFirst_WhateverOrderTheRigAuthoredThemIn()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddTargetBody(rig, "Left Arm", ActorBakeFixture.LeftArmTargetId, 300u);
            AddPathBody(rig, "Whole Actor", string.Empty, 100u);
            AddTargetBody(rig, "Torso", ActorBakeFixture.TorsoTargetId, 200u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            DynamicBuffer<RagdollBody> bodyElements = bakingWorld.EntityManager.GetBuffer<RagdollBody>(
                bakingWorld.GetPrimaryEntity(actorGameObject));

            Assert.AreEqual(3, bodyElements.Length);
            Assert.AreEqual(100u, bodyElements[0].bodyId.Value, "The actor root is depth 0.");
            Assert.AreEqual(200u, bodyElements[1].bodyId.Value, "The torso is depth 1.");
            Assert.AreEqual(300u, bodyElements[2].bodyId.Value, "The left arm is depth 2.");
        }

        // -----------------------------------------------------------------------------------
        // Parentage.
        // -----------------------------------------------------------------------------------

        [Test]
        public void ParentBodyIndexResolvesToTheNearestRagdolledAncestor_AndTheRootIsMinusOne()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddPathBody(rig, "Whole Actor", string.Empty, 100u);
            AddTargetBody(rig, "Torso", ActorBakeFixture.TorsoTargetId, 200u);
            AddTargetBody(rig, "Left Arm", ActorBakeFixture.LeftArmTargetId, 300u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);

            DynamicBuffer<RagdollBody> bodyElements = bakingWorld.EntityManager.GetBuffer<RagdollBody>(
                bakingWorld.GetPrimaryEntity(actorGameObject));

            // Shallowest-first: index 0 = root, 1 = torso, 2 = left arm.
            Assert.AreEqual(-1, bodyElements[0].parentBodyIndex, "The root has no ragdolled ancestor.");
            Assert.AreEqual(0, bodyElements[1].parentBodyIndex, "The torso's nearest ancestor is the root body.");
            Assert.AreEqual(1, bodyElements[2].parentBodyIndex, "The left arm's nearest ancestor is the torso body.");

            Assert.IsTrue((bodyElements[0].parameters.flags & RagdollBodyFlags.IsRoot) != 0);
            Assert.IsFalse((bodyElements[1].parameters.flags & RagdollBodyFlags.IsRoot) != 0);
        }

        /// <summary>
        /// A body's implied parent is its nearest ragdolled ancestor, not its immediate transform
        /// parent — the torso here carries no body of its own, so the left arm's ancestor walk must
        /// skip past it to the root.
        /// </summary>
        [Test]
        public void ABodyWhoseImmediateParentIsNotRagdolled_FindsTheNearestAncestorThatIs()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddPathBody(rig, "Whole Actor", string.Empty, 100u);
            AddTargetBody(rig, "Left Arm", ActorBakeFixture.LeftArmTargetId, 300u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);

            DynamicBuffer<RagdollBody> bodyElements = bakingWorld.EntityManager.GetBuffer<RagdollBody>(
                bakingWorld.GetPrimaryEntity(actorGameObject));

            Assert.AreEqual(2, bodyElements.Length);
            Assert.AreEqual(-1, bodyElements[0].parentBodyIndex);
            Assert.AreEqual(
                0,
                bodyElements[1].parentBodyIndex,
                "The torso is not a body, so the left arm's nearest ragdolled ancestor is the root.");
        }

        [Test]
        public void MoreThanOneDisconnectedRoot_WarnsButStillBakesBoth()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            // Neither the torso nor the head has a ragdolled ancestor: the actor root itself is not
            // a body, so this rig resolves to two independent, disconnected articulations.
            AddTargetBody(rig, "Torso", ActorBakeFixture.TorsoTargetId, 100u);
            AddTargetBody(rig, "Head", ActorBakeFixture.HeadTargetId, 200u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();
            AssertToolkitWarnings(1, "disconnected");

            DynamicBuffer<RagdollBody> bodyElements = bakingWorld.EntityManager.GetBuffer<RagdollBody>(
                bakingWorld.GetPrimaryEntity(actorGameObject));
            Assert.AreEqual(2, bodyElements.Length, "A warning, not an error: both roots still bake.");
            Assert.AreEqual(-1, bodyElements[0].parentBodyIndex);
            Assert.AreEqual(-1, bodyElements[1].parentBodyIndex);
        }

        // -----------------------------------------------------------------------------------
        // Bake-time conversions.
        // -----------------------------------------------------------------------------------

        [Test]
        public void BoxSizeIsHalved_MassIsInverted_AndLimitsBecomeRadians()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            RagdollBodyDefinition definition = AddTargetBody(rig, "Torso", ActorBakeFixture.TorsoTargetId, 100u);
            definition.boxSize = new float3(2f, 4f, 6f);
            definition.mass = 2f;
            definition.limitMinDegrees = -30f;
            definition.limitMaxDegrees = 60f;
            definition.swingLimitDegrees = 20f;
            definition.twistLimitDegrees = 10f;
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);

            RagdollBodyParams bodyParams = bakingWorld.EntityManager
                .GetBuffer<RagdollBody>(bakingWorld.GetPrimaryEntity(actorGameObject))[0]
                .parameters;

            Assert.AreEqual(new float3(1f, 2f, 3f), bodyParams.boxHalfExtents);
            Assert.AreEqual(0.5f, bodyParams.invMass, 1e-6f, "Mass is inverted once at bake, never at runtime.");
            Assert.AreEqual(math.radians(-30f), bodyParams.limitMin, 1e-5f);
            Assert.AreEqual(math.radians(60f), bodyParams.limitMax, 1e-5f);
            Assert.AreEqual(math.radians(20f), bodyParams.swingLimit, 1e-5f);
            Assert.AreEqual(math.radians(10f), bodyParams.twistLimit, 1e-5f);
        }

        [Test]
        public void TheDampingSentinelIsResolvedAtBake_UsingTheRigDefaultWhenAuthoredNegative()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            RagdollRigSettings rigSettings = rig.ragdollSettings;
            rigSettings.defaultLinearDamping = 0.2f;
            rigSettings.defaultAngularDamping = 0.3f;
            rig.ragdollSettings = rigSettings;

            RagdollBodyDefinition inheriting = AddTargetBody(rig, "Torso", ActorBakeFixture.TorsoTargetId, 100u);
            inheriting.linearDamping = -1f;
            inheriting.angularDamping = -1f;

            RagdollBodyDefinition overriding = AddTargetBody(rig, "Left Arm", ActorBakeFixture.LeftArmTargetId, 200u);
            overriding.linearDamping = 0.9f;
            overriding.angularDamping = 0.8f;

            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);

            DynamicBuffer<RagdollBody> bodyElements = bakingWorld.EntityManager.GetBuffer<RagdollBody>(
                bakingWorld.GetPrimaryEntity(actorGameObject));

            RagdollBodyParams inheritingParams = FindByBodyId(bodyElements, 100u).parameters;
            Assert.AreEqual(0.2f, inheritingParams.linearDamping, 1e-6f);
            Assert.AreEqual(0.3f, inheritingParams.angularDamping, 1e-6f);

            RagdollBodyParams overridingParams = FindByBodyId(bodyElements, 200u).parameters;
            Assert.AreEqual(0.9f, overridingParams.linearDamping, 1e-6f);
            Assert.AreEqual(0.8f, overridingParams.angularDamping, 1e-6f);
        }

        [Test]
        public void ParentAnchorOffsetAndRestRelativeRotation_AreMeasuredAgainstTheParentBody()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            RagdollBodyDefinition rootBody = AddPathBody(rig, "Whole Actor", string.Empty, 100u);
            rootBody.boxCenter = new float3(0f, 0.5f, 0f);
            AddTargetBody(rig, "Torso", ActorBakeFixture.TorsoTargetId, 200u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);

            DynamicBuffer<RagdollBody> bodyElements = bakingWorld.EntityManager.GetBuffer<RagdollBody>(
                bakingWorld.GetPrimaryEntity(actorGameObject));

            RagdollBodyParams torsoParams = FindByBodyId(bodyElements, 200u).parameters;

            // Torso sits at ActorBakeFixture.TorsoLocalPosition relative to the (unrotated) actor
            // root, whose own centre of mass is offset by its authored boxCenter.
            float3 expectedOffset = (float3)ActorBakeFixture.TorsoLocalPosition - rootBody.boxCenter;
            Assert.AreEqual(expectedOffset.x, torsoParams.parentAnchorOffset.x, 1e-4f);
            Assert.AreEqual(expectedOffset.y, torsoParams.parentAnchorOffset.y, 1e-4f);
            Assert.AreEqual(expectedOffset.z, torsoParams.parentAnchorOffset.z, 1e-4f);

            quaternion restRelativeRotation = torsoParams.restRelativeRotation;
            Assert.AreEqual(
                1f,
                math.abs(restRelativeRotation.value.w),
                1e-5f,
                "Neither body is authored with a rest rotation, so the relative rotation is identity.");
            Assert.AreEqual(0f, math.length(restRelativeRotation.value.xyz), 1e-5f);
        }

        // -----------------------------------------------------------------------------------
        // RagdollRigConfig — a D4 finding, not part of spec §5's original table (see that
        // component's own remarks): the rig-wide solver knobs RagdollSolveSystem needs every step
        // have to be baked somewhere, and nothing else in §5 carries them.
        // -----------------------------------------------------------------------------------

        [Test]
        public void TheRigsSolverSettings_AreBakedIntoRagdollRigConfig()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddTargetBody(rig, "Torso", ActorBakeFixture.TorsoTargetId, 100u);

            RagdollRigSettings rigSettings = rig.ragdollSettings;
            rigSettings.space = RagdollSpace.Spatial3D;
            rigSettings.gravityScale = 2f;
            rigSettings.jointStiffness = 0.75f;
            rigSettings.jointDamping = 0.25f;
            rigSettings.solverIterations = 8;
            rigSettings.substepHz = 60f;
            rig.ragdollSettings = rigSettings;

            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            Assert.IsTrue(bakingWorld.EntityManager.HasComponent<RagdollRigConfig>(actorEntity));

            RagdollRigConfig bakedConfig = bakingWorld.EntityManager.GetComponentData<RagdollRigConfig>(actorEntity);
            Assert.AreEqual(RagdollSpace.Spatial3D, bakedConfig.space);
            Assert.AreEqual(2f, bakedConfig.gravityScale, 1e-6f);
            Assert.AreEqual(0.75f, bakedConfig.jointStiffness, 1e-6f);
            Assert.AreEqual(0.25f, bakedConfig.jointDamping, 1e-6f);
            Assert.AreEqual(8, bakedConfig.solverIterations);
            Assert.AreEqual(
                1f / 60f, bakedConfig.substepDeltaTime, 1e-6f,
                "substepHz is inverted once at bake, the same convention RagdollSolverSettings.substepDeltaTime itself uses.");
        }

        [Test]
        public void ARagdollWithNoBodies_BakesNoRagdollRigConfigEither()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            Assert.IsFalse(
                bakingWorld.EntityManager.HasComponent<RagdollRigConfig>(
                    bakingWorld.GetPrimaryEntity(actorGameObject)),
                "RagdollRigConfig is part of the same opt-in bundle as every other ragdoll component.");
        }

        // -----------------------------------------------------------------------------------
        // Validation rule V26's runtime half.
        // -----------------------------------------------------------------------------------

        [Test]
        public void AnUnresolvableAddressIsReportedAtBake_AndTheBodyIsExcluded()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddPathBody(rig, "Nowhere", "Torso/DoesNotExist", 100u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.ExpectToolkitErrors(1);
            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            Assert.IsFalse(
                bakingWorld.EntityManager.HasComponent<RagdollActor>(
                    bakingWorld.GetPrimaryEntity(actorGameObject)),
                "The only body did not resolve, so there is nothing to bake — but the user was told.");
        }

        [Test]
        public void ASkinnedBoneAddress_NeverResolvesAtRuntimeBake_AndIsNotAnError()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddBoneBody(rig, "Spine", "Spine1", 100u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();
            AssertToolkitWarnings(0, null);

            Assert.IsFalse(
                bakingWorld.EntityManager.HasComponent<RagdollActor>(
                    bakingWorld.GetPrimaryEntity(actorGameObject)),
                "A bone-only rig authors and editor-previews but never gains a runtime node — this " +
                "is the documented D1 limitation, not a mistake, so it bakes no ragdoll components " +
                "and reports neither an error nor a warning.");
        }

        [Test]
        public void ASkinnedBoneBody_AlongsideARuntimeBody_StillBakesTheRuntimeBodyOnly()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddTargetBody(rig, "Torso", ActorBakeFixture.TorsoTargetId, 100u);
            AddBoneBody(rig, "Spine", "Spine1", 200u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            DynamicBuffer<RagdollBody> bodyElements = bakingWorld.EntityManager.GetBuffer<RagdollBody>(
                bakingWorld.GetPrimaryEntity(actorGameObject));
            Assert.AreEqual(1, bodyElements.Length, "Only the runtime-resolvable body reaches the buffer.");
            Assert.AreEqual(100u, bodyElements[0].bodyId.Value);
        }

        private static RagdollBody FindByBodyId(DynamicBuffer<RagdollBody> bodyElements, uint bodyId)
        {
            for (int bodyIndex = 0; bodyIndex < bodyElements.Length; bodyIndex++)
            {
                if (bodyElements[bodyIndex].bodyId.Value == bodyId)
                {
                    return bodyElements[bodyIndex];
                }
            }
            Assert.Fail("No baked body carries id " + bodyId + ".");
            return default;
        }
    }
}
