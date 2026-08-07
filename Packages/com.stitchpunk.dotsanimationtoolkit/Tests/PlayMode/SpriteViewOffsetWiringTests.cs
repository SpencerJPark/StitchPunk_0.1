// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
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
    public sealed class SpriteViewOffsetWiringTests
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
            testWorld = new World("SpriteViewOffsetWiringTests");
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
        /// Catches: applying the facing term to parts that never opted in. <see cref="SpriteViewOffset"/>
        /// is optional (A23 precedent), and a part without it must sample exactly as it did before
        /// A37 existed.
        /// </summary>
        [Test]
        public void APartWithoutTheComponent_SamplesItsRestSliceUnchanged()
        {
            Entity ear = BuildActorWithEar(spriteTrack: null);

            Assert.IsFalse(
                testWorld.EntityManager.HasComponent<SpriteViewOffset>(ear),
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
            testWorld.EntityManager.AddComponentData(part, new SpriteViewOffset { value = viewOffset });
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
