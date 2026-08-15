// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>TransformSampleSystem</c> and <c>TransformApplySystem</c> — the transform
    /// technique of architecture section 5.6 (build step C4.5), the first pair that puts motion on
    /// screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The composition maths itself belongs to <c>ClipSampler</c> and is covered by
    /// <c>LayerCompositionTests</c> in EditMode. These fixtures are about what only a running system
    /// decides: which actors and parts get sampled, which target each part reads, when quantization
    /// skips a frame, and — the one that matters most — which transform channel scale ends up in.
    /// </para>
    /// <para>
    /// Every part here rests <em>away from the origin, rotated, and non-uniformly scaled</em>. An
    /// identity rest pose would make several of these assertions pass no matter what the systems
    /// did.
    /// </para>
    /// </remarks>
    public sealed class TransformTechniqueTests
    {
        private const float Tolerance = 1e-4f;

        private const ulong ArmClipId = 100;
        private const int ArmClipIndex = 0;

        // Dense target indices, and the ids they correspond to in the fixture registry.
        private const int ShoulderTargetIndex = 0;
        private const int HandTargetIndex = 1;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private Entity actor;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("TransformTechniqueTests");
            elapsedTime = 0d;

            // One clip, two tracks bound to two different targets, with values distinct enough that
            // reading the wrong target or the wrong channel is unmistakable in a failure message.
            registry = PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = ArmClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Loop,
                        transformTracks = new[]
                        {
                            new PlaybackTestActor.TransformTrackSpec
                            {
                                targetIndex = ShoulderTargetIndex,
                                channels = AnimatedChannels.PositionXY
                                    | AnimatedChannels.Rotation
                                    | AnimatedChannels.Scale,
                                keys = new[]
                                {
                                    PlaybackTestActor.Key(
                                        0f, positionX: 3f, positionY: 4f, rotationZ: 1.25f,
                                        scaleX: 2f, scaleY: 0.5f)
                                }
                            },
                            new PlaybackTestActor.TransformTrackSpec
                            {
                                targetIndex = HandTargetIndex,
                                channels = AnimatedChannels.PositionXY,
                                keys = new[]
                                {
                                    PlaybackTestActor.Key(0f, positionX: -7f, positionY: -8f)
                                }
                            }
                        }
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

        // -------------------------------------------------------------------------------------
        // TransformSampleSystem
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: not writing <see cref="TargetPose"/> at all, which leaves every part frozen on
        /// the pose the baker seeded and produces an actor that is perfectly rigged and perfectly
        /// motionless.
        /// </summary>
        [Test]
        public void ASampledPart_ReceivesTheClipsPose()
        {
            Entity shoulder = AddPart(ShoulderTargetIndex);
            PlayArmClip();

            RunSample();

            // Keys are offsets from rest (amendment A31): position 0.5 + 3, rotation 0.25 + 1.25,
            // scale 1.5 x 2. The rest pose is non-identity precisely so these are distinguishable
            // from the raw key values.
            TargetPose pose = testWorld.EntityManager.GetComponentData<TargetPose>(shoulder);
            Assert.AreEqual(3.5f, pose.localPosition.x, Tolerance);
            Assert.AreEqual(5.25f, pose.localPosition.y, Tolerance);
            Assert.AreEqual(1.5f, pose.rotation.z, Tolerance);
            Assert.AreEqual(3f, pose.scale.x, Tolerance);
            Assert.AreEqual(0.4f, pose.scale.y, Tolerance);
        }

        /// <summary>
        /// Catches: sampling every part against the same target index — the loop counter instead of
        /// <see cref="RigPartRef.targetIndex"/>. Every part of the rig would then play the first
        /// target's motion, which looks like an animation that "works" until you notice the whole
        /// body doing the shoulder's move.
        /// </summary>
        [Test]
        public void EachPart_ReadsItsOwnTargetsTracks()
        {
            Entity shoulder = AddPart(ShoulderTargetIndex);
            Entity hand = AddPart(HandTargetIndex);
            PlayArmClip();

            RunSample();

            TargetPose shoulderPose = testWorld.EntityManager.GetComponentData<TargetPose>(shoulder);
            TargetPose handPose = testWorld.EntityManager.GetComponentData<TargetPose>(hand);
            Assert.AreEqual(3.5f, shoulderPose.localPosition.x, Tolerance);
            Assert.AreEqual(-9f, handPose.localPosition.x, Tolerance, "The hand must read the hand's track.");
            Assert.AreEqual(-7.25f, handPose.localPosition.y, Tolerance);
        }

        /// <summary>
        /// Catches: dropping the <c>targetIndex &lt; 0</c> guard. An unresolved part would be
        /// sampled against dense target 0 and would silently animate with another part's motion —
        /// worse than the inert behaviour the baker deliberately chose by leaving the index at −1.
        /// </summary>
        [Test]
        public void APartWhoseTargetNeverResolved_IsLeftAlone()
        {
            Entity orphan = AddPart(-1);
            TargetPose before = testWorld.EntityManager.GetComponentData<TargetPose>(orphan);
            PlayArmClip();

            RunSample();

            TargetPose after = testWorld.EntityManager.GetComponentData<TargetPose>(orphan);
            Assert.AreEqual(before.localPosition.x, after.localPosition.x, Tolerance);
            Assert.AreEqual(before.localPosition.y, after.localPosition.y, Tolerance);
        }

        /// <summary>
        /// Catches: dropping the <see cref="AnimVisible"/> gate on the sample job. Presentation work
        /// for off-screen actors is exactly the cost the visibility split exists to avoid, and
        /// nothing about paying it is visible — the output is correct, just needlessly computed.
        /// </summary>
        [Test]
        public void AnInvisibleActor_IsNotSampled()
        {
            Entity shoulder = AddPart(ShoulderTargetIndex);
            PlayArmClip();
            testWorld.EntityManager.SetComponentEnabled<AnimVisible>(actor, false);

            RunSample();

            TargetPose pose = testWorld.EntityManager.GetComponentData<TargetPose>(shoulder);
            Assert.AreEqual(
                RestPosition(ShoulderTargetIndex).x,
                pose.localPosition.x,
                Tolerance,
                "An invisible actor must not have been sampled.");
        }

        /// <summary>
        /// Catches: dropping the <see cref="ClipSampler.ShouldSample"/> call. A quantized actor
        /// would sample every frame, which costs exactly what the rate was set to save, while every
        /// output stays correct — the failure mode no assertion about pose values can see.
        /// </summary>
        [Test]
        public void AQuantizedActor_SkipsFramesBetweenItsSampleTicks()
        {
            Entity shoulder = AddPart(ShoulderTargetIndex);
            PlayArmClip();
            SetSampleRate(10f);

            // 1/10 s apart: the first advance crosses a sample tick, the second does not.
            AdvanceAndSample(0.1f);
            TargetPose sampled = testWorld.EntityManager.GetComponentData<TargetPose>(shoulder);
            Assert.AreEqual(3.5f, sampled.localPosition.x, Tolerance, "Guard: the first tick must sample.");

            // Scribble over the pose; if the actor samples again it will be overwritten back.
            testWorld.EntityManager.SetComponentData(shoulder, new TargetPose { localPosition = new float3(99f, 99f, 0f) });
            AdvanceAndSample(0.01f);

            Assert.AreEqual(
                99f,
                testWorld.EntityManager.GetComponentData<TargetPose>(shoulder).localPosition.x,
                Tolerance,
                "A 10 Hz actor must not re-sample 10 ms after its last tick.");
        }

        /// <summary>
        /// Catches: inverting the rate check, so that rate 0 means "never sample" instead of "every
        /// frame". Zero is the default for every actor that never opts in, so this is the setting
        /// almost all content runs at.
        /// </summary>
        [Test]
        public void AnActorWithNoRate_SamplesEveryFrame()
        {
            Entity shoulder = AddPart(ShoulderTargetIndex);
            PlayArmClip();

            AdvanceAndSample(0.001f);
            testWorld.EntityManager.SetComponentData(shoulder, new TargetPose { localPosition = new float3(99f, 99f, 0f) });
            AdvanceAndSample(0.001f);

            Assert.AreEqual(
                3.5f,
                testWorld.EntityManager.GetComponentData<TargetPose>(shoulder).localPosition.x,
                Tolerance,
                "Rate 0 means sample every frame.");
        }

        // -------------------------------------------------------------------------------------
        // TransformApplySystem
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: not writing the transform, or writing rotation as anything but a Z rotation.
        /// </summary>
        [Test]
        public void Apply_WritesPositionAndRotationOntoTheTransform()
        {
            Entity shoulder = AddPart(ShoulderTargetIndex);
            PlayArmClip();

            RunSampleAndApply();

            LocalTransform localTransform = testWorld.EntityManager.GetComponentData<LocalTransform>(shoulder);
            Assert.AreEqual(3.5f, localTransform.Position.x, Tolerance);
            Assert.AreEqual(5.25f, localTransform.Position.y, Tolerance);

            float appliedRotationZ = math.atan2(
                2f * localTransform.Rotation.value.w * localTransform.Rotation.value.z,
                1f - 2f * localTransform.Rotation.value.z * localTransform.Rotation.value.z);
            Assert.AreEqual(1.5f, appliedRotationZ, Tolerance, "Rotation must be applied about Z.");
        }

        /// <summary>
        /// <strong>The host's dead-scale regression, as a test.</strong> Catches: writing scale into
        /// <c>LocalTransform.Scale</c>, or not writing <c>PostTransformMatrix</c> at all. The host
        /// game's apply system did exactly that — forced <c>Scale = 1</c> and never touched the
        /// matrix — and every authored scale curve in the project silently did nothing. It is also
        /// unrepresentable in <c>LocalTransform</c>: a single uniform float cannot carry a
        /// non-uniform 2 × 0.5.
        /// </summary>
        [Test]
        public void Apply_PutsNonUniformScaleInThePostTransformMatrix()
        {
            Entity shoulder = AddPart(ShoulderTargetIndex);
            PlayArmClip();

            RunSampleAndApply();

            PostTransformMatrix postTransformMatrix =
                testWorld.EntityManager.GetComponentData<PostTransformMatrix>(shoulder);
            Assert.AreEqual(3f, postTransformMatrix.Value.c0.x, Tolerance, "Scale x belongs in the matrix.");
            Assert.AreEqual(0.4f, postTransformMatrix.Value.c1.y, Tolerance, "Scale y belongs in the matrix.");
            Assert.AreEqual(1f, postTransformMatrix.Value.c2.z, Tolerance, "Z is never scaled in 2.5D.");
        }

        /// <summary>
        /// Catches: leaving <c>LocalTransform.Scale</c> at whatever it held. The matrix is the whole
        /// scale channel, so a residual uniform scale multiplies on top of it and double-applies —
        /// a part authored at rest scale 2 would render at 4.
        /// </summary>
        [Test]
        public void Apply_PinsLocalTransformScaleToOne()
        {
            Entity shoulder = AddPart(ShoulderTargetIndex);
            testWorld.EntityManager.SetComponentData(
                shoulder,
                LocalTransform.FromPositionRotationScale(float3.zero, quaternion.identity, 3f));
            PlayArmClip();

            RunSampleAndApply();

            Assert.AreEqual(
                1f,
                testWorld.EntityManager.GetComponentData<LocalTransform>(shoulder).Scale,
                Tolerance,
                "Scale lives in PostTransformMatrix; leaving it here too would double-apply.");
        }

        /// <summary>
        /// Catches: taking the absolute value of scale, or clamping it. Negative scale is how a 2D
        /// cutout rig faces the other way; losing the sign means a character that never turns round.
        /// </summary>
        [Test]
        public void Apply_PreservesNegativeScaleSoPartsCanFlip()
        {
            Entity hand = AddPart(HandTargetIndex, restScale: new float3(-1f, 1f, 1f));
            // No scale channel on the hand's track, so the rest scale survives composition.
            PlayArmClip();

            RunSampleAndApply();

            Assert.AreEqual(
                -1f,
                testWorld.EntityManager.GetComponentData<PostTransformMatrix>(hand).Value.c0.x,
                Tolerance,
                "A flipped part must stay flipped.");
        }

        /// <summary>
        /// Catches: dropping the <see cref="AnimVisible"/> gate on the apply job.
        /// </summary>
        [Test]
        public void AnInvisiblePart_IsNotApplied()
        {
            Entity shoulder = AddPart(ShoulderTargetIndex);
            PlayArmClip();
            RunSample();
            testWorld.EntityManager.SetComponentEnabled<AnimVisible>(shoulder, false);
            testWorld.EntityManager.SetComponentData(shoulder, LocalTransform.FromPosition(new float3(42f, 0f, 0f)));

            RunApply();

            Assert.AreEqual(
                42f,
                testWorld.EntityManager.GetComponentData<LocalTransform>(shoulder).Position.x,
                Tolerance);
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Rest poses are deliberately non-identity, and distinct per target, so that a system which
        /// loses the rest pose or reads the wrong part is caught by the numbers rather than by luck.
        /// </summary>
        private static float3 RestPosition(int targetIndex)
        {
            return targetIndex == ShoulderTargetIndex
                ? new float3(0.5f, 1.25f, 0.1f)
                : new float3(-2f, 0.75f, 0.2f);
        }

        private Entity AddPart(int targetIndex, float3 restScale = default)
        {
            return PlaybackTestActor.AddPart(
                testWorld,
                actor,
                targetIndex,
                restPosition: RestPosition(targetIndex),
                restRotationZ: 0.25f,
                restScale: math.all(restScale == float3.zero) ? new float3(1.5f, 0.8f, 1f) : restScale);
        }

        private void SetSampleRate(float rateHz)
        {
            testWorld.EntityManager.SetComponentData(
                actor,
                new SampleSettings { rateHz = rateHz, phase01 = 0f });
        }

        /// <summary>Seeds layer 0 playing the arm clip, without routing through the command system.</summary>
        private void PlayArmClip()
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(ArmClipId);
            layer.clipIndex = ArmClipIndex;
            layer.time = 0f;
            layer.advanceStartTime = 0f;
            layer.speed = 1f;
            layer.loop = LoopMode.Loop;
            layer.flags = PlaybackFlags.Active;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);
        }

        private void RunSample()
        {
            AdvanceAndSample(0.016f);
        }

        private void AdvanceAndSample(float deltaTime)
        {
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));

            SystemHandle sampleSystem = testWorld.GetOrCreateSystem<TransformSampleSystem>();
            sampleSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void RunApply()
        {
            SystemHandle applySystem = testWorld.GetOrCreateSystem<TransformApplySystem>();
            applySystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void RunSampleAndApply()
        {
            RunSample();
            RunApply();
        }
    }
}
