// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

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

            // Log suppression is not set up here. UTF wraps each setup method in its own LogScope
            // and disposes it before the test body runs, so LogAssert.ignoreFailingMessages set in
            // [SetUp] never reaches the bake. BakingTestWorld.Bake sets it around the bake instead,
            // which is where the host application's own baking systems do their logging.
        }

        [TearDown]
        public void TearDown()
        {
            bakingWorld.Dispose();
            fixtureAssets.DestroyAll();
        }

        /// <summary>
        /// Asserts the bake emitted exactly <paramref name="expectedCount"/> toolkit warnings, one
        /// of which contains <paramref name="expectedFragment"/>. The count is what makes "exactly
        /// one" a real claim: a validator warning five times would otherwise pass.
        /// </summary>
        private void AssertToolkitWarnings(int expectedCount, string expectedFragment)
        {
            Assert.AreEqual(
                expectedCount,
                bakingWorld.ToolkitWarnings.Count,
                "Expected exactly " + expectedCount + " toolkit warning(s), got: " +
                string.Join(" | ", bakingWorld.ToolkitWarnings));
            if (expectedFragment == null)
            {
                return;
            }
            bool matched = false;
            for (int warningIndex = 0; warningIndex < bakingWorld.ToolkitWarnings.Count; warningIndex++)
            {
                if (bakingWorld.ToolkitWarnings[warningIndex].Contains(expectedFragment))
                {
                    matched = true;
                    break;
                }
            }
            Assert.IsTrue(
                matched,
                "No toolkit warning mentioned '" + expectedFragment + "'. Got: " +
                string.Join(" | ", bakingWorld.ToolkitWarnings));
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

            // The assertions above catch a MISSING component but not an EXTRA one, which is half a
            // check for something called "exactly". Unity adds its own transform and baking
            // components that are none of this package's business, so the exact comparison is
            // scoped to types this package owns.
            AssertToolkitComponentsAre(
                entityManager,
                actorEntity,
                new[]
                {
                    typeof(ClipRegistry), typeof(PlaybackLayer), typeof(AnimationCommand),
                    typeof(AnimationCommandPending), typeof(AnimEventOutput), typeof(AnimEventsPending),
                    typeof(RigPartRef), typeof(RigBindingUninitialized), typeof(AnimVisible),
                    typeof(BoundsDirty), typeof(ActorRestBounds), typeof(SampleSettings),
                    typeof(VatTextureBinding)
                });
        }

        /// <summary>
        /// Asserts the entity carries exactly the given toolkit-owned components — no more, no
        /// fewer. Components from other assemblies (Unity's transforms, baking bookkeeping) are
        /// ignored, since this package does not control them.
        /// </summary>
        private static void AssertToolkitComponentsAre(
            EntityManager entityManager,
            Entity entity,
            System.Type[] expectedComponentTypes)
        {
            NativeArray<ComponentType> presentComponents =
                entityManager.GetComponentTypes(entity, Allocator.Temp);
            try
            {
                System.Collections.Generic.List<string> presentToolkitNames =
                    new System.Collections.Generic.List<string>();
                for (int componentIndex = 0; componentIndex < presentComponents.Length; componentIndex++)
                {
                    System.Type managedType = presentComponents[componentIndex].GetManagedType();
                    if (managedType == null || managedType.Namespace == null)
                    {
                        continue;
                    }
                    if (managedType.Namespace.StartsWith("StitchPunk.AnimationToolkit"))
                    {
                        presentToolkitNames.Add(managedType.Name);
                    }
                }

                System.Collections.Generic.List<string> expectedNames =
                    new System.Collections.Generic.List<string>();
                for (int expectedIndex = 0; expectedIndex < expectedComponentTypes.Length; expectedIndex++)
                {
                    expectedNames.Add(expectedComponentTypes[expectedIndex].Name);
                }

                presentToolkitNames.Sort();
                expectedNames.Sort();
                CollectionAssert.AreEqual(
                    expectedNames,
                    presentToolkitNames,
                    "The baked entity must carry exactly the section 5.2 archetype. An unexpected " +
                    "component is as much a contract break as a missing one: it changes the " +
                    "archetype, and with it the chunk layout every query in the package matches.");
            }
            finally
            {
                presentComponents.Dispose();
            }
        }

        [Test]
        public void BakingAQuadPart_ProducesTheSection52PartArchetype()
        {
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000511UL);
            GameObject actorGameObject = fixtureAssets.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            EntityManager entityManager = bakingWorld.EntityManager;
            Entity torsoEntity =
                bakingWorld.GetPrimaryEntity(ActorBakeFixture.FindPart(actorGameObject, "Torso"));

            Assert.IsTrue(entityManager.HasComponent<RigPartBinding>(torsoEntity), "RigPartBinding.");
            Assert.IsTrue(entityManager.HasComponent<TargetRestPose>(torsoEntity), "TargetRestPose.");
            Assert.IsTrue(entityManager.HasComponent<TargetPose>(torsoEntity), "TargetPose.");
            Assert.IsTrue(
                entityManager.HasComponent<AnimVisible>(torsoEntity),
                "A part carries its own AnimVisible (section 5.2).");

            // A Quad is transform-only: its pose reaches the screen through LocalTransform and
            // PostTransformMatrix, so it needs no per-instance material property at all. Asserting
            // the absences catches a baker adding every technique's properties to every part,
            // which would put a material property on every quad in a crowd for nothing.
            Assert.IsFalse(
                entityManager.HasComponent<SpriteSliceProperty>(torsoEntity),
                "A Quad is transform-only and must carry no sprite property.");
            Assert.IsFalse(
                entityManager.HasComponent<AtlasFrameProperty>(torsoEntity),
                "A Quad is transform-only and must carry no atlas property.");
            Assert.IsFalse(
                entityManager.HasComponent<VatFrameAProperty>(torsoEntity),
                "A Quad part must not carry VAT properties.");
            Assert.IsFalse(
                entityManager.HasComponent<VatDriven>(torsoEntity),
                "A Quad part is not VAT-driven.");
        }

        [Test]
        public void BakingAFlipbookPart_CarriesBothSpritePropertiesAndNoVatOnes()
        {
            // TargetKind.FlipbookPlane had no coverage at all, and it is the kind that actually
            // owns the section 6.2 sprite properties. Which of the two a clip drives is a per-track
            // SpriteFrameMode decision, so the part must carry both.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000514UL);
            GameObject actorGameObject = fixtureAssets.CreateActorRoot("Actor", clipSet, false);
            GameObject flipbookPart = fixtureAssets.AddPart(
                actorGameObject, "Billboard", ActorBakeFixture.TorsoTargetId, Vector3.zero);
            RigTargetAuthoring flipbookAuthoring = flipbookPart.GetComponent<RigTargetAuthoring>();
            flipbookAuthoring.useKindOverride = true;
            flipbookAuthoring.kindOverride = TargetKind.FlipbookPlane;
            flipbookAuthoring.restSliceIndex = 3;

            bakingWorld.Bake(actorGameObject);
            EntityManager entityManager = bakingWorld.EntityManager;
            Entity partEntity = bakingWorld.GetPrimaryEntity(flipbookPart);

            Assert.IsTrue(
                entityManager.HasComponent<SpriteSliceProperty>(partEntity),
                "FLIPBOOK part is missing SpriteSliceProperty — the kind override was not honoured.");
            Assert.IsTrue(
                entityManager.HasComponent<AtlasFrameProperty>(partEntity),
                "FLIPBOOK part is missing AtlasFrameProperty — the kind override was not honoured.");
            Assert.AreEqual(
                3f,
                entityManager.GetComponentData<SpriteSliceProperty>(partEntity).Value,
                Tolerance,
                "The slice property must start at the authored rest slice, not at zero, or the part " +
                "shows frame 0 for one frame before the first sample lands.");
            Assert.IsFalse(
                entityManager.HasComponent<VatFrameAProperty>(partEntity),
                "A flipbook part must not carry VAT properties.");
            Assert.IsFalse(
                entityManager.HasComponent<VatDriven>(partEntity),
                "A flipbook part is not VAT-driven.");
        }

        [Test]
        public void BakingAVatPart_CarriesAllThreeVatPropertiesAndNoSpriteOnes()
        {
            // VatDriven was asserted by the material-validation tests, but the three per-instance
            // properties that actually carry VAT state into the draw were asserted nowhere. Section
            // 6.2 binds them to _VatFrameA/_VatFrameB/_VatBlend; a part missing any one of them
            // renders a frozen frame, and nothing in the suite would have said so.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x000000000000051BUL);
            GameObject actorGameObject = fixtureAssets.CreateActorRoot("Actor", clipSet, false);
            GameObject vatPart = fixtureAssets.AddPart(
                actorGameObject, "VatBody", ActorBakeFixture.TorsoTargetId, Vector3.zero);
            RigTargetAuthoring vatPartAuthoring = vatPart.GetComponent<RigTargetAuthoring>();
            vatPartAuthoring.useKindOverride = true;
            vatPartAuthoring.kindOverride = TargetKind.VatMesh;
            vatPartAuthoring.vatDrivingLayerIndex = 1;

            bakingWorld.Bake(actorGameObject);
            EntityManager entityManager = bakingWorld.EntityManager;
            Entity partEntity = bakingWorld.GetPrimaryEntity(vatPart);

            Assert.IsTrue(
                entityManager.HasComponent<VatFrameAProperty>(partEntity), "VatFrameAProperty.");
            Assert.IsTrue(
                entityManager.HasComponent<VatFrameBProperty>(partEntity), "VatFrameBProperty.");
            Assert.IsTrue(
                entityManager.HasComponent<VatBlendProperty>(partEntity), "VatBlendProperty.");
            Assert.AreEqual(
                1,
                entityManager.GetComponentData<VatDriven>(partEntity).layerIndex,
                "VatDriven must carry the authored driving layer. Defaulting to 0 would silently " +
                "drive every VAT part from the base layer regardless of what the user chose.");
            Assert.IsFalse(
                entityManager.HasComponent<SpriteSliceProperty>(partEntity),
                "A VAT part must not also carry the sprite properties.");
            Assert.IsFalse(
                entityManager.HasComponent<AtlasFrameProperty>(partEntity),
                "A VAT part must not also carry the sprite properties.");
        }

        [Test]
        public void BakingAQuadPart_ProducesPostTransformMatrix_SoScaleIsNotDead()
        {
            // Section 4.1 names PostTransformMatrix as the fix for the audit's dead-scale
            // regression, and C4's TransformApplySystem writes it every frame. The baker does not
            // add it directly — it asks for TransformUsageFlags.NonUniformScale and relies on
            // Entities to add it. If Entities ever elides that for a unit-scaled part, scale and
            // flip silently stop working and nothing else in the suite would notice until a
            // shader-level module failed for reasons that look unrelated.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000512UL);
            GameObject actorGameObject = fixtureAssets.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            Entity torsoEntity =
                bakingWorld.GetPrimaryEntity(ActorBakeFixture.FindPart(actorGameObject, "Torso"));

            Assert.IsTrue(
                bakingWorld.EntityManager.HasComponent<Unity.Transforms.PostTransformMatrix>(torsoEntity),
                "A rig part must bake with PostTransformMatrix. Without it the animated scale has " +
                "nowhere to be written and the dead-scale regression of audit section 2.1 returns.");
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
        public void SampleSettingsAndVatTextureBinding_CarryTheAuthoredValues_NotJustTheComponents()
        {
            // Both were presence-checked only: a baker that added them default-initialised passed
            // the archetype test, and every actor would then have sampled at 0 Hz against no
            // texture. The clamp is the interesting half — a negative rate is a typo, and section
            // 5.6 reads rateHz as "0 means every frame", so an unclamped −10 would be neither.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000518UL);
            Texture2D boneTexture = fixtureAssets.CreateTexture("BoneTex");
            Texture2D normalTexture = fixtureAssets.CreateTexture("NormalTex");
            VatTextureSetAsset vatTextures =
                fixtureAssets.CreateVatTextureSet("VatSet", 0x0000000000000602UL, boneTexture);
            vatTextures.normalTexture = normalTexture;
            clipSet.vatTextures = vatTextures;

            GameObject clampedActor = fixtureAssets.CreateActorRoot("ClampedActor", clipSet, false);
            clampedActor.GetComponent<ActorAuthoring>().sampleOverride =
                new SampleSettings { rateHz = -10f };
            GameObject ratedActor = fixtureAssets.CreateActorRoot("RatedActor", clipSet, false);
            ratedActor.GetComponent<ActorAuthoring>().sampleOverride =
                new SampleSettings { rateHz = 12f };

            bakingWorld.Bake(clampedActor, ratedActor);
            EntityManager entityManager = bakingWorld.EntityManager;

            Assert.AreEqual(
                0f,
                entityManager.GetComponentData<SampleSettings>(
                    bakingWorld.GetPrimaryEntity(clampedActor)).rateHz,
                Tolerance,
                "A negative authored rate must clamp to 0 — 'sample every frame' — not survive as a " +
                "negative interval the sampler would divide by.");
            Assert.AreEqual(
                12f,
                entityManager.GetComponentData<SampleSettings>(
                    bakingWorld.GetPrimaryEntity(ratedActor)).rateHz,
                Tolerance,
                "A legitimate rate must reach the entity unchanged; clamping everything to 0 would " +
                "pass the assertion above and quietly disable crowd sampling.");

            VatTextureBinding binding = entityManager.GetComponentData<VatTextureBinding>(
                bakingWorld.GetPrimaryEntity(ratedActor));
            Assert.AreEqual(
                vatTextures.SetKey,
                binding.setKey,
                "The binding's set key is what section 5.7 matches against the blob's vatSetKey; a " +
                "default here silently unbinds every VAT part on the actor.");
            Assert.AreEqual(
                boneTexture,
                binding.boneOrPositionTexture.Value,
                "A bone-flavor set must bind its bone texture, not its position texture.");
            Assert.AreEqual(normalTexture, binding.normalTexture.Value, "The normal texture too.");
        }

        [Test]
        public void AnActorWithNoVatTextureSet_GetsADefaultBinding_RatherThanNone()
        {
            // The component is unconditional so the archetype does not fork on content, which means
            // the no-VAT case must be representable as a value. Default is that value: set key 0
            // matches no blob, and the null texture refs are what section 5.7 checks before binding.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000519UL);
            GameObject actorGameObject = fixtureAssets.CreateActorRoot("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);
            VatTextureBinding binding = bakingWorld.EntityManager.GetComponentData<VatTextureBinding>(
                bakingWorld.GetPrimaryEntity(actorGameObject));

            Assert.AreEqual(0UL, binding.setKey, "No texture set means no set key.");
            Assert.IsNull(binding.boneOrPositionTexture.Value, "No texture set means no texture.");
        }

        [Test]
        public void TwoActorsFromOneClipSet_GetDifferentSamplePhases_AndTheSameOneOnRebake()
        {
            // Spreading sampling load across frames is the entire purpose of phase01, and nothing
            // asserted it. A baker returning a constant would have satisfied every other test.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000513UL);
            GameObject firstActor = fixtureAssets.CreateStandardActor("FirstActor", clipSet, false);
            GameObject secondActor = fixtureAssets.CreateStandardActor("SecondActor", clipSet, false);

            bakingWorld.Bake(firstActor, secondActor);
            EntityManager entityManager = bakingWorld.EntityManager;
            float firstPhase = entityManager
                .GetComponentData<SampleSettings>(bakingWorld.GetPrimaryEntity(firstActor)).phase01;
            float secondPhase = entityManager
                .GetComponentData<SampleSettings>(bakingWorld.GetPrimaryEntity(secondActor)).phase01;

            Assert.AreNotEqual(
                firstPhase,
                secondPhase,
                "Two actors must sample on different frames, or the crowd cost this exists to " +
                "spread all lands on one frame anyway.");
            Assert.GreaterOrEqual(firstPhase, 0f, "Phase is normalized to [0, 1).");
            Assert.Less(firstPhase, 1f, "Phase is normalized to [0, 1).");
            Assert.GreaterOrEqual(secondPhase, 0f, "Phase is normalized to [0, 1).");
            Assert.Less(secondPhase, 1f, "Phase is normalized to [0, 1).");

            // And it must be reproducible: amendment A18 forbids deriving it from a session-local
            // id precisely so a rebake of unchanged source yields the same bytes.
            using (BakingTestWorld rebakeWorld = new BakingTestWorld("RebakeWorld"))
            {
                rebakeWorld.Bake(firstActor, secondActor);
                Assert.AreEqual(
                    firstPhase,
                    rebakeWorld.EntityManager
                        .GetComponentData<SampleSettings>(
                            rebakeWorld.GetPrimaryEntity(firstActor)).phase01,
                    Tolerance,
                    "Re-baking unchanged source must reproduce the same phase. A session-local id " +
                    "would give a different value here every run.");
            }
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

            // Assert WHICH GameObject each dense index resolved to. Merely checking that some part
            // carries each of {0,1,2} is satisfied by any permutation — including binding by
            // authoring position, which is precisely the bug this fixture's out-of-order ids exist
            // to catch. The fixture authors Torso, Head, LeftArm while the dense order is Torso(100),
            // LeftArm(200), Head(300), so authoring order and dense order disagree for two of three.
            AssertPartBound(
                entityManager, partRefs, actorEntity, actorGameObject,
                "Torso", ActorBakeFixture.TorsoDenseIndex);
            AssertPartBound(
                entityManager, partRefs, actorEntity, actorGameObject,
                "LeftArm", ActorBakeFixture.LeftArmDenseIndex);
            AssertPartBound(
                entityManager, partRefs, actorEntity, actorGameObject,
                "Head", ActorBakeFixture.HeadDenseIndex);
        }

        private void AssertPartBound(
            EntityManager entityManager,
            DynamicBuffer<RigPartRef> partRefs,
            Entity expectedActorRoot,
            GameObject actorGameObject,
            string partName,
            int expectedDenseIndex)
        {
            GameObject partGameObject = ActorBakeFixture.FindPart(actorGameObject, partName);
            Entity partEntity = bakingWorld.GetPrimaryEntity(partGameObject);
            Assert.AreNotEqual(Entity.Null, partEntity, partName + " must bake to a real entity.");

            Assert.IsTrue(
                entityManager.HasComponent<RigPartBinding>(partEntity),
                partName + " must carry RigPartBinding.");
            RigPartBinding binding = entityManager.GetComponentData<RigPartBinding>(partEntity);
            Assert.AreEqual(
                expectedDenseIndex,
                binding.targetIndex,
                partName + " must resolve to the dense index of its own target id, not to its " +
                "position in the authoring hierarchy.");
            Assert.AreEqual(
                expectedActorRoot,
                binding.actorRoot,
                partName + " must point back at its actor root, or section 5.3 cannot rebind it.");
            Assert.IsTrue(
                entityManager.HasComponent<TargetRestPose>(partEntity),
                partName + " must carry the rest pose captured from its authoring transform.");
            Assert.IsTrue(
                entityManager.HasComponent<TargetPose>(partEntity),
                partName + " must carry the pose the sampler writes.");

            // And the root's buffer entry for that index must name this same entity — the two ends
            // of the binding have to agree, not merely each be individually plausible.
            bool foundInBuffer = false;
            for (int partRefIndex = 0; partRefIndex < partRefs.Length; partRefIndex++)
            {
                if (partRefs[partRefIndex].targetIndex != expectedDenseIndex)
                {
                    continue;
                }
                Assert.AreEqual(
                    partEntity,
                    partRefs[partRefIndex].part,
                    "The root's buffer entry for dense index " + expectedDenseIndex +
                    " must reference " + partName + ", the part that actually owns that target id.");
                foundInBuffer = true;
                break;
            }
            Assert.IsTrue(
                foundInBuffer,
                "The root's RigPartRef buffer has no entry for dense index " + expectedDenseIndex + ".");
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
        public void TwoActorsSharingAClipSet_BuildTheRegistryOnce_NotOncePerActor()
        {
            // Blob equality above cannot tell the two paths apart: build-twice-and-dedup ends at the
            // same reference as probe-and-skip. The difference is the work, and it scales with the
            // crowd — a probe that quietly stopped matching Build's key would cost every actor a
            // full canonicalisation-and-hash pass with no wrong value anywhere to catch it.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000517UL);
            GameObject firstActor = fixtureAssets.CreateStandardActor("FirstActor", clipSet, false);
            GameObject secondActor = fixtureAssets.CreateStandardActor("SecondActor", clipSet, false);
            GameObject thirdActor = fixtureAssets.CreateStandardActor("ThirdActor", clipSet, false);

            ClipRegistryBuilder.ResetBuildInvocationCount();
            bakingWorld.Bake(firstActor, secondActor, thirdActor);

            Assert.AreEqual(
                1,
                ClipRegistryBuilder.BuildInvocationCount,
                "Three actors on one clip set must build one registry. Building three and throwing " +
                "two away is invisible in the baked data and is exactly what TryComputeContentHash " +
                "exists to prevent.");
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

            // Every bound is asserted exactly, on both sides. One-sided assertions would let an
            // arbitrarily over-inflated box pass, and a box that is too large is a real defect: it
            // is exactly what culling correctness depends on being tight.
            //
            // Derived by hand from the fixture. Boxes in actor space, half-extents in brackets:
            //   Torso   at (0.4, 1, 0)   [0.5, 0.75, 0.1] -> x [-0.10, 0.90]  y [ 0.25,  1.75]
            //   LeftArm at (-0.2, 1.3, 0)[0.25, 0.5, 0.1] -> x [-0.45, 0.05]  y [ 0.80,  1.80]
            //   Head    at (0, 12, 0)    [0.4, 0.4, 0.1]  -> x [-0.40, 0.40]  y [11.60, 12.40]
            //   union                                       x [-0.45, 0.90]  y [ 0.25, 12.40]
            //
            // The left arm's actor-space centre is torso + arm = (0.4 - 0.6, 1 + 0.3). A baker that
            // read localPosition without walking the chain would place it at (-0.6, 0.3), giving
            // x min -0.85 and y min -0.20 — both caught here. Offset-space bounds would collapse
            // every part onto the origin and top out near 0.75, nowhere near 12.4.
            Assert.AreEqual(-0.45f, restBounds.Min.x, Tolerance, "Rest bounds min x.");
            Assert.AreEqual(0.90f, restBounds.Max.x, Tolerance, "Rest bounds max x.");
            Assert.AreEqual(0.25f, restBounds.Min.y, Tolerance, "Rest bounds min y.");
            Assert.AreEqual(
                12.40f,
                restBounds.Max.y,
                Tolerance,
                "Rest bounds must reach the head at y = 12 plus its 0.4 half-extent. Falling short " +
                "means the baker treated offset space as actor space; overshooting means it " +
                "inflated the box, which costs culling accuracy just as surely.");
            Assert.AreEqual(-0.10f, restBounds.Min.z, Tolerance, "Rest bounds min z.");
            Assert.AreEqual(0.10f, restBounds.Max.z, Tolerance, "Rest bounds max z.");
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

        [Test]
        public void APartRotatedNegatively_CapturesASignedRestRotation_NotAWrappedOne()
        {
            // Every other fixture part is unrotated, so atan2 returns 0 whether the baker reads the
            // quaternion or localEulerAngles — the whole suite passed on a reader that cannot tell
            // −30° from +330°. Only a negatively rotated part separates them, and the difference is
            // not cosmetic: section 5.6 composes animated rotation as an offset from this value, so
            // a wrapped rest rotation spins the part a full turn the wrong way on the first sample.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000515UL);
            GameObject actorGameObject = fixtureAssets.CreateActorRoot("Actor", clipSet, false);
            GameObject rotatedPart = fixtureAssets.AddPart(
                actorGameObject, "Torso", ActorBakeFixture.TorsoTargetId, Vector3.zero);
            rotatedPart.transform.localRotation = Quaternion.Euler(0f, 0f, -30f);

            bakingWorld.Bake(actorGameObject);
            Entity partEntity = bakingWorld.GetPrimaryEntity(rotatedPart);
            EntityManager entityManager = bakingWorld.EntityManager;

            Assert.AreEqual(
                -math.PI / 6f,
                entityManager.GetComponentData<TargetRestPose>(partEntity).rotationZ,
                Tolerance,
                "A −30° part must capture −π/6 radians. Reading localEulerAngles instead would " +
                "give +330° (≈ +5.76 rad) — the same pose, but the wrong number to blend from.");
            Assert.AreEqual(
                -math.PI / 6f,
                entityManager.GetComponentData<TargetPose>(partEntity).rotationZ,
                Tolerance,
                "The seeded output pose starts at the rest pose, so it carries the same sign.");
        }

        [Test]
        public void ADisabledPart_IsStillCoveredByTheRestBounds_BecauseItIsStillBound()
        {
            // The binding pass includes disabled entities, so a part on an inactive GameObject bakes,
            // binds and animates. If the bounds pass excluded it, the actor's culling box would be
            // right until the part was enabled at runtime and then wrong — the kind of bug that
            // reproduces only in the shipped game.
            //
            // The two passes agree today only because Baker.GetComponentsInChildren defaults to
            // include-inactive, the opposite of the GameObject method it is named after. That is an
            // easy thing to "fix" into a real defect, which is why this is pinned rather than left
            // to the reader of ActorBaker's comment.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x000000000000051AUL);
            GameObject actorGameObject = fixtureAssets.CreateActorRoot("Actor", clipSet, false);
            fixtureAssets.AddPart(
                actorGameObject, "Torso", ActorBakeFixture.TorsoTargetId, Vector3.zero);
            GameObject disabledHead = fixtureAssets.AddPart(
                actorGameObject, "Head", ActorBakeFixture.HeadTargetId, ActorBakeFixture.HeadLocalPosition);
            disabledHead.SetActive(false);

            bakingWorld.Bake(actorGameObject);
            AABB restBounds = bakingWorld.EntityManager
                .GetComponentData<ActorRestBounds>(bakingWorld.GetPrimaryEntity(actorGameObject)).value;

            // Head sits at y = 12 with a 0.4 half-extent; the torso alone would top out at 0.75.
            Assert.AreEqual(
                12.40f,
                restBounds.Max.y,
                Tolerance,
                "The disabled head must still be inside the box. Topping out at 0.75 means the " +
                "bounds pass skipped inactive children while the binding pass did not.");
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

            bakingWorld.Bake(actorGameObject);

            Assert.AreEqual(
                1,
                bakingWorld.ToolkitErrors.Count,
                "A stray part must be reported exactly once, by the managed baker that can name it.");
            StringAssert.Contains("Stray", bakingWorld.ToolkitErrors[0]);
            StringAssert.Contains(
                ActorBakeFixture.UnknownTargetId.ToString(), bakingWorld.ToolkitErrors[0]);

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
        public void AnActorOnAClipSetWithValidationErrors_LogsEveryRuleCode_AndBakesNoRegistry()
        {
            // ClipRegistryBuilder.Build throws on a set with errors, and ActorBaker turns that
            // exception into the one message a user ever sees about it. Nothing exercised that
            // path, so the enumeration could have listed no codes — or crashed on the exception —
            // and every other test would still have passed. Two rules are broken, not one: a
            // formatter that reports only the first failure is the likely bug, and a single-error
            // fixture cannot catch it.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000516UL);
            ClipAsset walkClip = ActorBakeFixture.FindClip(clipSet, ActorBakeFixture.WalkClipStableId);
            walkClip.duration = 0f;
            walkClip.transformTracks[0].targetId = ActorBakeFixture.UnknownTargetId;

            // Three parts, deliberately. Each one's binding pass will find an actor entity with no
            // registry on it, and each must stay quiet about that: the actor has already reported
            // the single actionable message, and restating it per part buries it.
            GameObject actorGameObject = fixtureAssets.CreateStandardActor("Actor", clipSet, false);

            bakingWorld.Bake(actorGameObject);

            Assert.AreEqual(
                1,
                bakingWorld.ToolkitErrors.Count,
                "The whole set fails once, as one message — not once plus once per part. Got: " +
                string.Join(" | ", bakingWorld.ToolkitErrors));
            StringAssert.Contains("Set", bakingWorld.ToolkitErrors[0]);
            StringAssert.Contains(
                ValidationCode.V01.ToString(),
                bakingWorld.ToolkitErrors[0],
                "The message must name the duration rule.");
            StringAssert.Contains(
                ValidationCode.V02.ToString(),
                bakingWorld.ToolkitErrors[0],
                "The message must name the unknown-target rule too. Listing only the first failure " +
                "sends the user round the loop once per broken clip.");

            using (EntityQuery registryQuery =
                bakingWorld.EntityManager.CreateEntityQuery(typeof(ClipRegistry)))
            {
                Assert.AreEqual(
                    0,
                    registryQuery.CalculateEntityCount(),
                    "A set that cannot be validated must produce no registry at all. A half-built " +
                    "actor — layers seeded against a blob that was never made — is worse than none.");
            }
        }

        // -----------------------------------------------------------------------------------
        // "a material↔texture-set mismatch fixture logs exactly one warning from RigTargetBaker"
        // -----------------------------------------------------------------------------------

        [Test]
        public void AVatPartWhoseMaterialLacksTheTextureSlot_LogsExactlyOneWarning()
        {
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x000000000000050CUL);
            Texture2D boneTexture = fixtureAssets.CreateTexture("BoneTex");
            clipSet.vatTextures =
                fixtureAssets.CreateVatTextureSet("VatSet", 0x0000000000000601UL, boneTexture);

            GameObject actorGameObject = fixtureAssets.CreateStandardActor("Actor", clipSet, false);

            // The fixture material is a plain unlit material: it has no _VatBoneTex slot, so it
            // cannot display the bone texture the set baked. Catching that at bake is the whole
            // point of the check — at runtime it degrades to a part animating against nothing
            // visible, which is far harder to trace back to a material assignment.
            GameObject vatPart = fixtureAssets.AddPart(
                actorGameObject, "VatBody", ActorBakeFixture.TorsoTargetId, Vector3.zero);
            RigTargetAuthoring vatPartAuthoring = vatPart.GetComponent<RigTargetAuthoring>();
            vatPartAuthoring.useKindOverride = true;
            vatPartAuthoring.kindOverride = TargetKind.VatMesh;
            vatPartAuthoring.expectedMaterial = fixtureAssets.CreateVatMaterial("PlainMaterial", boneTexture);

            // This part claims the torso id the standard actor's own torso already claims, so the
            // binding pass reports a duplicate. That is incidental to the material check, but it is
            // asserted below rather than ignored: it comes from the Bursted pass, and pinning it is
            // what proves worker-thread diagnostics are being captured at all.
            bakingWorld.Bake(actorGameObject);

            AssertToolkitWarnings(1, "_VatBoneTex");
            Assert.AreEqual(
                1,
                bakingWorld.ToolkitErrors.Count,
                "The duplicate torso claim is the only error this fixture should produce: " +
                string.Join(" | ", bakingWorld.ToolkitErrors));
            StringAssert.Contains(
                "VatBody",
                bakingWorld.ToolkitErrors[0],
                "A Bursted binding diagnostic must name the offending object by its authoring path " +
                "(amendment A21). It cannot pass a clickable Object reference, so the path is the " +
                "only thing standing between the user and hunting through the hierarchy by hand.");

            Assert.IsTrue(
                bakingWorld.EntityManager.HasComponent<ClipRegistry>(
                    bakingWorld.GetPrimaryEntity(actorGameObject)),
                "A material mismatch is a warning, not a bake failure: the actor must still bake.");
        }

        [Test]
        public void AVatPartBoundToTheWrongTexture_LogsTheSection44Mismatch()
        {
            // THE section 4.4 case: a material that genuinely declares _VatBoneTex, but points it at
            // a different texture than the set baked. Every other fixture short-circuits earlier on
            // "declares no such slot", which is a different branch — so without this the specified
            // mismatch had no coverage at all.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x000000000000050FUL);
            Texture2D bakedTexture = fixtureAssets.CreateTexture("BakedBoneTex");
            Texture2D staleTexture = fixtureAssets.CreateTexture("StaleBoneTex");
            clipSet.vatTextures =
                fixtureAssets.CreateVatTextureSet("VatSet", 0x0000000000000603UL, bakedTexture);

            GameObject actorGameObject = fixtureAssets.CreateActorRoot("Actor", clipSet, false);
            GameObject vatPart = fixtureAssets.AddPart(
                actorGameObject, "VatBody", ActorBakeFixture.TorsoTargetId, Vector3.zero);
            RigTargetAuthoring vatPartAuthoring = vatPart.GetComponent<RigTargetAuthoring>();
            vatPartAuthoring.useKindOverride = true;
            vatPartAuthoring.kindOverride = TargetKind.VatMesh;
            vatPartAuthoring.expectedMaterial =
                fixtureAssets.CreateVatCapableMaterial("StaleMaterial", staleTexture);

            bakingWorld.Bake(actorGameObject);

            // Two warnings: the stale texture binding, plus the unrelated notice that layer 0 is
            // default-active with no seeded clip. Pinning the count stops the mismatch warning
            // being emitted twice, or not at all.
            AssertToolkitWarnings(2, "StaleBoneTex");
            Assert.IsEmpty(bakingWorld.ToolkitErrors, "A stale binding warns; it is not an error.");

            Assert.IsTrue(
                bakingWorld.EntityManager.HasComponent<ClipRegistry>(
                    bakingWorld.GetPrimaryEntity(actorGameObject)),
                "A stale texture binding warns; it must not fail the bake.");
        }

        [Test]
        public void AVatPartBoundToTheBakedTexture_WarnsAboutNothing()
        {
            // The false-positive guard, and the reason the mismatch test above means anything: with
            // no fixture able to build a CORRECTLY configured VAT material, a validator that warned
            // on every part would have passed the whole suite.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x0000000000000510UL);
            Texture2D bakedTexture = fixtureAssets.CreateTexture("BakedBoneTex");
            clipSet.vatTextures =
                fixtureAssets.CreateVatTextureSet("VatSet", 0x0000000000000604UL, bakedTexture);

            GameObject actorGameObject = fixtureAssets.CreateActorRoot("Actor", clipSet, false);
            GameObject vatPart = fixtureAssets.AddPart(
                actorGameObject, "VatBody", ActorBakeFixture.TorsoTargetId, Vector3.zero);
            RigTargetAuthoring vatPartAuthoring = vatPart.GetComponent<RigTargetAuthoring>();
            vatPartAuthoring.useKindOverride = true;
            vatPartAuthoring.kindOverride = TargetKind.VatMesh;
            vatPartAuthoring.expectedMaterial =
                fixtureAssets.CreateVatCapableMaterial("CorrectMaterial", bakedTexture);

            bakingWorld.Bake(actorGameObject);

            // Only the unrelated default-active-layer notice — no material warning. That absence
            // is the false-positive guard; without it a validator warning on every part passes.
            AssertToolkitWarnings(1, "seeds no starting clip");
            Assert.IsEmpty(bakingWorld.ToolkitErrors, "A correct VAT part produces no errors.");

            Assert.IsTrue(
                bakingWorld.EntityManager.HasComponent<VatDriven>(
                    bakingWorld.GetPrimaryEntity(vatPart)),
                "A correctly configured VAT part must bake VAT-driven and silently.");
        }

        [Test]
        public void AVatPartWhoseClipSetHasNoTextureSet_LogsExactlyOneWarning()
        {
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x000000000000050DUL);
            GameObject actorGameObject = fixtureAssets.CreateActorRoot("Actor", clipSet, false);

            GameObject vatPart = fixtureAssets.AddPart(
                actorGameObject, "VatBody", ActorBakeFixture.TorsoTargetId, Vector3.zero);
            RigTargetAuthoring vatPartAuthoring = vatPart.GetComponent<RigTargetAuthoring>();
            vatPartAuthoring.useKindOverride = true;
            vatPartAuthoring.kindOverride = TargetKind.VatMesh;
            vatPartAuthoring.expectedMaterial = fixtureAssets.CreateVatMaterial("PlainMaterial", null);

            bakingWorld.Bake(actorGameObject);

            AssertToolkitWarnings(2, "no VAT texture set");

            Assert.IsTrue(
                bakingWorld.EntityManager.HasComponent<ClipRegistry>(
                    bakingWorld.GetPrimaryEntity(actorGameObject)),
                "A VAT part on a set with no texture set warns, but must not fail the bake.");
        }

        [Test]
        public void AVatPartWithNoMaterialToCheck_EmitsNoMaterialWarning()
        {
            // `expectedMaterial` is how a part whose renderer is supplied at runtime opts into the
            // check. Left empty there is nothing to compare, and warning anyway would train users
            // to ignore the warning that matters.
            RigAsset rig = fixtureAssets.CreateRig("Rig");
            ClipSetAsset clipSet = fixtureAssets.CreateClipSet("Set", rig, 0x000000000000050EUL);
            Texture2D boneTexture = fixtureAssets.CreateTexture("BoneTex");
            clipSet.vatTextures =
                fixtureAssets.CreateVatTextureSet("VatSet", 0x0000000000000602UL, boneTexture);

            GameObject actorGameObject = fixtureAssets.CreateActorRoot("Actor", clipSet, false);
            GameObject vatPart = fixtureAssets.AddPart(
                actorGameObject, "VatBody", ActorBakeFixture.TorsoTargetId, Vector3.zero);
            RigTargetAuthoring vatPartAuthoring = vatPart.GetComponent<RigTargetAuthoring>();
            vatPartAuthoring.useKindOverride = true;
            vatPartAuthoring.kindOverride = TargetKind.VatMesh;

            bakingWorld.Bake(actorGameObject);

            // The real assertion: no material warning. Without it this test asserted nothing about
            // logging at all, and warning on a null material would have passed.
            AssertToolkitWarnings(1, "seeds no starting clip");

            Assert.IsTrue(
                bakingWorld.EntityManager.HasComponent<VatDriven>(
                    bakingWorld.GetPrimaryEntity(vatPart)),
                "The part must still bake as VAT-driven; it simply has no material to validate.");
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
