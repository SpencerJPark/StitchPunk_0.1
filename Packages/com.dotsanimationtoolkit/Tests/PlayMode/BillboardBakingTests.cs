// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// What amendment A44 actually bakes: the actor's billboard-root buffer, its depth ordering, and
    /// the <see cref="BillboardMember"/> each part inherits.
    /// </summary>
    public sealed class BillboardBakingTests
    {
        private ActorBakeFixture fixture;
        private BakingTestWorld bakingWorld;

        [SetUp]
        public void SetUp()
        {
            fixture = new ActorBakeFixture();
            bakingWorld = new BakingTestWorld("BillboardBakingTests");
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
            return fixture.CreateClipSet("Set", rig, 0x4400UL);
        }

        private static BillboardRootDefinition AddTargetRoot(
            RigAsset rig, string displayName, uint targetId, uint rootId)
        {
            BillboardRootDefinition definition = new BillboardRootDefinition
            {
                displayName = displayName,
                stableId = rootId,
                address = new BillboardNodeAddress
                {
                    kind = BillboardAddressKind.RigTarget,
                    targetId = targetId
                }
            };
            rig.billboardRoots.Add(definition);
            return definition;
        }

        private static BillboardRootDefinition AddPathRoot(
            RigAsset rig, string displayName, string path, uint rootId)
        {
            BillboardRootDefinition definition = new BillboardRootDefinition
            {
                displayName = displayName,
                stableId = rootId,
                address = new BillboardNodeAddress
                {
                    kind = BillboardAddressKind.HierarchyPath,
                    hierarchyPath = path
                }
            };
            rig.billboardRoots.Add(definition);
            return definition;
        }

        // -----------------------------------------------------------------------------------
        // Opt-in.
        // -----------------------------------------------------------------------------------

        [Test]
        public void AnActorWhoseRigDeclaresNoRoots_BakesNoBillboardBufferAtAll()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            Assert.IsFalse(
                bakingWorld.EntityManager.HasBuffer<BillboardRootElement>(actorEntity),
                "Billboarding is opt-in; a rig that never billboards must cost nothing.");
            Assert.IsFalse(
                bakingWorld.EntityManager.HasComponent<BillboardMember>(
                    bakingWorld.GetPrimaryEntity(ActorBakeFixture.FindPart(actorGameObject, "Torso"))),
                "A part under no root carries no membership either.");
        }

        // -----------------------------------------------------------------------------------
        // The buffer.
        // -----------------------------------------------------------------------------------

        [Test]
        public void EachDeclaredRootBecomesABufferElementNamingItsNodeEntity()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddTargetRoot(rig, "Torso", ActorBakeFixture.TorsoTargetId, 100u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            DynamicBuffer<BillboardRootElement> rootElements =
                bakingWorld.EntityManager.GetBuffer<BillboardRootElement>(actorEntity);

            Assert.AreEqual(1, rootElements.Length);
            Assert.AreEqual(100u, rootElements[0].rootId);
            Assert.AreEqual(
                bakingWorld.GetPrimaryEntity(ActorBakeFixture.FindPart(actorGameObject, "Torso")),
                rootElements[0].node);
        }

        /// <summary>
        /// The runtime resolves the buffer in order and reads each node's rest orientation through
        /// live transforms, so an outer root must already have written its result before a nested
        /// one reads through it. Reorder this buffer and nested billboards turn twice.
        /// </summary>
        [Test]
        public void TheBufferIsOrderedShallowestFirst_WhateverOrderTheRigAuthoredThemIn()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddTargetRoot(rig, "Left Arm", ActorBakeFixture.LeftArmTargetId, 300u);
            AddPathRoot(rig, "Whole Actor", string.Empty, 100u);
            AddTargetRoot(rig, "Torso", ActorBakeFixture.TorsoTargetId, 200u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            DynamicBuffer<BillboardRootElement> rootElements =
                bakingWorld.EntityManager.GetBuffer<BillboardRootElement>(
                    bakingWorld.GetPrimaryEntity(actorGameObject));

            Assert.AreEqual(3, rootElements.Length);
            Assert.AreEqual(100u, rootElements[0].rootId, "The actor root is depth 0.");
            Assert.AreEqual(200u, rootElements[1].rootId, "The torso is depth 1.");
            Assert.AreEqual(300u, rootElements[2].rootId, "The left arm is depth 2.");
        }

        [Test]
        public void AuthoredDegreesBecomeRadians_AndTheArcIsHalved()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            BillboardRootDefinition definition =
                AddTargetRoot(rig, "Torso", ActorBakeFixture.TorsoTargetId, 100u);
            definition.angleOffsetDegrees = 30f;
            definition.clampEnabled = true;
            definition.clampArcDegrees = 90f;
            definition.snapEnabled = true;
            definition.snapSteps = 16;
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);

            BillboardSettings settings = bakingWorld.EntityManager
                .GetBuffer<BillboardRootElement>(bakingWorld.GetPrimaryEntity(actorGameObject))[0]
                .settings;

            Assert.AreEqual(math.radians(30f), settings.angleOffsetRadians, 1e-5f);
            Assert.AreEqual(
                math.radians(45f),
                settings.clampHalfArcRadians,
                1e-5f,
                "The arc is halved once at bake, because every use of it is symmetric about rest.");
            Assert.AreEqual(16, settings.snapSteps);
        }

        [Test]
        public void TheOptInBooleansBecomeSentinels_SoTheRuntimeCarriesOneFieldNotTwo()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            BillboardRootDefinition definition =
                AddTargetRoot(rig, "Torso", ActorBakeFixture.TorsoTargetId, 100u);
            definition.snapEnabled = false;
            definition.snapSteps = 8;
            definition.clampEnabled = false;
            definition.clampArcDegrees = 90f;
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);

            BillboardSettings settings = bakingWorld.EntityManager
                .GetBuffer<BillboardRootElement>(bakingWorld.GetPrimaryEntity(actorGameObject))[0]
                .settings;

            Assert.IsFalse(settings.SnapEnabled, "Snapping off must survive an authored step count.");
            Assert.IsFalse(settings.ClampEnabled, "Clamping off must survive an authored arc.");
        }

        // -----------------------------------------------------------------------------------
        // Membership.
        // -----------------------------------------------------------------------------------

        [Test]
        public void APartInheritsItsNearestAncestorRoot()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddPathRoot(rig, "Whole Actor", string.Empty, 100u);
            AddTargetRoot(rig, "Torso", ActorBakeFixture.TorsoTargetId, 200u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);

            // LeftArm sits under Torso, which sits under the actor. The torso is nearer.
            Entity leftArmEntity = bakingWorld.GetPrimaryEntity(
                ActorBakeFixture.FindPart(actorGameObject, "LeftArm"));
            Entity headEntity = bakingWorld.GetPrimaryEntity(
                ActorBakeFixture.FindPart(actorGameObject, "Head"));

            Assert.AreEqual(
                200u,
                bakingWorld.EntityManager.GetComponentData<BillboardMember>(leftArmEntity).rootId);
            Assert.AreEqual(
                100u,
                bakingWorld.EntityManager.GetComponentData<BillboardMember>(headEntity).rootId,
                "The head is not under the torso, so it still billboards with the actor.");
        }

        [Test]
        public void APartThatIsItselfARoot_NamesItselfRatherThanItsAncestor()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddPathRoot(rig, "Whole Actor", string.Empty, 100u);
            AddTargetRoot(rig, "Torso", ActorBakeFixture.TorsoTargetId, 200u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);

            Entity torsoEntity = bakingWorld.GetPrimaryEntity(
                ActorBakeFixture.FindPart(actorGameObject, "Torso"));

            Assert.AreEqual(
                200u,
                bakingWorld.EntityManager.GetComponentData<BillboardMember>(torsoEntity).rootId,
                "The nearest-ancestor walk is inclusive of the node itself — the override rule.");
        }

        [Test]
        public void AMemberPointsAtItsOwnActorRoot()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddPathRoot(rig, "Whole Actor", string.Empty, 100u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            Entity headEntity = bakingWorld.GetPrimaryEntity(
                ActorBakeFixture.FindPart(actorGameObject, "Head"));

            Assert.AreEqual(
                actorEntity,
                bakingWorld.EntityManager.GetComponentData<BillboardMember>(headEntity).actorRoot);
        }

        // -----------------------------------------------------------------------------------
        // The A41 checkbox, kept working.
        // -----------------------------------------------------------------------------------

        [Test]
        public void TheActorBillboardCheckbox_BecomesOneRootOnTheActorItself()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);
            actorGameObject.GetComponent<ActorAuthoring>().billboardMode = BillboardMode.ScreenAligned;

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            DynamicBuffer<BillboardRootElement> rootElements =
                bakingWorld.EntityManager.GetBuffer<BillboardRootElement>(actorEntity);

            Assert.AreEqual(1, rootElements.Length);
            Assert.AreEqual(actorEntity, rootElements[0].node);
            Assert.AreEqual(BillboardMode.ScreenAligned, rootElements[0].settings.mode);
            Assert.AreEqual(
                0u,
                rootElements[0].rootId,
                "The implicit root carries the reserved id 0, which no authored root can mint.");
            Assert.AreEqual(
                0u,
                bakingWorld.EntityManager
                    .GetComponentData<BillboardMember>(
                        bakingWorld.GetPrimaryEntity(
                            ActorBakeFixture.FindPart(actorGameObject, "Head")))
                    .rootId,
                "Every part inherits the implicit root, which carries the reserved id 0.");
        }

        /// <summary>
        /// The rig is the shared, authoritative place and the component is a convenience, so an
        /// explicit rig row must win rather than producing two roots on one node.
        /// </summary>
        [Test]
        public void AnExplicitRigRootForTheActorRoot_OutranksTheCheckbox()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddPathRoot(rig, "Whole Actor", string.Empty, 100u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);
            actorGameObject.GetComponent<ActorAuthoring>().billboardMode = BillboardMode.Upright;

            bakingWorld.Bake(actorGameObject);

            DynamicBuffer<BillboardRootElement> rootElements =
                bakingWorld.EntityManager.GetBuffer<BillboardRootElement>(
                    bakingWorld.GetPrimaryEntity(actorGameObject));

            Assert.AreEqual(1, rootElements.Length, "One node, one root.");
            Assert.AreEqual(100u, rootElements[0].rootId, "The rig row wins.");
        }

        // -----------------------------------------------------------------------------------
        // Validation rule V21's other half.
        // -----------------------------------------------------------------------------------

        [Test]
        public void AnUnresolvablePathAddressIsReportedAtBake()
        {
            ClipSetAsset clipSet = CreateClipSet(out RigAsset rig);
            AddPathRoot(rig, "Nowhere", "Torso/DoesNotExist", 100u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.ExpectToolkitErrors(1);
            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            Assert.IsFalse(
                bakingWorld.EntityManager.HasBuffer<BillboardRootElement>(
                    bakingWorld.GetPrimaryEntity(actorGameObject)),
                "The only root did not resolve, so there is nothing to bake — but the user was told.");
        }
    }
}
