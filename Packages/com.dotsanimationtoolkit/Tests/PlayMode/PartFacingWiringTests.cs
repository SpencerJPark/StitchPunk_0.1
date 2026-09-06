// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers the wiring of amendment A37's slice sum through a running world — that the facing
    /// term reaches a sampled pose at all, that a relative key adds rather than replaces, and that
    /// neither can be outranked by a clip on a higher layer.
    /// </summary>
    /// <remarks>
    /// The block arithmetic itself is covered in EditMode by <c>SpriteViewOffsetTests</c>. These
    /// fixtures are about the decisions only a running system makes: which parts get the facing
    /// term, where it is applied relative to composition, and whether it survives layering. The
    /// last of those is the reason the term is a component rather than clip data, so it is the one
    /// worth proving rather than asserting.
    /// </remarks>
    public sealed class PartFacingWiringTests
    {
        private const ulong IdleClipId = 100;
        private const int IdleClipIndex = 0;
        private const ulong BlinkClipId = 200;
        private const int BlinkClipIndex = 1;

        private const int EarTargetIndex = 0;
        private const int EarFramesPerVariant = 2;

        /// <summary>Round-ear front view: block base 2, so back view is 3.</summary>
        private const int RoundFrontRestSlice = 2;
        private const int RoundBackSlice = 3;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private Entity actor;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("PartFacingWiringTests");
            elapsedTime = 0d;
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;

            if (registry.IsCreated)
            {
                registry.Dispose();
            }
        }

        /// <summary>
        /// Catches: never applying the facing term. The whole feature would be inert while a host's
        /// facing system wrote a component nothing read — and the symptom is a character who simply
        /// never turns their ears round, which looks like missing art rather than missing code.
        /// </summary>
        [Test]
        public void AViewOffset_ReachesTheSampledPose()
        {
            Entity ear = BuildActorWithEar(spriteTrack: null);
            SetViewOffset(ear, 1);

            RunSample();

            Assert.AreEqual(
                RoundBackSlice,
                SampledSlice(ear),
                "Facing one view along from a round-front rest slice is the round BACK view.");
        }

        /// <summary>
        /// Catches: applying the facing term to parts that never opted in. <see cref="PartFacing"/>
        /// is optional (A23 precedent), and a part without it must sample exactly as it did before
        /// A37 existed.
        /// </summary>
        [Test]
        public void APartWithoutTheComponent_SamplesItsRestSliceUnchanged()
        {
            Entity ear = BuildActorWithEar(spriteTrack: null);

            Assert.IsFalse(
                testWorld.EntityManager.HasComponent<PartFacing>(ear),
                "Guard: the fixture part must not carry the component, or this proves nothing.");

            RunSample();

            Assert.AreEqual(RoundFrontRestSlice, SampledSlice(ear));
        }

        /// <summary>
        /// <strong>The reason the facing term is a component and not clip data.</strong> Catches:
        /// applying facing before composition, or anywhere a clip could overwrite it. A blink clip
        /// legitimately drives the slice, and on the host's layer model it composites above the
        /// locomotion that carries direction — so if facing lived in clip data it would be silently
        /// dropped exactly when a character blinked.
        /// </summary>
        [Test]
        public void AnUpperLayerClipDrivingTheSlice_DoesNotDropTheFacingTerm()
        {
            Entity ear = BuildActorWithEar(new PlaybackTestActor.SpriteTrackSpec
            {
                targetIndex = EarTargetIndex,
                mode = SpriteFrameMode.Slice,
                sliceSpace = SpriteSliceSpace.Absolute,
                keys = new[] { PlaybackTestActor.SliceKey(0f, RoundFrontRestSlice) }
            });

            // Layer 1 composites after layer 0, i.e. it wins the slice channel outright.
            PlayClipOnLayer(1, BlinkClipId, BlinkClipIndex);
            SetViewOffset(ear, 1);

            RunSample();

            Assert.AreEqual(
                RoundBackSlice,
                SampledSlice(ear),
                "The upper layer set the frame; facing still decides which view of it shows.");
        }

        /// <summary>
        /// Catches: treating a relative key as absolute. The key is authored as "+1 frame", and a
        /// system that replaced instead of adding would send every character to slice 1 — the
        /// pointy-ear back view — regardless of the variant they rolled.
        /// </summary>
        [Test]
        public void ARelativeKey_AddsToTheRestSliceRatherThanReplacingIt()
        {
            Entity ear = BuildActorWithEar(new PlaybackTestActor.SpriteTrackSpec
            {
                targetIndex = EarTargetIndex,
                mode = SpriteFrameMode.Slice,
                sliceSpace = SpriteSliceSpace.RelativeToRest,
                keys = new[] { PlaybackTestActor.SliceKey(0f, 1) }
            });
            PlayClipOnLayer(0, IdleClipId, IdleClipIndex);

            RunSample();

            Assert.AreEqual(
                RoundBackSlice,
                SampledSlice(ear),
                "Absolute handling would give 1, which is another ear shape entirely.");
        }

        /// <summary>
        /// Catches: dropping the <c>-1</c> sentinel from absolute mode while adding relative mode.
        /// Existing content relies on a negative key meaning "leave the frame alone", and A37 must
        /// not change what an unmigrated clip does.
        /// </summary>
        [Test]
        public void AnAbsoluteNegativeKey_StillMeansNoChange()
        {
            Entity ear = BuildActorWithEar(new PlaybackTestActor.SpriteTrackSpec
            {
                targetIndex = EarTargetIndex,
                mode = SpriteFrameMode.Slice,
                sliceSpace = SpriteSliceSpace.Absolute,
                keys = new[] { PlaybackTestActor.SliceKey(0f, -1) }
            });
            PlayClipOnLayer(0, IdleClipId, IdleClipIndex);

            RunSample();

            Assert.AreEqual(RoundFrontRestSlice, SampledSlice(ear));
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers
        // -------------------------------------------------------------------------------------

        private int SampledSlice(Entity part)
        {
            return testWorld.EntityManager.GetComponentData<TargetPose>(part).sliceIndex;
        }

        private void SetViewOffset(Entity part, int viewOffset)
        {
            testWorld.EntityManager.AddComponentData(
                part, new PartFacing { viewOffset = viewOffset, mirrorX = false });
        }

        /// <summary>
        /// <strong>A mirror is not an alt view, and the distinction is the whole reason
        /// <see cref="PartFacing"/> has two fields.</strong> Catches: implementing "flip the nose"
        /// as a slice offset. A nose seen from the left is the <em>same art</em> reflected, so it
        /// needs a negative scale and the same frame — an offset would send it to whatever slice
        /// happens to sit next to it, which for a design-driven target is another character's nose.
        /// </summary>
        [Test]
        public void MirroringAPart_NegatesItsScaleAndLeavesTheSliceAlone()
        {
            Entity ear = BuildActorWithEar(spriteTrack: null);
            testWorld.EntityManager.AddComponentData(
                ear, new PartFacing { viewOffset = 0, mirrorX = true });

            RunSample();

            TargetPose pose = testWorld.EntityManager.GetComponentData<TargetPose>(ear);
            Assert.Less(pose.scale.x, 0f, "A mirrored part is reflected on x.");
            Assert.AreEqual(
                RoundFrontRestSlice,
                pose.sliceIndex,
                "Mirroring must not move the frame — that is what viewOffset is for.");
        }

        /// <summary>
        /// <strong>A mirror reflects the rig, not just the art.</strong> Catches: negating only
        /// <c>scale.x</c>. An ear authored to the left of the head must end up on the <em>right</em>
        /// when mirrored; flipping its texture while leaving it pinned to the same side of the skull
        /// is a half-mirror, and looks worse than not mirroring at all. Rotation goes with it,
        /// because rotation is handed — an arm swung +30° reflects to −30°.
        /// </summary>
        [Test]
        public void MirroringAPart_AlsoReflectsItsPositionAndRotation()
        {
            registry = PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec { clipId = IdleClipId, duration = 1f }
                },
                targetCount: 1,
                framesPerVariant: EarFramesPerVariant);
            actor = PlaybackTestActor.CreateActor(testWorld, registry);

            // An ear off to one side of the head, tilted — the shape of a real cutout part.
            Entity ear = PlaybackTestActor.AddPart(
                testWorld, actor, EarTargetIndex,
                restPosition: new Unity.Mathematics.float3(-0.9f, 0.05f, 0.25f),
                restRotationZ: 0.4f,
                restSliceIndex: RoundFrontRestSlice,
                asFlipbookPlane: true);
            testWorld.EntityManager.AddComponentData(
                ear, new PartFacing { viewOffset = 0, mirrorX = true });

            RunSample();

            TargetPose pose = testWorld.EntityManager.GetComponentData<TargetPose>(ear);
            Assert.AreEqual(
                0.9f, pose.localPosition.x, 1e-4f, "The plane belongs on the other side of the head.");
            Assert.AreEqual(
                -0.4f, pose.rotation.z, 1e-4f, "Rotation is handed and reflects with the part.");
            Assert.AreEqual(
                0.05f, pose.localPosition.y, 1e-4f, "Only x reflects — height is unchanged.");
        }

        /// <summary>
        /// <strong>A mirror point flips its whole subtree, so a part under one must not flip
        /// again</strong> (owner rule 2026-09-06). Catches the shape that reached the owner's screen:
        /// scale composes down the hierarchy, so a mirrored part inside a mirrored parent multiplies
        /// back to +1 and only every second level of a nested rig ends up flipped — measured on
        /// <c>MaleCitizen</c> as Pelvis −1, Torso +1, Neck −1, BaseHead +1, a character whose head
        /// did not turn with its body.
        /// </summary>
        [Test]
        public void MirroringAPartUnderAMirrorPoint_LeavesItToTheAncestor()
        {
            registry = PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec { clipId = IdleClipId, duration = 1f }
                },
                targetCount: 1,
                framesPerVariant: EarFramesPerVariant);
            actor = PlaybackTestActor.CreateActor(testWorld, registry);

            Entity nestedPart = PlaybackTestActor.AddPart(
                testWorld, actor, EarTargetIndex,
                restPosition: new Unity.Mathematics.float3(-0.9f, 0.05f, 0.25f),
                restRotationZ: 0.4f,
                restSliceIndex: RoundFrontRestSlice,
                asFlipbookPlane: true);
            testWorld.EntityManager.AddComponentData(
                nestedPart, new PartFacing { viewOffset = 0, mirrorX = true });
            // What the baker adds when a facing part sits under another facing part.
            testWorld.EntityManager.AddComponent<PartMirrorFromAncestor>(nestedPart);

            RunSample();

            TargetPose pose = testWorld.EntityManager.GetComponentData<TargetPose>(nestedPart);
            Assert.AreEqual(-0.9f, pose.localPosition.x, 1e-4f,
                "The ancestor's reflection already moves this part; negating here would move it back.");
            Assert.AreEqual(0.4f, pose.rotation.z, 1e-4f, "Same for the handedness of its rotation.");
            Assert.Greater(pose.scale.x, 0f,
                "And for its art: two negated scales down one chain multiply back to unmirrored.");
        }

        /// <summary>
        /// Catches: assigning −1 rather than negating. A part authored already-flipped (a left ear
        /// built by mirroring the right one) composes with facing instead of being overridden by it,
        /// so mirroring a flipped part unflips it.
        /// </summary>
        [Test]
        public void MirroringAnAlreadyFlippedPart_UnflipsIt()
        {
            registry = PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec { clipId = IdleClipId, duration = 1f }
                },
                targetCount: 1,
                framesPerVariant: EarFramesPerVariant);
            actor = PlaybackTestActor.CreateActor(testWorld, registry);

            Entity flippedPart = PlaybackTestActor.AddPart(
                testWorld, actor, EarTargetIndex,
                restScale: new Unity.Mathematics.float3(-1f, 1f, 1f),
                restSliceIndex: RoundFrontRestSlice,
                asFlipbookPlane: true);
            testWorld.EntityManager.AddComponentData(
                flippedPart, new PartFacing { viewOffset = 0, mirrorX = true });

            RunSample();

            Assert.Greater(
                testWorld.EntityManager.GetComponentData<TargetPose>(flippedPart).scale.x,
                0f,
                "Negating composes with the authored flip; assigning -1 would swallow it.");
        }

        /// <summary>
        /// Builds an actor whose single ear part rests on the round-front view, with an optional
        /// sprite track on the given clip.
        /// </summary>
        private Entity BuildActorWithEar(PlaybackTestActor.SpriteTrackSpec spriteTrack)
        {
            PlaybackTestActor.SpriteTrackSpec[] idleTracks = spriteTrack == null
                ? new PlaybackTestActor.SpriteTrackSpec[0]
                : new[] { spriteTrack };

            registry = PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = IdleClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Loop,
                        spriteTracks = idleTracks
                    },
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = BlinkClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Loop,
                        spriteTracks = idleTracks
                    }
                },
                targetCount: 1,
                framesPerVariant: EarFramesPerVariant);

            actor = PlaybackTestActor.CreateActor(testWorld, registry);
            return PlaybackTestActor.AddPart(
                testWorld,
                actor,
                EarTargetIndex,
                restSliceIndex: RoundFrontRestSlice,
                asFlipbookPlane: true);
        }

        private void PlayClipOnLayer(int layerIndex, ulong clipId, int clipIndex)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(clipId);
            layer.clipIndex = clipIndex;
            layer.time = 0f;
            layer.advanceStartTime = 0f;
            layer.speed = 1f;
            layer.loop = LoopMode.Loop;
            layer.flags = PlaybackFlags.Active;
            PlaybackTestActor.SetLayer(testWorld, actor, layerIndex, layer);
        }

        private void RunSample()
        {
            elapsedTime += 0.016f;
            testWorld.SetTime(new TimeData(elapsedTime, 0.016f));

            SystemHandle sampleSystem = testWorld.GetOrCreateSystem<TransformSampleSystem>();
            sampleSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
