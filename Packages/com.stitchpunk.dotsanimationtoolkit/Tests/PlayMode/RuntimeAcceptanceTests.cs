// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// The clauses of the M3 PlayMode acceptance list (architecture section 8, restated by section
    /// 11.2) that no per-phase fixture built in C4.2–C4.8 already pins — build step C4.9.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This file is deliberately not a restatement of the acceptance list.</strong> Most of
    /// section 8 M3 is covered, clause by clause, by the fixtures that shipped with the system each
    /// clause describes; re-asserting those here would add rows to a test count without adding
    /// coverage, and would put two fixtures in the position of failing together for one defect. The
    /// clause-to-fixture map lives in <c>Docs/AnimationToolkit/Phase_C4_9_Acceptance.md</c>, and
    /// every fixture below is one of the rows that map records as uncovered.
    /// </para>
    /// <para>
    /// What they have in common is that each spans <em>more than one system</em>, which is why none
    /// of them fell out of a single build step. Re-binding and animating is the binding group plus
    /// the whole logic and presentation chain; the visibility contract is only meaningful when the
    /// ungated logic group and the gated presentation group run together; the LOD-2 mid-blend swap
    /// needs the blend timer in one group and the snap in another. The lesson this package keeps
    /// re-teaching — that a defect is usually invisible from inside the system that contains it —
    /// says these are exactly the fixtures worth having.
    /// </para>
    /// </remarks>
    public sealed class RuntimeAcceptanceTests
    {
        private const float Tolerance = 1e-4f;

        private const ulong WalkClipId = 100;
        private const int WalkClipIndex = 0;
        private const ulong RunClipId = 200;
        private const int RunClipIndex = 1;

        private const int BodyTargetIndex = 0;

        /// <summary>A pose value nothing in these fixtures can produce, so "not written" is visible.</summary>
        private const float ScribbledPoseX = -999f;

        /// <summary>
        /// Rest poses are non-identity throughout. At the origin with unit scale, "composited from
        /// rest" and "wrote the raw key and lost the rest pose" are the same number — the shape of a
        /// test that passes under both the correct and the broken implementation (amendment A31).
        /// </summary>
        private static readonly float3 BodyRestPosition = new float3(0.5f, 1.25f, 0.1f);

        private World testWorld;
        private double elapsedTime;
        private readonly List<BlobAssetReference<ClipRegistryBlob>> ownedRegistries =
            new List<BlobAssetReference<ClipRegistryBlob>>();

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("RuntimeAcceptanceTests");
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

            for (int registryIndex = 0; registryIndex < ownedRegistries.Count; registryIndex++)
            {
                BlobAssetReference<ClipRegistryBlob> registry = ownedRegistries[registryIndex];
                if (registry.IsCreated)
                {
                    registry.Dispose();
                }
            }
            ownedRegistries.Clear();
        }

        // -------------------------------------------------------------------------------------
        // Section 8 M3: "ECB-instantiated actor re-binds parts (RigBinding) and animates"
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// The full spawn path in one frame: an actor instantiated through an
        /// <see cref="EntityCommandBuffer"/> re-binds its parts and then puts the clip's pose on its
        /// own transform.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Catches: the binding group not running ahead of the logic and presentation groups on a
        /// spawn frame, and any part of the chain from a queued command to an applied transform
        /// failing to reach a freshly instantiated actor. Every other fixture in the package seeds a
        /// hand-built actor; this is the only one that starts from an <see cref="EntityCommandBuffer"/>
        /// playback, which is how a game actually produces one.
        /// </para>
        /// <para>
        /// <strong>It deliberately does not claim to discriminate the part rebuild, because on this
        /// path nothing can</strong> — see <c>amendment A35</c>. <c>Instantiate</c> remaps entity
        /// references inside dynamic buffers as well as inside components whenever the target is a
        /// member of the instantiated <c>LinkedEntityGroup</c>, so the instance's
        /// <see cref="RigPartRef"/> buffer already names its own parts before
        /// <c>RigBindingSystem</c> runs. An earlier draft of this fixture asserted the opposite as a
        /// guard, on the strength of section 5.3's stated rationale, and failed with the instance
        /// already correctly bound.
        /// </para>
        /// <para>
        /// The prefab assertion is kept regardless: it is cheap, and if a future Entities release
        /// stops remapping buffers it is the assertion that says so.
        /// </para>
        /// </remarks>
        [Test]
        public void AnEcbInstantiatedActor_RebindsItsPartsAndThenAnimatesThem()
        {
            BlobAssetReference<ClipRegistryBlob> registry = TrackRegistry(BuildWalkRegistry());
            Entity prefabRoot = CreateActorPrefab(registry, out Entity prefabPart);

            EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.Temp);
            commandBuffer.Instantiate(prefabRoot);
            commandBuffer.Playback(testWorld.EntityManager);
            commandBuffer.Dispose();

            Entity instancedActor = FindTheOneNonPrefabActor();
            Assert.AreNotEqual(
                prefabRoot,
                instancedActor,
                "Guard: the query must have found the instance, not the source it was copied from.");

            PlaybackTestActor.EnqueueCommand(
                testWorld,
                instancedActor,
                PlaybackTestActor.PlayCommand(0, WalkClipId));
            RunFrame(0.25f);

            Entity instancedPart = testWorld.EntityManager.GetBuffer<RigPartRef>(instancedActor)[0].part;
            Assert.AreNotEqual(prefabPart, instancedPart, "The instance is still bound to the prefab's part.");

            // Key x runs 0 → 10 across the clip, and keys are offsets from rest (A31), so a quarter
            // of a second in the body sits at rest 0.5 + 2.5.
            Assert.AreEqual(
                3f,
                testWorld.EntityManager.GetComponentData<TargetPose>(instancedPart).localPosition.x,
                Tolerance,
                "The instance's own part must have been sampled.");
            Assert.AreEqual(
                3f,
                testWorld.EntityManager.GetComponentData<LocalTransform>(instancedPart).Position.x,
                Tolerance,
                "...and applied, which is what 'and animates' means.");
            Assert.AreEqual(
                BodyRestPosition.x,
                testWorld.EntityManager.GetComponentData<TargetPose>(prefabPart).localPosition.x,
                Tolerance,
                "The prefab's part must be untouched; a mis-bound instance animates it instead.");
        }

        // -------------------------------------------------------------------------------------
        // Section 8 M3: "AnimVisible disabled -> TargetPose frozen while time keeps advancing;
        // re-enable -> next-frame refresh"
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// The section 5.9 visibility boundary, from both sides at once: presentation stops, the
        /// clock does not.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Catches: gating the logic group on <see cref="AnimVisible"/>. <c>TransformTechniqueTests</c>
        /// already pins that an invisible actor is not sampled, but it runs the sample system alone
        /// — so it cannot tell "presentation is gated" from "the whole actor is gated", and the
        /// second is a far worse defect than the first. An off-screen actor whose timers stop
        /// rejoins the world at the pose it left on, having fired none of its events: footsteps go
        /// silent, hit frames never land, and a queued clip never promotes, all only for actors the
        /// player is not looking at.
        /// </para>
        /// <para>
        /// The time assertion is deliberately the clip's own <c>time</c> rather than an event count.
        /// It is the value every other logic-group behaviour is derived from, so it fails first and
        /// most legibly.
        /// </para>
        /// </remarks>
        [Test]
        public void AnInvisibleActor_FreezesItsPoseButNotItsClock()
        {
            Entity body = CreateWalkingActor(out Entity actor);

            RunFrame(0.1f);
            Assert.AreEqual(
                1.5f,
                PoseX(body),
                Tolerance,
                "Guard: the actor must have sampled while visible, or the freeze below is vacuous.");

            testWorld.EntityManager.SetComponentEnabled<AnimVisible>(actor, false);
            RunFrame(0.2f);
            RunFrame(0.2f);

            Assert.AreEqual(1.5f, PoseX(body), Tolerance, "An invisible actor must not be sampled.");
            Assert.AreEqual(
                0.5f,
                PlaybackTestActor.GetLayer(testWorld, actor, 0).time,
                Tolerance,
                "Playback time is never gated on visibility (section 5.9): the clock kept running.");
        }

        /// <summary>
        /// The self-healing half of section 5.9: an actor coming back into view is correct on its
        /// first visible frame, with nothing having tracked that it was ever away.
        /// </summary>
        /// <remarks>
        /// Catches: adding dirty-tracking to <c>TransformSampleSystem</c> — re-sampling only when
        /// something is known to have changed. That is a tempting optimisation which looks right in
        /// every other fixture, because every other fixture samples a visible actor. Here it strands
        /// the actor on the pose it froze at until its clip happens to change, which for a looping
        /// idle is never. The expected value is the pose for the <em>current</em> time, not for the
        /// time it went invisible and not for one frame's worth past it, so all three outcomes are
        /// distinguishable in the failure message.
        /// </remarks>
        [Test]
        public void AReappearingActor_RefreshesOnItsFirstVisibleFrame()
        {
            Entity body = CreateWalkingActor(out Entity actor);

            RunFrame(0.1f);
            testWorld.EntityManager.SetComponentEnabled<AnimVisible>(actor, false);
            RunFrame(0.2f);
            RunFrame(0.2f);
            Assert.AreEqual(1.5f, PoseX(body), Tolerance, "Guard: the pose must be stale before it can refresh.");

            testWorld.EntityManager.SetComponentEnabled<AnimVisible>(actor, true);
            RunFrame(0.1f);

            // t = 0.6, so key x = 6 and the composited pose is rest 0.5 + 6. Resuming from the frozen
            // pose would read 2.5; holding it would read 1.5.
            Assert.AreEqual(
                6.5f,
                PoseX(body),
                Tolerance,
                "The first visible frame re-samples from scratch at the current time.");
        }

        // -------------------------------------------------------------------------------------
        // Section 8 M3: "sample-rate phase spreads two actors onto different sample frames"
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Two actors on the same rate but opposite phases sample on alternating frames, and never
        /// on the same one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Catches: passing 0 instead of <see cref="SampleSettings.phase01"/> into
        /// <c>ClipSampler.ShouldSample</c>. Every existing fixture around quantization uses one
        /// actor, where a dropped phase is unobservable — the actor still samples at the right rate,
        /// merely on a different set of frames, and every assertion about its pose still holds.
        /// </para>
        /// <para>
        /// <strong>The point of the phase is a property no single actor has.</strong> It exists so
        /// that a crowd does not sample in lockstep and spike one frame in every N; a build that
        /// drops it is correct actor-by-actor and pathological in aggregate, which is why
        /// <c>RigBindingSystem</c> goes to the trouble of re-deriving it per instance. This is the
        /// only fixture in the package that can see it.
        /// </para>
        /// <para>
        /// At 10 Hz with 1/20 s frames, the sample index advances for exactly one of the two actors
        /// on each frame: phase 0 crosses at 0.1, 0.2, ... and phase 0.5 at 0.05, 0.15, ...
        /// </para>
        /// </remarks>
        [Test]
        public void TwoActorsAHalfPhaseApart_SampleOnAlternatingFrames()
        {
            BlobAssetReference<ClipRegistryBlob> registry = TrackRegistry(BuildWalkRegistry());

            Entity firstActor = PlaybackTestActor.CreateActor(testWorld, registry);
            Entity firstBody = AddBody(firstActor);
            SeedWalkingLayer(firstActor);
            SetSampling(firstActor, rateHz: 10f, phase01: 0f);

            Entity secondActor = PlaybackTestActor.CreateActor(testWorld, registry);
            Entity secondBody = AddBody(secondActor);
            SeedWalkingLayer(secondActor);
            SetSampling(secondActor, rateHz: 10f, phase01: 0.5f);

            AssertSampledThisFrame(firstBody, secondBody, 0.05f, false, true);
            AssertSampledThisFrame(firstBody, secondBody, 0.05f, true, false);
            AssertSampledThisFrame(firstBody, secondBody, 0.05f, false, true);
            AssertSampledThisFrame(firstBody, secondBody, 0.05f, true, false);
        }

        // -------------------------------------------------------------------------------------
        // Section 8 M3 / 11.3: "LOD 2 mid-blend swap -> blend completes on schedule"
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// An actor whose LOD level changes in the middle of a crossfade rejoins the correct weight
        /// and completes the blend at exactly its authored duration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Catches: implementing level 2's hard cut by touching the blend <em>state</em> — zeroing
        /// <c>blendElapsed</c>, clearing the <c>Blending</c> flag, or releasing the source slot early
        /// — instead of snapping only the weight the sampler reads. <c>AnimationLodTests</c> pins
        /// that the level-2 pose is a hard cut and that level 0's is a lerp, but both observe a
        /// single frame at a fixed weight, so a snap that also stopped the timer would pass them
        /// both.
        /// </para>
        /// <para>
        /// <strong>The third frame is the discriminating one.</strong> Dropping back to level 0
        /// mid-blend is the only observation that can distinguish "the weight was snapped for
        /// display" from "the blend was cut short": the first resumes at 0.75, the second is already
        /// finished and reads 100.5. That is the property <c>AnimationLodPolicy.SnapsBlendWeights</c>
        /// documents in prose — "blend timers keep advancing at every level" — and which nothing
        /// executed until this fixture.
        /// </para>
        /// <para>
        /// The clips carry one key each, so the blend weight is the only thing that can move the
        /// pose. A time-varying clip would let a wrong weight and a wrong time cancel out.
        /// </para>
        /// </remarks>
        [Test]
        public void ALodSwapMidBlend_LeavesTheBlendRunningAndCompletesItOnSchedule()
        {
            BlobAssetReference<ClipRegistryBlob> registry = TrackRegistry(BuildBlendPairRegistry());
            Entity actor = PlaybackTestActor.CreateActor(testWorld, registry);
            Entity body = AddBody(actor);
            testWorld.EntityManager.AddComponentData(actor, new AnimLod { level = 0 });
            SeedCrossfadeFromWalkToRun(actor, blendDuration: 1f);

            // Frame 1, level 0. Weight 0.25: the pose is a quarter of the way from walk to run.
            RunFrame(0.25f);
            Assert.AreEqual(25.5f, PoseX(body), Tolerance, "Guard: the blend must be lerping to begin with.");

            // Frame 2, level 2. The weight snaps to the nearer end, which at 0.5 is the incoming clip.
            SetLodLevel(actor, 2);
            RunFrame(0.25f);
            Assert.AreEqual(100.5f, PoseX(body), Tolerance, "At level 2 a crossfade renders as a hard cut.");
            Assert.AreEqual(
                0.5f,
                PlaybackTestActor.GetLayer(testWorld, actor, 0).blendElapsed,
                Tolerance,
                "The blend timer must advance through the swap, not pause under it.");

            // Frame 3, back at level 0. The weight the sampler reads is the one the timer reached.
            SetLodLevel(actor, 0);
            RunFrame(0.25f);
            Assert.AreEqual(
                75.5f,
                PoseX(body),
                Tolerance,
                "Dropping back mid-blend rejoins the true weight; a blend cut short would read 100.5.");
            Assert.IsTrue(
                (PlaybackTestActor.GetLayer(testWorld, actor, 0).flags & PlaybackFlags.Blending) != 0,
                "The blend must still be running one frame short of its duration.");

            // Frame 4 lands exactly on blendDuration, so the blend completes this frame and no later.
            RunFrame(0.25f);
            PlaybackLayer settledLayer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.IsTrue(
                (settledLayer.flags & PlaybackFlags.Blending) == 0,
                "The blend completes at exactly blendDuration.");
            Assert.AreEqual(-1, settledLayer.previousClipIndex, "The crossfade source slot is released.");
            Assert.AreEqual(
                100.5f,
                PoseX(body),
                Tolerance,
                "Final weight is 1: the pose is the incoming clip outright.");
        }

        // -------------------------------------------------------------------------------------
        // Section 11.3 product-owner edge cases, runtime halves
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// A clip with no tracks and no markers holds the rest pose and still reports its completion.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Catches: seeding the composited pose from the identity rather than from
        /// <see cref="TargetRestPose"/>, and gating <c>ClipFinished</c> on the clip having something
        /// to emit. An empty clip is what an animator has the moment they create an asset and before
        /// they key anything, so the first defect surfaces as "the rig collapses to the origin when I
        /// preview my new clip" — and the completion path is what a game waits on to move to the
        /// next state, so a clip that never reports finishing hangs whatever queued behind it.
        /// </para>
        /// <para>
        /// <strong>The pose is scribbled first, and that is not decoration.</strong>
        /// <c>PlaybackTestActor.AddPart</c> seeds <see cref="TargetPose"/> from the rest pose —
        /// exactly as <c>RigTargetBaker</c> does — so asserting "the pose equals the rest pose"
        /// against an unscribbled part would pass against a system that wrote nothing at all. That is
        /// the first of the non-discriminating shapes in §11, and it is the one this fixture is most
        /// exposed to, because "holds the rest pose" and "was never written" are the same numbers.
        /// </para>
        /// </remarks>
        [Test]
        public void AnEmptyClip_HoldsTheRestPose_AndStillReportsItsCompletion()
        {
            BlobAssetReference<ClipRegistryBlob> registry = TrackRegistry(BuildEmptyClipRegistry());
            Entity actor = PlaybackTestActor.CreateActor(testWorld, registry);
            Entity body = PlaybackTestActor.AddPart(
                testWorld,
                actor,
                BodyTargetIndex,
                restPosition: BodyRestPosition,
                restRotationZ: 0.25f,
                restScale: new float3(1.5f, 0.8f, 1f));

            // Values the rest pose cannot produce, so "published the rest pose" is distinguishable
            // from "never wrote anything" — the baker seeds TargetPose from rest, so without this the
            // assertions below hold against a system that does nothing.
            testWorld.EntityManager.SetComponentData(
                body,
                new TargetPose
                {
                    localPosition = new float3(ScribbledPoseX, ScribbledPoseX, ScribbledPoseX),
                    rotation = new float3(0f, 0f, ScribbledPoseX),
                    scale = new float3(ScribbledPoseX, ScribbledPoseX, ScribbledPoseX),
                    sliceIndex = -9,
                    atlasRect = ClipSampler.IdentityAtlasRect
                });

            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.PlayCommand(0, WalkClipId));
            RunFrame(0.5f);

            TargetPose pose = testWorld.EntityManager.GetComponentData<TargetPose>(body);
            Assert.AreEqual(BodyRestPosition.x, pose.localPosition.x, Tolerance, "An empty clip keys nothing.");
            Assert.AreEqual(BodyRestPosition.y, pose.localPosition.y, Tolerance);
            Assert.AreEqual(0.25f, pose.rotation.z, Tolerance);
            Assert.AreEqual(1.5f, pose.scale.x, Tolerance);
            Assert.AreEqual(0.8f, pose.scale.y, Tolerance);

            // Past the one-second duration, so the Once clip clamps and raises its completion.
            RunFrame(0.6f);

            DynamicBuffer<AnimEventOutput> animEvents =
                testWorld.EntityManager.GetBuffer<AnimEventOutput>(actor);
            Assert.AreEqual(1, animEvents.Length, "An empty clip's only event is its completion.");
            Assert.AreEqual((uint)ReservedEventKeys.ClipFinished, animEvents[0].eventKey);
            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<AnimEventsPending>(actor));
            Assert.AreEqual(
                BodyRestPosition.x,
                testWorld.EntityManager.GetComponentData<TargetPose>(body).localPosition.x,
                Tolerance,
                "And it is still the rest pose after finishing.");
        }

        /// <summary>
        /// A VAT clip baked to a single frame addresses that frame and never the row after it.
        /// </summary>
        /// <remarks>
        /// Catches: clamping the local frame to <c>vatFrameCount</c> rather than
        /// <c>vatFrameCount - 1</c>. With one frame the two differ by exactly the first row of
        /// whatever clip was baked next, so the mesh snaps to an unrelated pose — and only for
        /// single-frame clips, which are the still poses a rig spends most of its time in. The
        /// EditMode layout maths covers the addressing; this covers the property the system actually
        /// publishes.
        /// </remarks>
        [Test]
        public void ASingleFrameVatClip_ClampsToItsOwnRow()
        {
            BlobAssetReference<ClipRegistryBlob> registry = TrackRegistry(BuildSingleFrameVatRegistry());
            Entity actor = PlaybackTestActor.CreateActor(testWorld, registry);
            Entity body = PlaybackTestActor.AddPart(
                testWorld, actor, BodyTargetIndex, vatDrivingLayerIndex: 0);

            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(WalkClipId);
            layer.clipIndex = WalkClipIndex;
            layer.time = 0.9f;
            layer.advanceStartTime = 0.9f;
            layer.speed = 1f;
            layer.loop = LoopMode.Loop;
            layer.flags = PlaybackFlags.Active;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);

            // Values nothing can produce, so a system that publishes nothing fails rather than
            // inheriting a plausible-looking seed.
            EntityManager entityManager = testWorld.EntityManager;
            entityManager.SetComponentData(body, new VatFrameAProperty { Value = -999f });
            entityManager.SetComponentData(body, new VatFrameBProperty { Value = -777f });
            entityManager.SetComponentData(body, new VatBlendProperty { Value = -555f });

            elapsedTime += 0.016f;
            testWorld.SetTime(new TimeData(elapsedTime, 0.016f));
            UpdateSystem<VatMaterialSystem>();
            testWorld.EntityManager.CompleteAllTrackedJobs();

            // The clip owns row 7 alone. Any time inside it addresses row 7; row 8 belongs elsewhere.
            Assert.AreEqual(
                7f,
                entityManager.GetComponentData<VatFrameAProperty>(body).Value,
                Tolerance,
                "A one-frame clip holds its only row whatever the playback time.");
            Assert.AreEqual(
                7f,
                entityManager.GetComponentData<VatFrameBProperty>(body).Value,
                Tolerance,
                "B points at A when nothing is blending.");
            Assert.AreEqual(
                0f,
                entityManager.GetComponentData<VatBlendProperty>(body).Value,
                Tolerance);
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers — registries
        // -------------------------------------------------------------------------------------

        private BlobAssetReference<ClipRegistryBlob> TrackRegistry(BlobAssetReference<ClipRegistryBlob> registry)
        {
            ownedRegistries.Add(registry);
            return registry;
        }

        /// <summary>One looping clip whose body track sweeps x from 0 to 10 across a second.</summary>
        private static BlobAssetReference<ClipRegistryBlob> BuildWalkRegistry()
        {
            return PlaybackTestActor.BuildRegistry(
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
                    }
                },
                targetCount: 1);
        }

        /// <summary>
        /// Two single-key clips far apart, so the composited pose reports the blend weight and
        /// nothing else. A time-varying key would let a wrong weight and a wrong time cancel.
        /// </summary>
        private static BlobAssetReference<ClipRegistryBlob> BuildBlendPairRegistry()
        {
            return PlaybackTestActor.BuildRegistry(
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
                                keys = new[] { PlaybackTestActor.Key(0f, positionX: 0f) }
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
        }

        /// <summary>A Once clip with no tracks and no markers — section 11.3's empty-clip case.</summary>
        private static BlobAssetReference<ClipRegistryBlob> BuildEmptyClipRegistry()
        {
            return PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = WalkClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Once
                    }
                },
                targetCount: 1);
        }

        /// <summary>
        /// A clip owning exactly one VAT row, starting at 7 so that an off-by-one clamp addresses a
        /// row that visibly belongs to something else.
        /// </summary>
        private static BlobAssetReference<ClipRegistryBlob> BuildSingleFrameVatRegistry()
        {
            return PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = WalkClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Loop,
                        vatFrameStart = 7,
                        vatFrameCount = 1,
                        vatFps = 30f
                    }
                },
                targetCount: 1);
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers — entities
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Builds an actor already playing the walk clip on layer 0, with one part bound to the body
        /// target, and returns that part.
        /// </summary>
        private Entity CreateWalkingActor(out Entity actor)
        {
            BlobAssetReference<ClipRegistryBlob> registry = TrackRegistry(BuildWalkRegistry());
            actor = PlaybackTestActor.CreateActor(testWorld, registry);
            Entity body = AddBody(actor);
            SeedWalkingLayer(actor);
            return body;
        }

        private Entity AddBody(Entity actor)
        {
            return PlaybackTestActor.AddPart(
                testWorld, actor, BodyTargetIndex, restPosition: BodyRestPosition);
        }

        /// <summary>Seeds layer 0 playing the walk clip from its start, without routing a command.</summary>
        private void SeedWalkingLayer(Entity actor)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(WalkClipId);
            layer.clipIndex = WalkClipIndex;
            layer.time = 0f;
            layer.advanceStartTime = 0f;
            layer.speed = 1f;
            layer.loop = LoopMode.Loop;
            layer.flags = PlaybackFlags.Active;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);
        }

        /// <summary>Seeds layer 0 mid-crossfade from the walk clip into the run clip, at weight 0.</summary>
        private void SeedCrossfadeFromWalkToRun(Entity actor, float blendDuration)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(RunClipId);
            layer.clipIndex = RunClipIndex;
            layer.time = 0f;
            layer.advanceStartTime = 0f;
            layer.speed = 1f;
            layer.loop = LoopMode.Loop;
            layer.previousClip = new ClipId(WalkClipId);
            layer.previousClipIndex = WalkClipIndex;
            layer.previousTime = 0f;
            layer.previousSpeed = 1f;
            layer.previousLoop = LoopMode.Loop;
            layer.blendElapsed = 0f;
            layer.blendDuration = blendDuration;
            layer.flags = PlaybackFlags.Active | PlaybackFlags.Blending;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);
        }

        private void SetSampling(Entity actor, float rateHz, float phase01)
        {
            testWorld.EntityManager.SetComponentData(
                actor,
                new SampleSettings { rateHz = rateHz, phase01 = phase01 });
        }

        private void SetLodLevel(Entity actor, byte level)
        {
            testWorld.EntityManager.SetComponentData(actor, new AnimLod { level = level });
        }

        /// <summary>
        /// Builds a prefab-shaped actor: everything <see cref="PlaybackTestActor.CreateActor"/>
        /// gives a baked root, plus the <c>LinkedEntityGroup</c> instantiate remaps, the binding tag
        /// baked enabled, and the <see cref="Prefab"/> tag that keeps the source out of every query.
        /// </summary>
        private Entity CreateActorPrefab(BlobAssetReference<ClipRegistryBlob> registry, out Entity prefabPart)
        {
            EntityManager entityManager = testWorld.EntityManager;

            Entity prefabRoot = PlaybackTestActor.CreateActor(testWorld, registry);
            prefabPart = AddBody(prefabRoot);

            entityManager.AddComponent<RigBindingUninitialized>(prefabRoot);
            entityManager.SetComponentEnabled<RigBindingUninitialized>(prefabRoot, true);
            entityManager.AddComponent<Prefab>(prefabRoot);
            entityManager.AddComponent<Prefab>(prefabPart);

            // Element 0 is the root, per Entities' contract; fetched after the last structural change.
            DynamicBuffer<LinkedEntityGroup> linkedEntities =
                entityManager.AddBuffer<LinkedEntityGroup>(prefabRoot);
            linkedEntities.Add(prefabRoot);
            linkedEntities.Add(prefabPart);

            return prefabRoot;
        }

        /// <summary>
        /// The single instantiated actor. The prefab source carries <see cref="Prefab"/>, which every
        /// default-options query excludes, so exactly one entity can match.
        /// </summary>
        private Entity FindTheOneNonPrefabActor()
        {
            EntityQuery actorQuery = testWorld.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ClipRegistry>());
            NativeArray<Entity> actors = actorQuery.ToEntityArray(Allocator.Temp);

            Assert.AreEqual(1, actors.Length, "Guard: exactly one non-prefab actor should exist.");
            Entity instancedActor = actors[0];
            actors.Dispose();
            return instancedActor;
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers — running frames
        // -------------------------------------------------------------------------------------

        private float PoseX(Entity part)
        {
            return testWorld.EntityManager.GetComponentData<TargetPose>(part).localPosition.x;
        }

        /// <summary>
        /// Overwrites both poses with a value nothing can produce, runs a frame, and asserts which of
        /// the two actors re-sampled — "did not sample" and "sampled the same number again" are
        /// otherwise the same observation.
        /// </summary>
        private void AssertSampledThisFrame(
            Entity firstBody,
            Entity secondBody,
            float deltaTime,
            bool expectFirstSampled,
            bool expectSecondSampled)
        {
            ScribblePose(firstBody);
            ScribblePose(secondBody);

            RunFrame(deltaTime);

            Assert.AreEqual(
                expectFirstSampled,
                PoseX(firstBody) != ScribbledPoseX,
                $"Actor on phase 0 at t={elapsedTime:F2}: expected sampled={expectFirstSampled}.");
            Assert.AreEqual(
                expectSecondSampled,
                PoseX(secondBody) != ScribbledPoseX,
                $"Actor on phase 0.5 at t={elapsedTime:F2}: expected sampled={expectSecondSampled}. "
                + "Both actors sampling together is the crowd spike phase exists to prevent.");
        }

        private void ScribblePose(Entity part)
        {
            testWorld.EntityManager.SetComponentData(
                part,
                new TargetPose { localPosition = new float3(ScribbledPoseX, 0f, 0f) });
        }

        /// <summary>
        /// One toolkit frame in group order: binding, then the ungated logic group, then the gated
        /// presentation group.
        /// </summary>
        /// <remarks>
        /// <c>AnimLodDistanceSystem</c> is deliberately absent. It is the optional writer of a level
        /// these fixtures set by hand, and it has its own coverage in <c>AnimationLodTests</c>;
        /// running it here would make the LOD fixture depend on a camera singleton and a threshold
        /// table that have nothing to do with what it is asserting.
        /// </remarks>
        private void RunFrame(float deltaTime)
        {
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));

            UpdateSystem<RigBindingSystem>();
            UpdateSystem<CommandApplySystem>();
            UpdateSystem<PlaybackTimeSystem>();
            UpdateSystem<EventEmissionSystem>();
            UpdateSystem<TransformSampleSystem>();
            UpdateSystem<TransformApplySystem>();

            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void UpdateSystem<TSystem>() where TSystem : unmanaged, ISystem
        {
            SystemHandle systemHandle = testWorld.GetOrCreateSystem<TSystem>();
            systemHandle.Update(testWorld.Unmanaged);
        }
    }
}
