// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>SpriteMaterialSystem</c> — the flipbook technique of architecture section 5.7
    /// (build step C4.6).
    /// </summary>
    /// <remarks>
    /// Frame <em>selection</em> (hold the last key, the "−1 = no change" convention) belongs to
    /// <c>ClipSampler.SampleSpriteTrack</c> and is covered in EditMode. These fixtures are about the
    /// publish step: that the sampled frame reaches the per-instance shader properties at all, that
    /// both modes are carried, and that the value published is never one the shader cannot use.
    /// </remarks>
    public sealed class SpriteMaterialSystemTests
    {
        private const float Tolerance = 1e-4f;

        private const ulong FlipClipId = 300;
        private const int FlipClipIndex = 0;

        private const int BodyTargetIndex = 0;
        private const int RestSliceIndex = 4;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private Entity actor;
        private double elapsedTime;

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
        /// Catches: not publishing the slice at all, which leaves every flipbook part showing the
        /// frame the baker seeded — an animation that is sampling correctly and rendering frame one
        /// forever.
        /// </summary>
        [Test]
        public void ASampledSliceFrame_ReachesTheShaderProperty()
        {
            BuildActor(new PlaybackTestActor.SpriteTrackSpec
            {
                targetIndex = BodyTargetIndex,
                mode = SpriteFrameMode.Slice,
                keys = new[] { PlaybackTestActor.SliceKey(0f, 9) }
            });
            Entity body = PlaybackTestActor.AddPart(
                testWorld, actor, BodyTargetIndex, restSliceIndex: RestSliceIndex, asFlipbookPlane: true);
            ScribbleProperties(body);
            PlayFlipClip();

            RunSampleAndPublish();

            Assert.AreEqual(
                9f,
                testWorld.EntityManager.GetComponentData<SpriteSliceProperty>(body).Value,
                Tolerance);
        }

        /// <summary>
        /// Catches: publishing only the slice and leaving the atlas rect behind. A part driven in
        /// atlas mode would render the full texture regardless of the clip — every frame identical,
        /// with the sampler working perfectly.
        /// </summary>
        [Test]
        public void ASampledAtlasFrame_ReachesTheShaderProperty()
        {
            float4 authoredRect = new float4(0.25f, 0.5f, 0.75f, 0.125f);
            BuildActor(new PlaybackTestActor.SpriteTrackSpec
            {
                targetIndex = BodyTargetIndex,
                mode = SpriteFrameMode.AtlasRect,
                keys = new[] { PlaybackTestActor.AtlasKey(0f, authoredRect) }
            });
            Entity body = PlaybackTestActor.AddPart(
                testWorld, actor, BodyTargetIndex, restSliceIndex: RestSliceIndex, asFlipbookPlane: true);
            ScribbleProperties(body);
            PlayFlipClip();

            RunSampleAndPublish();

            float4 published = testWorld.EntityManager.GetComponentData<AtlasFrameProperty>(body).Value;
            Assert.AreEqual(authoredRect.x, published.x, Tolerance);
            Assert.AreEqual(authoredRect.y, published.y, Tolerance);
            Assert.AreEqual(authoredRect.z, published.z, Tolerance);
            Assert.AreEqual(authoredRect.w, published.w, Tolerance);
        }

        /// <summary>
        /// <strong>The reason section 5.7 forbids a <c>sliceIndex &gt;= 0</c> guard.</strong>
        /// Catches: a regression in any of the four places that keep a pose slice non-negative —
        /// the baker's <c>max(0, …)</c> clamp, <c>RestToPose</c>'s seed, <c>SampleSpriteTrack</c>'s
        /// own <c>&gt;= 0</c> test, or <c>LerpPose</c> picking whole values. A −1 authored key means
        /// "hold the current frame", so the rest slice must still be showing. If this ever fails,
        /// the fix is upstream; adding a guard in the material system would hide it.
        /// </summary>
        [Test]
        public void ANegativeSliceKey_LeavesTheRestFrameShowing()
        {
            BuildActor(new PlaybackTestActor.SpriteTrackSpec
            {
                targetIndex = BodyTargetIndex,
                mode = SpriteFrameMode.Slice,
                keys = new[] { PlaybackTestActor.SliceKey(0f, -1) }
            });
            Entity body = PlaybackTestActor.AddPart(
                testWorld, actor, BodyTargetIndex, restSliceIndex: RestSliceIndex, asFlipbookPlane: true);
            ScribbleProperties(body);
            PlayFlipClip();

            RunSampleAndPublish();

            Assert.AreEqual(
                (float)RestSliceIndex,
                testWorld.EntityManager.GetComponentData<SpriteSliceProperty>(body).Value,
                Tolerance,
                "A -1 key holds the frame, so the rest slice is what reaches the shader — never -1.");
        }

        /// <summary>
        /// Catches: publishing 0, or stale memory, for a part no clip drives. Host design and skin
        /// systems change a part's base look by writing <c>TargetRestPose.restSliceIndex</c>
        /// (section 5.7), so an undriven part must publish exactly that.
        /// </summary>
        [Test]
        public void APartNoTrackDrives_PublishesItsRestFrame()
        {
            BuildActor(new PlaybackTestActor.SpriteTrackSpec
            {
                // Bound to a target this actor has no part for.
                targetIndex = 1,
                mode = SpriteFrameMode.Slice,
                keys = new[] { PlaybackTestActor.SliceKey(0f, 9) }
            });
            Entity body = PlaybackTestActor.AddPart(
                testWorld, actor, BodyTargetIndex, restSliceIndex: RestSliceIndex, asFlipbookPlane: true);
            ScribbleProperties(body);
            PlayFlipClip();

            RunSampleAndPublish();

            Assert.AreEqual(
                (float)RestSliceIndex,
                testWorld.EntityManager.GetComponentData<SpriteSliceProperty>(body).Value,
                Tolerance);
        }

        /// <summary>
        /// Catches: leaving the atlas rect at whatever memory held for a slice-mode part. The
        /// identity rect is the full texture, so a slice-driven plane renders its whole frame rather
        /// than an arbitrary sub-rect.
        /// </summary>
        [Test]
        public void ASliceModePart_PublishesTheFullTextureAsItsAtlasRect()
        {
            BuildActor(new PlaybackTestActor.SpriteTrackSpec
            {
                targetIndex = BodyTargetIndex,
                mode = SpriteFrameMode.Slice,
                keys = new[] { PlaybackTestActor.SliceKey(0f, 9) }
            });
            Entity body = PlaybackTestActor.AddPart(
                testWorld, actor, BodyTargetIndex, restSliceIndex: RestSliceIndex, asFlipbookPlane: true);
            ScribbleProperties(body);
            PlayFlipClip();

            RunSampleAndPublish();

            float4 published = testWorld.EntityManager.GetComponentData<AtlasFrameProperty>(body).Value;
            Assert.AreEqual(ClipSampler.IdentityAtlasRect.x, published.x, Tolerance);
            Assert.AreEqual(ClipSampler.IdentityAtlasRect.z, published.z, Tolerance);
        }

        /// <summary>
        /// Catches: dropping the <see cref="AnimVisible"/> gate. Publishing for off-screen parts is
        /// the cost the visibility split exists to avoid, and it is invisible — the values are right,
        /// merely computed and uploaded for nothing.
        /// </summary>
        [Test]
        public void AnInvisiblePart_IsNotPublished()
        {
            BuildActor(new PlaybackTestActor.SpriteTrackSpec
            {
                targetIndex = BodyTargetIndex,
                mode = SpriteFrameMode.Slice,
                keys = new[] { PlaybackTestActor.SliceKey(0f, 9) }
            });
            Entity body = PlaybackTestActor.AddPart(
                testWorld, actor, BodyTargetIndex, restSliceIndex: RestSliceIndex, asFlipbookPlane: true);
            ScribbleProperties(body);
            PlayFlipClip();
            RunSample();

            testWorld.EntityManager.SetComponentEnabled<AnimVisible>(body, false);
            testWorld.EntityManager.SetComponentData(body, new SpriteSliceProperty { Value = 42f });
            RunPublish();

            Assert.AreEqual(
                42f,
                testWorld.EntityManager.GetComponentData<SpriteSliceProperty>(body).Value,
                Tolerance);
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers
        // -------------------------------------------------------------------------------------

        /// <summary>Builds a one-clip registry carrying <paramref name="spriteTrack"/>, plus the actor.</summary>
        private void BuildActor(PlaybackTestActor.SpriteTrackSpec spriteTrack)
        {
            testWorld = new World("SpriteMaterialSystemTests");
            elapsedTime = 0d;
            registry = PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = FlipClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Loop,
                        spriteTracks = new[] { spriteTrack }
                    }
                },
                targetCount: 2);
            actor = PlaybackTestActor.CreateActor(testWorld, registry);
        }

        private void PlayFlipClip()
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(FlipClipId);
            layer.clipIndex = FlipClipIndex;
            layer.time = 0f;
            layer.advanceStartTime = 0f;
            layer.speed = 1f;
            layer.loop = LoopMode.Loop;
            layer.flags = PlaybackFlags.Active;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);
        }

        /// <summary>
        /// Overwrites both shader properties with values nothing would ever produce, so that a test
        /// asserting the published value is asserting that a write happened. The baker seeds these
        /// from the rest pose, so a fixture that skipped this step would pass unchanged against a
        /// system that published nothing at all.
        /// </summary>
        private void ScribbleProperties(Entity partEntity)
        {
            testWorld.EntityManager.SetComponentData(partEntity, new SpriteSliceProperty { Value = -999f });
            testWorld.EntityManager.SetComponentData(
                partEntity,
                new AtlasFrameProperty { Value = new float4(-9f, -9f, -9f, -9f) });
        }

        private void RunSample()
        {
            elapsedTime += 0.016f;
            testWorld.SetTime(new TimeData(elapsedTime, 0.016f));

            SystemHandle sampleSystem = testWorld.GetOrCreateSystem<TransformSampleSystem>();
            sampleSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void RunPublish()
        {
            SystemHandle spriteSystem = testWorld.GetOrCreateSystem<SpriteMaterialSystem>();
            spriteSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void RunSampleAndPublish()
        {
            RunSample();
            RunPublish();
        }
    }
}
