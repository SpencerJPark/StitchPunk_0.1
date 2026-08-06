// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>AnimLodDistanceSystem</c> and the three effects an LOD level has on sampling —
    /// architecture section 5.10 (build step C4.8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The level arithmetic itself lives in <c>AnimationLodPolicy</c> and is covered in EditMode.
    /// These fixtures are about the wiring: that the distance system writes the level at all, that
    /// it stays off unless asked twice, and that each of the three effects — rate scaling, blend
    /// snapping, pose freezing — actually reaches a sampled pose.
    /// </para>
    /// <para>
    /// Every effect here is invisible in a screenshot. A frozen actor at LOD 3 looks like an actor
    /// that happens not to be moving; a level that never changes looks like a feature nobody
    /// enabled. This is the whole reason §5.10 is tested rather than eyeballed.
    /// </para>
    /// </remarks>
    public sealed class AnimationLodTests
    {
        private const float Tolerance = 1e-4f;

        private const ulong WalkClipId = 100;
        private const int WalkClipIndex = 0;
        private const ulong RunClipId = 200;
        private const int RunClipIndex = 1;

        private const int BodyTargetIndex = 0;

        /// <summary>Ascending squared thresholds: bands break at distance 10, 20 and 30.</summary>
        private static readonly float4 Thresholds = new float4(100f, 400f, 900f, 0f);

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private Entity actor;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("AnimationLodTests");
            elapsedTime = 0d;

            // Two single-key clips at very different positions, so a pose says unambiguously which
            // clip produced it and a lerp between them is a distinct third number.
            registry = PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = WalkClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Loop,
                        transformTracks = new[]
                        {
                            new PlaybackTestActor.TransformTrackSpec
                            {
                                targetIndex = BodyTargetIndex,
                                channels = AnimatedChannels.PositionXY,
                                keys = new[]
                                {
                                    PlaybackTestActor.Key(0f, positionX: 0f),
                                    PlaybackTestActor.Key(1f, positionX: 10f)
                                }
                            }
                        }
                    },
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = RunClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Loop,
                        transformTracks = new[]
                        {
                            new PlaybackTestActor.TransformTrackSpec
                            {
                                targetIndex = BodyTargetIndex,
                                channels = AnimatedChannels.PositionXY,
                                keys = new[] { PlaybackTestActor.Key(0f, positionX: 100f) }
                            }
                        }
                    }
                },
                targetCount: 1);

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

        // -------------------------------------------------------------------------------------
        // AnimLodDistanceSystem
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: never writing the level, which leaves every actor at 0 and the whole feature
        /// inert while the config flag reads as enabled.
        /// </summary>
        [Test]
        public void AnActorInEachDistanceBand_GetsThatBandsLevel()
        {
            GiveActorLod();
            EnableDistanceLod();

            AssertLevelAtDistance(0f, 0);
            AssertLevelAtDistance(15f, 1);
            AssertLevelAtDistance(25f, 2);
            AssertLevelAtDistance(50f, 3);
        }

        /// <summary>
        /// Catches: running regardless of the config flag. Distance LOD is opt-in (§5.10) because a
        /// host that drives LOD from its own crowd budget must not have its levels overwritten —
        /// and the symptom of that would be a host's tuning silently reverting every frame.
        /// </summary>
        [Test]
        public void WithDistanceLodDisabled_TheLevelIsLeftToTheHost()
        {
            GiveActorLod();
            SetCamera(float3.zero);
            CreateConfig(distanceLodEnabled: false);
            PlaceActorAt(new float3(1000f, 0f, 0f));

            // A host's own write, which the system must not touch.
            testWorld.EntityManager.SetComponentData(actor, new AnimLod { level = 1 });
            RunLodSystem();

            Assert.AreEqual(
                1,
                testWorld.EntityManager.GetComponentData<AnimLod>(actor).level,
                "Distance LOD is default-off; a kilometre away must still leave the host's 1 alone.");
        }

        /// <summary>
        /// Catches: measuring from the origin instead of the actor's world position — an error that
        /// is invisible for any rig authored near 0,0 and puts an entire distant town at LOD 3 the
        /// moment content moves away from it.
        /// </summary>
        [Test]
        public void DistanceIsMeasuredFromTheActorsWorldPosition()
        {
            GiveActorLod();
            EnableDistanceLod();

            // Both far from the world origin, but on top of each other.
            SetCamera(new float3(500f, 500f, 0f));
            PlaceActorAt(new float3(500f, 500f, 0f));
            RunLodSystem();

            Assert.AreEqual(
                0,
                testWorld.EntityManager.GetComponentData<AnimLod>(actor).level,
                "An actor sitting on the camera is at level 0 wherever in the world they both are.");
        }

        // -------------------------------------------------------------------------------------
        // The three effects on sampling
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: not applying the level to the sample rate at all. At LOD 1 an uncapped actor is
        /// capped to 30 Hz, so a 60 fps frame sequence samples on every second frame and must leave
        /// the pose alone in between.
        /// </summary>
        /// <remarks>
        /// The middle assertion is not decoration. Without it, "the actor skipped this frame" and
        /// "the actor has never sampled at all" are the same observation, and the fixture would
        /// pass against a LOD level that quantized the actor into stopping outright.
        /// </remarks>
        [Test]
        public void AtLevelOne_AnUncappedActorSkipsAlternateFrames()
        {
            Entity body = AddBody();
            PlayWalkClip();
            AdvanceLayerTimeTo(0.5f);
            GiveActorLod(1);

            // 1/60 s steps against a 30 Hz cap: the index advances on the second frame, not the first.
            AdvanceAndSample(1f / 60f);
            AdvanceAndSample(1f / 60f);
            Assert.AreEqual(
                5f,
                testWorld.EntityManager.GetComponentData<TargetPose>(body).localPosition.x,
                Tolerance,
                "Guard: the actor must actually sample on its own tick, or the skip below is vacuous.");

            ScribblePose(body);
            AdvanceAndSample(1f / 60f);

            Assert.AreEqual(
                -99f,
                testWorld.EntityManager.GetComponentData<TargetPose>(body).localPosition.x,
                Tolerance,
                "A 30 Hz actor must not re-sample 1/60 s after its last tick.");
        }

        /// <summary>
        /// The contrast that makes the fixture above about LOD rather than about quantization in
        /// general. Catches: applying a cap at level 0 — the same frame sequence must sample every
        /// frame for an actor at full quality.
        /// </summary>
        [Test]
        public void AtLevelZero_TheSameFrameSequenceSamplesEveryFrame()
        {
            Entity body = AddBody();
            PlayWalkClip();
            AdvanceLayerTimeTo(0.5f);

            AdvanceAndSample(1f / 60f);
            ScribblePose(body);
            AdvanceAndSample(1f / 60f);

            Assert.AreEqual(
                5f,
                testWorld.EntityManager.GetComponentData<TargetPose>(body).localPosition.x,
                Tolerance,
                "An uncapped level-0 actor samples on every frame, so the scribble is overwritten.");
        }

        /// <summary>
        /// Catches: not passing the snap through to <c>CompositeLayers</c>. Mid-blend at level 2 the
        /// pose must be the incoming clip outright, not the halfway lerp a level-0 actor shows.
        /// </summary>
        [Test]
        public void AtLevelTwo_ACrossfadeIsRenderedAsAHardCut()
        {
            Entity body = AddBody();
            StartBlendFromWalkToRun(blendElapsed: 0.5f, blendDuration: 1f);
            GiveActorLod(2);

            AdvanceAndSample(1f);

            Assert.AreEqual(
                100f,
                testWorld.EntityManager.GetComponentData<TargetPose>(body).localPosition.x,
                Tolerance,
                "Half way through, a snapped blend shows the run clip; the lerp would be 50.");
        }

        /// <summary>
        /// The same layer state at level 0, which is what makes the fixture above mean something.
        /// Catches: snapping unconditionally, which would remove crossfading from the whole package
        /// while every LOD test still passed.
        /// </summary>
        [Test]
        public void AtLevelZero_TheSameCrossfadeStillLerps()
        {
            Entity body = AddBody();
            StartBlendFromWalkToRun(blendElapsed: 0.5f, blendDuration: 1f);

            AdvanceAndSample(1f);

            Assert.AreEqual(
                50f,
                testWorld.EntityManager.GetComponentData<TargetPose>(body).localPosition.x,
                Tolerance,
                "lerp(walk 0, run 100, 0.5).");
        }

        /// <summary>
        /// Catches: level 3 behaving as level 2. A frozen actor holds its pose while playback time
        /// keeps advancing underneath — that divergence is the entire point of the level, and
        /// nothing about it is visible on screen.
        /// </summary>
        [Test]
        public void AtLevelThree_ThePoseHoldsWhileTimeAdvances()
        {
            Entity body = AddBody();
            PlayWalkClip();
            AdvanceLayerTimeTo(0.2f);
            GiveActorLod(3);

            AdvanceAndSample(0.1f);
            Assert.AreEqual(
                2f,
                testWorld.EntityManager.GetComponentData<TargetPose>(body).localPosition.x,
                Tolerance,
                "Guard: level 3 still takes its first sample.");

            AdvanceLayerTimeTo(0.9f);
            AdvanceAndSample(0.5f);

            Assert.AreEqual(
                2f,
                testWorld.EntityManager.GetComponentData<TargetPose>(body).localPosition.x,
                Tolerance,
                "The clip did not change, so the pose holds at 2 — an unfrozen actor would read 9.");
        }

        /// <summary>
        /// The "unless the layer's clip changes" half of §5.10's level 3. Catches: freezing
        /// unconditionally, which strands a distant actor on the pose of a clip it stopped playing
        /// — a soldier still standing at ease after the whole line has drawn swords.
        /// </summary>
        [Test]
        public void AtLevelThree_AClipChangeUnfreezesThePose()
        {
            Entity body = AddBody();
            PlayWalkClip();
            GiveActorLod(3);
            AdvanceAndSample(0.1f);

            SwitchToRunClip();
            AdvanceAndSample(0.1f);

            Assert.AreEqual(
                100f,
                testWorld.EntityManager.GetComponentData<TargetPose>(body).localPosition.x,
                Tolerance,
                "A different clip is exactly the condition that must break the freeze.");
        }

        /// <summary>
        /// Catches: applying the freeze at levels below 3, or latching it once an actor has ever
        /// been frozen. An actor walking toward the camera would then stay stuck on its LOD-3 pose
        /// after arriving — the most visible failure this feature can produce, and the one a suite
        /// that only ever <em>raises</em> the level would never see.
        /// </summary>
        [Test]
        public void DroppingBackFromLevelThree_ResumesSamplingImmediately()
        {
            Entity body = AddBody();
            PlayWalkClip();
            GiveActorLod(3);
            AdvanceAndSample(0.1f);

            SetLodLevel(0);
            AdvanceLayerTimeTo(0.5f);
            AdvanceAndSample(1f / 60f);

            Assert.AreEqual(
                5f,
                testWorld.EntityManager.GetComponentData<TargetPose>(body).localPosition.x,
                Tolerance,
                "Back at level 0 the actor samples on the ordinary rate rule, with no clip change.");
        }

        /// <summary>
        /// Catches: taking <see cref="AnimLod"/> as a job parameter instead of a lookup, which
        /// enrols the sampling job in an <c>All</c> query for an opt-in component — every actor
        /// that never asked for LOD would stop sampling entirely, silently, with no error. This is
        /// the single most destructive mistake available in C4.8.
        /// </summary>
        [Test]
        public void AnActorWithoutAnimLod_SamplesNormally()
        {
            Entity body = AddBody();
            PlayWalkClip();

            Assert.IsFalse(
                testWorld.EntityManager.HasComponent<AnimLod>(actor),
                "Guard: the default fixture actor must not carry AnimLod, or this proves nothing.");

            AdvanceLayerTimeTo(0.5f);
            AdvanceAndSample(1f / 60f);

            Assert.AreEqual(
                5f,
                testWorld.EntityManager.GetComponentData<TargetPose>(body).localPosition.x,
                Tolerance);
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers
        // -------------------------------------------------------------------------------------

        private Entity AddBody()
        {
            return PlaybackTestActor.AddPart(testWorld, actor, BodyTargetIndex);
        }

        private void GiveActorLod(byte level = 0)
        {
            testWorld.EntityManager.AddComponentData(actor, new AnimLod { level = level });
        }

        private void SetLodLevel(byte level)
        {
            testWorld.EntityManager.SetComponentData(actor, new AnimLod { level = level });
        }

        private void CreateConfig(bool distanceLodEnabled)
        {
            testWorld.EntityManager.CreateSingleton(
                new AnimationToolkitConfig
                {
                    defaultSampleRateHz = 0f,
                    distanceLodEnabled = distanceLodEnabled,
                    lodDistancesSq = Thresholds
                },
                "TestConfig");
        }

        /// <summary>
        /// Creates the camera singleton, or moves the existing one. <c>CreateSingleton</c> throws
        /// on a second call, and a fixture that both enables LOD and then repositions the camera is
        /// the ordinary shape here.
        /// </summary>
        private void SetCamera(float3 position)
        {
            EntityManager entityManager = testWorld.EntityManager;
            EntityQuery cameraQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<AnimationToolkitCameraData>());

            if (cameraQuery.CalculateEntityCount() > 0)
            {
                cameraQuery.SetSingleton(new AnimationToolkitCameraData { position = position });
                return;
            }
            entityManager.CreateSingleton(
                new AnimationToolkitCameraData { position = position },
                "TestCamera");
        }

        private void EnableDistanceLod()
        {
            SetCamera(float3.zero);
            CreateConfig(distanceLodEnabled: true);
        }

        private void PlaceActorAt(float3 position)
        {
            testWorld.EntityManager.SetComponentData(
                actor,
                new LocalToWorld { Value = float4x4.Translate(position) });
        }

        private void AssertLevelAtDistance(float distance, byte expectedLevel)
        {
            PlaceActorAt(new float3(distance, 0f, 0f));
            RunLodSystem();

            Assert.AreEqual(
                expectedLevel,
                testWorld.EntityManager.GetComponentData<AnimLod>(actor).level,
                $"Distance {distance} belongs to level {expectedLevel}.");
        }

        private void PlayWalkClip()
        {
            PlaybackTestActor.SetLayer(testWorld, actor, 0, BuildActiveLayer(WalkClipId, WalkClipIndex));
        }

        private void SwitchToRunClip()
        {
            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            layer.clip = new ClipId(RunClipId);
            layer.clipIndex = RunClipIndex;
            layer.time = 0f;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);
        }

        private void StartBlendFromWalkToRun(float blendElapsed, float blendDuration)
        {
            PlaybackLayer layer = BuildActiveLayer(RunClipId, RunClipIndex);
            layer.previousClip = new ClipId(WalkClipId);
            layer.previousClipIndex = WalkClipIndex;
            layer.previousTime = 0f;
            layer.previousLoop = LoopMode.Loop;
            layer.blendElapsed = blendElapsed;
            layer.blendDuration = blendDuration;
            layer.flags |= PlaybackFlags.Blending;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);
        }

        private void AdvanceLayerTimeTo(float playbackTime)
        {
            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            layer.time = playbackTime;
            layer.advanceStartTime = playbackTime;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);
        }

        private static PlaybackLayer BuildActiveLayer(ulong clipId, int clipIndex)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(clipId);
            layer.clipIndex = clipIndex;
            layer.time = 0f;
            layer.advanceStartTime = 0f;
            layer.speed = 1f;
            layer.loop = LoopMode.Loop;
            layer.flags = PlaybackFlags.Active;
            return layer;
        }

        /// <summary>
        /// Overwrites a sampled pose with a value nothing produces, so "the actor did not sample"
        /// is distinguishable from "the actor sampled the same number again".
        /// </summary>
        private void ScribblePose(Entity partEntity)
        {
            testWorld.EntityManager.SetComponentData(
                partEntity,
                new TargetPose { localPosition = new float3(-99f, -99f, 0f) });
        }

        private void RunLodSystem()
        {
            SystemHandle lodSystem = testWorld.GetOrCreateSystem<AnimLodDistanceSystem>();
            lodSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void AdvanceAndSample(float deltaTime)
        {
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));

            SystemHandle sampleSystem = testWorld.GetOrCreateSystem<TransformSampleSystem>();
            sampleSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
