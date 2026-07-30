// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Text.RegularExpressions;
using NUnit.Framework;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// The module M2 baking acceptance list (architecture section 8): baking an
    /// <see cref="ActorAuthoring"/> prefab must produce the section 5.2 archetypes exactly, resolve
    /// every part to its dense target index, share one blob between actors that share a clip set,
    /// survive a misconfigured part without failing the bake, and supply actor-space rest bounds.
    /// </summary>
    /// <remarks>
    /// These are the first tests in the package that run a real bake rather than calling pure
    /// functions, so they assert on entity data produced by Unity's baking pipeline via
    /// <see cref="BakingTestWorld"/>.
    /// </remarks>
    public sealed class ActorBakingAcceptanceTests
    {
        private const float Tolerance = 1e-4f;

        private ActorBakeFixture fixtureAssets;
        private BakingTestWorld bakingWorld;

        [SetUp]
        public void SetUp()
        {
            fixtureAssets = new ActorBakeFixture();
            bakingWorld = new BakingTestWorld("ActorBakingAcceptanceTests");
        }

        [TearDown]
        public void TearDown()
        {
            bakingWorld.Dispose();
            fixtureAssets.DestroyAll();
        }

        // -----------------------------------------------------------------------------------
        // "baking an ActorAuthoring prefab yields the section 5.2 root archetype exactly
        //  (assert component-by-component incl. enableable initial states)"
        // -----------------------------------------------------------------------------------

        [Test]
        public void BakingAnActor_ProducesTheSection52RootArchetype()
        {
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000501UL);
            GameObject actorGameObject = fixtureAssets.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            EntityManager entityManager = bakingWorld.EntityManager;

            Assert.IsTrue(entityManager.HasComponent<ClipRegistry>(actorEntity), "ClipRegistry.");
            Assert.IsTrue(entityManager.HasBuffer<PlaybackLayer>(actorEntity), "PlaybackLayer buffer.");
            Assert.IsTrue(entityManager.HasBuffer<AnimationCommand>(actorEntity), "AnimationCommand buffer.");
            Assert.IsTrue(entityManager.HasBuffer<AnimEventOutput>(actorEntity), "AnimEventOutput buffer.");
            Assert.IsTrue(entityManager.HasBuffer<RigPartRef>(actorEntity), "RigPartRef buffer.");
            Assert.IsTrue(entityManager.HasComponent<SampleSettings>(actorEntity), "SampleSettings.");
            Assert.IsTrue(entityManager.HasComponent<ActorRestBounds>(actorEntity), "ActorRestBounds.");
            Assert.IsTrue(entityManager.HasComponent<VatTextureBinding>(actorEntity), "VatTextureBinding.");

            Assert.IsTrue(
                entityManager.GetComponentData<ClipRegistry>(actorEntity).Value.IsCreated,
                "The registry blob must be created and owned by the BlobAssetStore.");

            // The enableable initial states are contractual: a wrong default here silently changes
            // first-frame behaviour rather than failing loudly.
            Assert.IsTrue(
                entityManager.IsComponentEnabled<RigBindingUninitialized>(actorEntity),
                "RigBindingUninitialized must be baked ENABLED so section 5.3 rebinds on first update.");
            Assert.IsTrue(
                entityManager.IsComponentEnabled<AnimVisible>(actorEntity),
                "AnimVisible must be baked ENABLED — an actor is visible until told otherwise.");
            Assert.IsTrue(
                entityManager.IsComponentEnabled<BoundsDirty>(actorEntity),
                "BoundsDirty must be baked ENABLED to guarantee a first-frame bounds write (section 5.8).");
            Assert.IsFalse(
                entityManager.IsComponentEnabled<AnimationCommandPending>(actorEntity),
                "AnimationCommandPending must be baked DISABLED — no commands are queued yet.");
            Assert.IsFalse(
                entityManager.IsComponentEnabled<AnimEventsPending>(actorEntity),
                "AnimEventsPending must be baked DISABLED — no events have been emitted yet.");
        }

        [Test]
        public void BakingAnActor_AddsAnimLodOnlyWhenTheAuthoringAsksForIt()
        {
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000502UL);
            GameObject withoutLod = fixtureAssets.CreateStandardActor("NoLod", clipSet, false);
            GameObject withLod = fixtureAssets.CreateStandardActor("WithLod", clipSet, true);

            bakingWorld.Bake(withoutLod, withLod);
            EntityManager entityManager = bakingWorld.EntityManager;

            Assert.IsFalse(
                entityManager.HasComponent<AnimLod>(bakingWorld.GetPrimaryEntity(withoutLod)),
                "AnimLod is opt-in; adding it unasked would enrol every actor in distance LOD.");
            Assert.IsTrue(
                entityManager.HasComponent<AnimLod>(bakingWorld.GetPrimaryEntity(withLod)),
                "An actor that asked for distance LOD must carry AnimLod.");
            Assert.AreEqual(
                0,
                entityManager.GetComponentData<AnimLod>(bakingWorld.GetPrimaryEntity(withLod)).level,
                "LOD starts at level 0 — full quality — until a system demotes it.");
        }

        [Test]
        public void BakingAnActor_SeedsTheStartingLayerItWasAuthoredWith()
        {
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000503UL);
            GameObject actorGameObject = fixtureAssets.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            DynamicBuffer<PlaybackLayer> layers =
                bakingWorld.EntityManager.GetBuffer<PlaybackLayer>(actorEntity);

            Assert.AreEqual(
                ActorBakeFixture.LayerCount,
                layers.Length,
                "The buffer must have one entry per rig layer, seeded or not.");
            Assert.AreEqual(
                ActorBakeFixture.WalkClipStableId,
                layers[0].clip.Value,
                "Layer 0 was authored with the walk clip.");
            Assert.AreEqual(
                ActorBakeFixture.WalkDenseClipIndex,
                layers[0].clipIndex,
                "The seeded clip must be resolved to its dense index at bake, not left unresolved.");
            Assert.AreNotEqual(
                0,
                (int)(layers[0].flags & PlaybackFlags.Active),
                "A seeded layer starts active.");
            Assert.AreEqual(
                0UL,
                layers[1].clip.Value,
                "An unseeded layer must stay empty rather than inherit its neighbour.");
        }

        // -----------------------------------------------------------------------------------
        // "part entities carry RigPartBinding with correct dense indices for a 3-target fixture rig"
        // -----------------------------------------------------------------------------------

        [Test]
        public void BakingAnActor_ResolvesEveryPartToItsDenseTargetIndex()
        {
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000504UL);
            GameObject actorGameObject = fixtureAssets.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            EntityManager entityManager = bakingWorld.EntityManager;
            DynamicBuffer<RigPartRef> partRefs = entityManager.GetBuffer<RigPartRef>(actorEntity);

            Assert.AreEqual(3, partRefs.Length, "Every declared part must be bound to the root.");

            // The fixture authors the parts out of id order on purpose, so a binding pass that
            // returned authoring positions instead of resolving ids would fail here.
            AssertPartBound(entityManager, partRefs, ActorBakeFixture.TorsoDenseIndex, actorEntity);
            AssertPartBound(entityManager, partRefs, ActorBakeFixture.LeftArmDenseIndex, actorEntity);
            AssertPartBound(entityManager, partRefs, ActorBakeFixture.HeadDenseIndex, actorEntity);
        }

        private static void AssertPartBound(
            EntityManager entityManager,
            DynamicBuffer<RigPartRef> partRefs,
            int expectedDenseIndex,
            Entity expectedActorRoot)
        {
            for (int partRefIndex = 0; partRefIndex < partRefs.Length; partRefIndex++)
            {
                if (partRefs[partRefIndex].targetIndex != expectedDenseIndex)
                {
                    continue;
                }
                Entity partEntity = partRefs[partRefIndex].part;
                Assert.AreNotEqual(Entity.Null, partEntity, "A bound part must reference a real entity.");
                Assert.IsTrue(
                    entityManager.HasComponent<RigPartBinding>(partEntity),
                    "A bound part must carry RigPartBinding.");

                RigPartBinding binding = entityManager.GetComponentData<RigPartBinding>(partEntity);
                Assert.AreEqual(
                    expectedDenseIndex,
                    binding.targetIndex,
                    "The part's own binding must agree with the root's buffer entry.");
                Assert.AreEqual(
                    expectedActorRoot,
                    binding.actorRoot,
                    "A part must point back at its actor root, or section 5.3 cannot rebind it.");
                Assert.IsTrue(
                    entityManager.HasComponent<TargetRestPose>(partEntity),
                    "A part must carry the rest pose captured from its authoring transform.");
                Assert.IsTrue(
                    entityManager.HasComponent<TargetPose>(partEntity),
                    "A part must carry the pose the sampler writes.");
                return;
            }
            Assert.Fail("No part was bound to dense target index " + expectedDenseIndex + ".");
        }

        // -----------------------------------------------------------------------------------
        // "two actors sharing a set share one blob (reference equality via content hash)"
        // -----------------------------------------------------------------------------------

        [Test]
        public void TwoActorsSharingAClipSet_ShareOneRegistryBlob()
        {
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000505UL);
            GameObject firstActor = fixtureAssets.CreateStandardActor("FirstActor", clipSet, false);
            GameObject secondActor = fixtureAssets.CreateStandardActor("SecondActor", clipSet, false);

            bakingWorld.Bake(firstActor, secondActor);
            EntityManager entityManager = bakingWorld.EntityManager;

            BlobAssetReference<ClipRegistryBlob> firstRegistry = entityManager
                .GetComponentData<ClipRegistry>(bakingWorld.GetPrimaryEntity(firstActor)).Value;
            BlobAssetReference<ClipRegistryBlob> secondRegistry = entityManager
                .GetComponentData<ClipRegistry>(bakingWorld.GetPrimaryEntity(secondActor)).Value;

            Assert.IsTrue(firstRegistry.IsCreated && secondRegistry.IsCreated, "Both blobs must exist.");
            Assert.AreEqual(
                firstRegistry,
                secondRegistry,
                "Two actors on one clip set must resolve to the SAME blob, not two equal copies — " +
                "that is the whole point of keying AddBlobAssetWithCustomHash on the content hash, " +
                "and duplicating it would multiply crowd memory by the actor count.");
        }

        [Test]
        public void TwoActorsOnDifferentClipSets_DoNotShareABlob()
        {
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset firstSet = fixtureAssets.CreateClipSet("FirstSet", rig, 0x0000000000000506UL);
            ClipSetAsset secondSet = fixtureAssets.CreateClipSet("SecondSet", rig, 0x0000000000000507UL);
            GameObject firstActor = fixtureAssets.CreateStandardActor("FirstActor", firstSet, false);
            GameObject secondActor = fixtureAssets.CreateStandardActor("SecondActor", secondSet, false);

            bakingWorld.Bake(firstActor, secondActor);
            EntityManager entityManager = bakingWorld.EntityManager;

            Assert.AreNotEqual(
                entityManager.GetComponentData<ClipRegistry>(
                    bakingWorld.GetPrimaryEntity(firstActor)).Value,
                entityManager.GetComponentData<ClipRegistry>(
                    bakingWorld.GetPrimaryEntity(secondActor)).Value,
                "Dedup is scoped to one set identity (section 4.5): distinct sets must never collapse " +
                "onto one blob, or editing one set would silently rewrite the other's baked data.");
        }

        // -----------------------------------------------------------------------------------
        // "ActorRestBounds ... a fixture rig with a part offset well away from the origin"
        // -----------------------------------------------------------------------------------

        [Test]
        public void BakingAnActor_ProducesActorSpaceRestBounds_ThatContainAFarOffsetPart()
        {
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000508UL);
            GameObject actorGameObject = fixtureAssets.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            AABB restBounds = bakingWorld.EntityManager
                .GetComponentData<ActorRestBounds>(bakingWorld.GetPrimaryEntity(actorGameObject)).value;

            // The head sits 12 units up with half-extents 0.4, so actor-space bounds must reach
            // 12.4. Offset-space bounds would place it at the origin and top out near 1.8 — a box
            // smaller than the actor's own silhouette, which is the cull-popping bug section 5.8
            // exists to close and the reason offsetBounds must never be used as actor space.
            Assert.GreaterOrEqual(
                restBounds.Max.y,
                12.4f - Tolerance,
                "Rest bounds must reach the head at y = 12. Falling short means the baker treated " +
                "offset space as actor space and never walked the transform chain.");

            // The left arm hangs off the torso, not the root, so its actor-space x is
            // torso.x + arm.x = -0.6, minus its 0.25 half-extent.
            Assert.LessOrEqual(
                restBounds.Min.x,
                -0.85f + Tolerance,
                "Rest bounds must include a part parented below another part, which is only correct " +
                "if the baker accumulated the whole transform chain rather than one local position.");

            Assert.LessOrEqual(
                restBounds.Min.y,
                0.25f + Tolerance,
                "The torso's lower edge sets the floor of the box.");
            Assert.GreaterOrEqual(
                restBounds.Max.x,
                0.5f - Tolerance,
                "The torso's right edge sets the right of the box.");
        }

        [Test]
        public void AnActorWithNoParts_GetsAZeroRestBoundsBox_RatherThanAnInvertedOne()
        {
            // MinMaxAABB.Empty is inverted by construction; converting it unchecked would hand the
            // bounds system nonsense extents rather than an honest empty box.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000509UL);
            GameObject actorGameObject = fixtureAssets.CreateActorRoot("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            AABB restBounds = bakingWorld.EntityManager
                .GetComponentData<ActorRestBounds>(bakingWorld.GetPrimaryEntity(actorGameObject)).value;

            Assert.AreEqual(0f, restBounds.Extents.x, Tolerance, "An actor with no parts has no extent.");
            Assert.AreEqual(0f, restBounds.Extents.y, Tolerance, "An actor with no parts has no extent.");
            Assert.AreEqual(0f, restBounds.Extents.z, Tolerance, "An actor with no parts has no extent.");
        }

        // -----------------------------------------------------------------------------------
        // "unknown-target part logs error and is skipped without failing the bake"
        // -----------------------------------------------------------------------------------

        [Test]
        public void APartWithAnUnknownTargetId_IsSkippedWithAnError_AndTheBakeStillSucceeds()
        {
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x000000000000050AUL);
            GameObject actorGameObject = fixtureAssets.CreateStandardActor("Actor", clipSet, false);
            fixtureAssets.AddPart(
                actorGameObject, "Stray", ActorBakeFixture.UnknownTargetId, new Vector3(3f, 0f, 0f));

            LogAssert.Expect(LogType.Error, new Regex(ActorBakeFixture.UnknownTargetId.ToString()));

            bakingWorld.Bake(actorGameObject);
            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            DynamicBuffer<RigPartRef> partRefs =
                bakingWorld.EntityManager.GetBuffer<RigPartRef>(actorEntity);

            Assert.AreEqual(
                3,
                partRefs.Length,
                "The stray part must be skipped, leaving the three valid parts bound. A misconfigured " +
                "part is a content error to report, not a reason to fail the whole bake.");
        }

        [Test]
        public void AStrayPartDoesNotEnlargeTheRestBounds()
        {
            // A part that could not be resolved contributes no extents — otherwise a typo in one
            // target id would silently inflate every actor's culling box.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x000000000000050BUL);
            GameObject actorGameObject = fixtureAssets.CreateStandardActor("Actor", clipSet, false);
            fixtureAssets.AddPart(
                actorGameObject, "Stray", ActorBakeFixture.UnknownTargetId, new Vector3(50f, 0f, 0f));

            LogAssert.Expect(LogType.Error, new Regex(ActorBakeFixture.UnknownTargetId.ToString()));

            bakingWorld.Bake(actorGameObject);
            AABB restBounds = bakingWorld.EntityManager
                .GetComponentData<ActorRestBounds>(bakingWorld.GetPrimaryEntity(actorGameObject)).value;

            Assert.Less(
                restBounds.Max.x,
                50f,
                "An unresolvable part must not contribute to the rest bounds.");
        }
    }
}
