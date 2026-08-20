// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>VatMaterialSystem</c> — the vertex-animation-texture technique of architecture
    /// sections 5.8 and 6.5 (build step C4.7).
    /// </summary>
    /// <remarks>
    /// A VAT part has no <c>TargetPose</c> to inspect: the whole technique is "turn playback time
    /// into a row index and hand it to the shader", so every one of these fixtures asserts on the
    /// three per-instance properties. The arithmetic under test is
    /// <c>vatFrameStart + clamp(MapTime(time) * vatFps, 0, vatFrameCount - 1)</c>, and the reason it
    /// goes through <see cref="ClipSampler.MapTime"/> rather than a local <c>fmod</c> is that the
    /// same clip must land on the same pose whichever technique renders it (§5.11).
    /// </remarks>
    public sealed class VatMaterialSystemTests
    {
        private const float Tolerance = 1e-4f;

        /// <summary>1 s at 24 fps plus the duplicated loop-safe frame, based at global row 100.</summary>
        private const ulong RunClipId = 300;
        private const int RunClipIndex = 0;
        private const int RunFrameStart = 100;
        private const int RunFrameCount = 25;
        private const float RunFps = 24f;

        /// <summary>2 s at 6 fps plus the duplicated loop-safe frame, based at global row 200.</summary>
        private const ulong SwingClipId = 400;
        private const int SwingClipIndex = 1;
        private const int SwingFrameStart = 200;
        private const int SwingFrameCount = 13;
        private const float SwingFps = 6f;

        /// <summary>A clip the VAT bake never touched — a skinned-mesh-only clip on a VAT rig.</summary>
        private const ulong TalkClipId = 500;
        private const int TalkClipIndex = 2;

        private const int BodyTargetIndex = 0;
        private const int CapeTargetIndex = 1;

        private const float SentinelFrame = -777f;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private Entity actor;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("VatMaterialSystemTests");
            registry = PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = RunClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Loop,
                        vatFrameStart = RunFrameStart,
                        vatFrameCount = RunFrameCount,
                        vatFps = RunFps
                    },
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = SwingClipId,
                        duration = 2f,
                        defaultLoop = LoopMode.Loop,
                        vatFrameStart = SwingFrameStart,
                        vatFrameCount = SwingFrameCount,
                        vatFps = SwingFps
                    },
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = TalkClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Loop
                    }
                },
                targetCount: 2);
            actor = PlaybackTestActor.CreateActor(testWorld, registry);
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
        /// Catches: not publishing at all, publishing a <em>local</em> frame index, or publishing
        /// normalized time instead of a frame. Half a second into a 24 fps clip based at row 100 is
        /// row 112 — a number no two of those mistakes share.
        /// </summary>
        [Test]
        public void ADrivingLayersTime_ReachesTheFrameAProperty()
        {
            Entity body = AddVatPart(BodyTargetIndex, drivingLayerIndex: 0);
            ScribbleProperties(body);
            PlayOnLayer(0, RunClipId, RunClipIndex, playbackTime: 0.5f);

            RunSystem();

            Assert.AreEqual(
                RunFrameStart + 12f,
                testWorld.EntityManager.GetComponentData<VatFrameAProperty>(body).Value,
                Tolerance,
                "0.5 s x 24 fps = local frame 12, offset by the clip's global base row.");
        }

        /// <summary>
        /// Catches: skipping <see cref="ClipSampler.MapTime"/> and multiplying raw playback time by
        /// the frame rate. A looping VAT clip would then stick on its last row forever while the
        /// transform half of the same actor kept looping — the drift §5.11 exists to prevent. Raw
        /// time here would clamp to row 124; the wrapped time is row 106.
        /// </summary>
        [Test]
        public void ALoopingClipPastItsEnd_WrapsRatherThanHoldingTheLastFrame()
        {
            Entity body = AddVatPart(BodyTargetIndex, drivingLayerIndex: 0);
            ScribbleProperties(body);
            PlayOnLayer(0, RunClipId, RunClipIndex, playbackTime: 1.25f);

            RunSystem();

            Assert.AreEqual(
                RunFrameStart + 6f,
                testWorld.EntityManager.GetComponentData<VatFrameAProperty>(body).Value,
                Tolerance,
                "1.25 s wraps to 0.25 s, which is local frame 6 — not a clamp to the final row.");
        }

        /// <summary>
        /// Catches: dropping the crossfade path, which makes a VAT actor hard-cut between clips
        /// while its sprite and transform parts blend smoothly. Frame B must name the outgoing
        /// clip's row and the weight must be the layer's own blend progress.
        /// </summary>
        [Test]
        public void ABlendingLayer_PublishesTheOutgoingClipAsFrameBWithItsWeight()
        {
            Entity body = AddVatPart(BodyTargetIndex, drivingLayerIndex: 0);
            ScribbleProperties(body);

            PlaybackLayer layer = BuildActiveLayer(RunClipId, RunClipIndex, 0.5f, LoopMode.Loop);
            layer.previousClip = new ClipId(SwingClipId);
            layer.previousClipIndex = SwingClipIndex;
            layer.previousTime = 1f;
            layer.previousSpeed = 1f;
            layer.previousLoop = LoopMode.Loop;
            layer.blendDuration = 0.4f;
            layer.blendElapsed = 0.1f;
            layer.flags |= PlaybackFlags.Blending;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);

            RunSystem();

            EntityManager entityManager = testWorld.EntityManager;
            Assert.AreEqual(
                RunFrameStart + 12f, entityManager.GetComponentData<VatFrameAProperty>(body).Value, Tolerance,
                "A is the incoming clip.");
            Assert.AreEqual(
                SwingFrameStart + 6f, entityManager.GetComponentData<VatFrameBProperty>(body).Value, Tolerance,
                "B is the outgoing clip at its own time, rate and base row.");
            Assert.AreEqual(
                0.25f, entityManager.GetComponentData<VatBlendProperty>(body).Value, Tolerance,
                "0.1 s of a 0.4 s blend is a quarter of the way across.");
        }

        /// <summary>
        /// Catches: leaving <c>_VatFrameB</c> at 0 — or at whatever the previous blend left — when
        /// nothing is blending. With the weight at 0 a correct shader ignores B, but any shader that
        /// lerps before testing the weight would snap the mesh to the first row of the whole texture.
        /// Pointing B at A makes the blend a no-op under every reading.
        /// </summary>
        [Test]
        public void ANonBlendingLayer_PointsFrameBAtFrameAWithNoWeight()
        {
            Entity body = AddVatPart(BodyTargetIndex, drivingLayerIndex: 0);
            ScribbleProperties(body);
            PlayOnLayer(0, RunClipId, RunClipIndex, playbackTime: 0.5f);

            RunSystem();

            EntityManager entityManager = testWorld.EntityManager;
            Assert.AreEqual(
                RunFrameStart + 12f, entityManager.GetComponentData<VatFrameBProperty>(body).Value, Tolerance,
                "B mirrors A rather than pointing at row 0.");
            Assert.AreEqual(
                0f, entityManager.GetComponentData<VatBlendProperty>(body).Value, Tolerance);
        }

        /// <summary>
        /// Catches: reading layer 0 for every part instead of <see cref="VatDriven.layerIndex"/>.
        /// A cape on its own layer is the whole reason the field is per part; hard-coding layer 0
        /// would leave both parts correct on any single-layer actor and silently wrong here.
        /// </summary>
        [Test]
        public void EachPart_FollowsItsOwnDrivingLayer()
        {
            Entity body = AddVatPart(BodyTargetIndex, drivingLayerIndex: 0);
            Entity cape = AddVatPart(CapeTargetIndex, drivingLayerIndex: 1);
            ScribbleProperties(body);
            ScribbleProperties(cape);
            PlayOnLayer(0, RunClipId, RunClipIndex, playbackTime: 0.5f);
            PlayOnLayer(1, SwingClipId, SwingClipIndex, playbackTime: 1f);

            RunSystem();

            EntityManager entityManager = testWorld.EntityManager;
            Assert.AreEqual(
                RunFrameStart + 12f, entityManager.GetComponentData<VatFrameAProperty>(body).Value, Tolerance);
            Assert.AreEqual(
                SwingFrameStart + 6f, entityManager.GetComponentData<VatFrameAProperty>(cape).Value, Tolerance);
        }

        /// <summary>
        /// Catches: a clip with no baked VAT range publishing frame 0, which points at the first row
        /// of some other clip's block — a mesh that snaps to an unrelated pose the moment a
        /// skinned-only clip plays. The last frame must simply hold, because a VAT mesh has no rest
        /// pose to fall back to.
        /// </summary>
        [Test]
        public void AClipWithNoVatRange_HoldsTheLastFrameAndZeroesTheBlend()
        {
            Entity body = AddVatPart(BodyTargetIndex, drivingLayerIndex: 0);
            ScribbleProperties(body);
            testWorld.EntityManager.SetComponentData(body, new VatBlendProperty { Value = 0.5f });
            PlayOnLayer(0, TalkClipId, TalkClipIndex, playbackTime: 0.5f);

            RunSystem();

            EntityManager entityManager = testWorld.EntityManager;
            Assert.AreEqual(
                SentinelFrame, entityManager.GetComponentData<VatFrameAProperty>(body).Value, Tolerance,
                "The frame is held, not reset to the texture's first row.");
            Assert.AreEqual(
                0f, entityManager.GetComponentData<VatBlendProperty>(body).Value, Tolerance,
                "The weight is forced to 0 so the shader cannot keep interpolating toward a stale B.");
        }

        /// <summary>
        /// Catches: dropping the active-flag test. A stopped layer still carries its last clip index
        /// and time, so without the flag check the mesh would keep being re-addressed from state
        /// nothing is advancing — indistinguishable from playing until the layer is reused.
        /// </summary>
        [Test]
        public void AStoppedLayer_LeavesTheFrameWhereItWas()
        {
            Entity body = AddVatPart(BodyTargetIndex, drivingLayerIndex: 0);
            ScribbleProperties(body);

            PlaybackLayer layer = BuildActiveLayer(RunClipId, RunClipIndex, 0.5f, LoopMode.Loop);
            layer.flags = PlaybackFlags.None;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);

            RunSystem();

            Assert.AreEqual(
                SentinelFrame,
                testWorld.EntityManager.GetComponentData<VatFrameAProperty>(body).Value,
                Tolerance);
        }

        /// <summary>
        /// Catches: dropping the <see cref="AnimVisible"/> gate. Off-screen VAT parts would keep
        /// uploading per-instance properties — right values, computed and shipped for nothing, which
        /// is exactly the cost the visibility split exists to avoid and exactly the kind of
        /// regression no visual check can see.
        /// </summary>
        [Test]
        public void AnInvisiblePart_IsNotPublished()
        {
            Entity body = AddVatPart(BodyTargetIndex, drivingLayerIndex: 0);
            ScribbleProperties(body);
            PlayOnLayer(0, RunClipId, RunClipIndex, playbackTime: 0.5f);
            testWorld.EntityManager.SetComponentEnabled<AnimVisible>(body, false);

            RunSystem();

            Assert.AreEqual(
                SentinelFrame,
                testWorld.EntityManager.GetComponentData<VatFrameAProperty>(body).Value,
                Tolerance);
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers
        // -------------------------------------------------------------------------------------

        private Entity AddVatPart(int targetIndex, int drivingLayerIndex)
        {
            return PlaybackTestActor.AddPart(
                testWorld,
                actor,
                targetIndex,
                restPosition: new float3(targetIndex, 0f, 0f),
                vatDrivingLayerIndex: drivingLayerIndex);
        }

        /// <summary>
        /// Overwrites the three properties with values nothing would ever produce, so a fixture
        /// asserting a published number is asserting that a write happened. The baker seeds them at
        /// 0, which several of the failure modes under test also produce.
        /// </summary>
        private void ScribbleProperties(Entity partEntity)
        {
            EntityManager entityManager = testWorld.EntityManager;
            entityManager.SetComponentData(partEntity, new VatFrameAProperty { Value = SentinelFrame });
            entityManager.SetComponentData(partEntity, new VatFrameBProperty { Value = SentinelFrame });
            entityManager.SetComponentData(partEntity, new VatBlendProperty { Value = SentinelFrame });
        }

        private static PlaybackLayer BuildActiveLayer(
            ulong clipId, int clipIndex, float playbackTime, LoopMode loopMode)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(clipId);
            layer.clipIndex = clipIndex;
            layer.time = playbackTime;
            layer.advanceStartTime = playbackTime;
            layer.speed = 1f;
            layer.loop = loopMode;
            layer.flags = PlaybackFlags.Active;
            return layer;
        }

        private void PlayOnLayer(int layerIndex, ulong clipId, int clipIndex, float playbackTime)
        {
            PlaybackTestActor.SetLayer(
                testWorld,
                actor,
                layerIndex,
                BuildActiveLayer(clipId, clipIndex, playbackTime, LoopMode.Loop));
        }

        private void RunSystem()
        {
            SystemHandle vatSystem = testWorld.GetOrCreateSystem<VatMaterialSystem>();
            vatSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
