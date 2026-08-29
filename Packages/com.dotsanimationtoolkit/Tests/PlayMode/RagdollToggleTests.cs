// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// The ragdoll feature's headline promise (Phase D4, amendment A50, spec §10): "turning the
    /// ragdoll off resets the character to before." Every fixture here bakes a real actor through
    /// <see cref="ActorBakeFixture"/>/<see cref="BakingTestWorld"/> — the same infrastructure
    /// <c>RagdollBakingTests</c> uses — and then runs the five runtime systems directly against the
    /// baking world, exactly as <c>SocketResolveSystemTests</c> runs systems against its own
    /// hand-built world.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No fixture in this phase has ever enabled a <see cref="RagdollActor"/> before this file. A
    /// <see cref="RagdollConfig"/> singleton has to be created by hand — without one,
    /// <c>SystemAPI.TryGetSingleton</c> in <c>RagdollSolveSystem</c> falls back to zero-valued
    /// gravity (matching <c>TransformSampleSystem</c>'s "missing config is a sane default"
    /// precedent) and nothing would ever fall, which would make every fixture here vacuous.
    /// </para>
    /// </remarks>
    public sealed class RagdollToggleTests
    {
        private ActorBakeFixture fixture;
        private BakingTestWorld bakingWorld;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            fixture = new ActorBakeFixture();
            bakingWorld = new BakingTestWorld("RagdollToggleTests");
            elapsedTime = 0d;
        }

        [TearDown]
        public void TearDown()
        {
            bakingWorld.Dispose();
            fixture.DestroyAll();
        }

        [Test]
        public void EnablingTheRagdoll_CapturesTheRestPose_ClearsCaptureNeeded_AndSetsRestoreNeeded()
        {
            RigAsset rig;
            ClipSetAsset clipSet = CreateClipSet(out rig);
            AddPathBody(rig, "Whole Actor", string.Empty, 100u);
            AddTargetBody(rig, "Torso", ActorBakeFixture.TorsoTargetId, 200u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", rig, clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();
            CreateRealisticRagdollConfig();

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            Entity torsoEntity = bakingWorld.GetPrimaryEntity(ActorBakeFixture.FindPart(actorGameObject, "Torso"));

            LocalTransform preDropActorTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(actorEntity);
            LocalTransform preDropTorsoTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(torsoEntity);

            EnableRagdoll(actorEntity);
            RunCapture();

            RagdollState state = bakingWorld.EntityManager.GetComponentData<RagdollState>(actorEntity);
            Assert.AreEqual(
                RagdollStateFlags.RestoreNeeded,
                state.flags,
                "Capture must clear CaptureNeeded and set RestoreNeeded, and nothing else.");

            DynamicBuffer<RagdollRestPose> restPoseElements =
                bakingWorld.EntityManager.GetBuffer<RagdollRestPose>(actorEntity);
            AssertLocalTransformExactlyEqual(
                preDropActorTransform, restPoseElements[0].localTransform, "root rest pose");
            AssertLocalTransformExactlyEqual(
                preDropTorsoTransform, restPoseElements[1].localTransform, "torso rest pose");
        }

        [Test]
        public void DisablingAfterSeveralFrames_RestoresEveryNodesLocalTransformAndPostTransformMatrix_ToFloatEquality()
        {
            RigAsset rig;
            ClipSetAsset clipSet = CreateClipSet(out rig);
            AddPathBody(rig, "Whole Actor", string.Empty, 100u);
            AddTargetBody(rig, "Torso", ActorBakeFixture.TorsoTargetId, 200u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", rig, clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();
            CreateRealisticRagdollConfig();

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            Entity torsoEntity = bakingWorld.GetPrimaryEntity(ActorBakeFixture.FindPart(actorGameObject, "Torso"));

            EnableRagdoll(actorEntity);
            RunCapture();

            LocalTransform capturedRootTransform =
                bakingWorld.EntityManager.GetBuffer<RagdollRestPose>(actorEntity)[0].localTransform;
            LocalTransform capturedTorsoTransform =
                bakingWorld.EntityManager.GetBuffer<RagdollRestPose>(actorEntity)[1].localTransform;
            PostTransformMatrix capturedTorsoMatrix =
                bakingWorld.EntityManager.GetBuffer<RagdollRestPose>(actorEntity)[1].postTransformMatrix;

            // Something other than the ragdoll group writes PostTransformMatrix while a ragdoll runs
            // (an animated scale channel, in production — RagdollApplySystem's own remarks say it
            // never touches this component). Mutating it here stands in for that write, so restoring
            // it below is proven to come from the captured snapshot rather than from a value the
            // ragdoll group never disturbed in the first place.
            bakingWorld.EntityManager.SetComponentData(
                torsoEntity,
                new PostTransformMatrix { Value = float4x4.Scale(2f, 3f, 1f) });

            // Several real frames, so the bodies genuinely move away from the captured pose under
            // gravity before the toggle is flipped off again.
            for (int frameIndex = 0; frameIndex < 30; frameIndex++)
            {
                RunOneRagdollFrame(1f / 60f);
            }

            LocalTransform movedRootTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(actorEntity);
            LocalTransform movedTorsoTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(torsoEntity);
            Assert.AreNotEqual(
                capturedRootTransform.Position.y,
                movedRootTransform.Position.y,
                "Guard: the root must have genuinely fallen, or restoring it below proves nothing.");
            Assert.AreNotEqual(
                capturedTorsoTransform.Position.y,
                movedTorsoTransform.Position.y,
                "Guard: the torso must have genuinely fallen, or restoring it below proves nothing.");

            DisableRagdoll(actorEntity);
            RunRelease();

            LocalTransform restoredRootTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(actorEntity);
            LocalTransform restoredTorsoTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(torsoEntity);
            PostTransformMatrix restoredTorsoMatrix =
                bakingWorld.EntityManager.GetComponentData<PostTransformMatrix>(torsoEntity);

            AssertLocalTransformExactlyEqual(capturedRootTransform, restoredRootTransform, "root restore");
            AssertLocalTransformExactlyEqual(capturedTorsoTransform, restoredTorsoTransform, "torso restore");
            AssertPostTransformMatrixExactlyEqual(capturedTorsoMatrix, restoredTorsoMatrix, "torso PostTransformMatrix restore");

            RagdollState state = bakingWorld.EntityManager.GetComponentData<RagdollState>(actorEntity);
            Assert.AreEqual(
                RagdollStateFlags.CaptureNeeded,
                state.flags,
                "Release must clear RestoreNeeded (and Sleeping, if it was set) and arm CaptureNeeded for the next switch-on.");
            Assert.AreEqual(0f, state.substepAccumulator, "The accumulator must not carry across a release.");
            Assert.AreEqual(0f, state.sleepTimer, "The sleep timer must not carry across a release.");
        }

        [Test]
        public void ReenablingAfterARelease_RecapturesFromWhereverTheActorNowIs_NotTheStaleBuffer()
        {
            RigAsset rig;
            ClipSetAsset clipSet = CreateClipSet(out rig);
            AddPathBody(rig, "Whole Actor", string.Empty, 100u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", rig, clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();
            CreateRealisticRagdollConfig();

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);

            EnableRagdoll(actorEntity);
            RunCapture();
            for (int frameIndex = 0; frameIndex < 10; frameIndex++)
            {
                RunOneRagdollFrame(1f / 60f);
            }
            DisableRagdoll(actorEntity);
            RunRelease();

            // Simulate the animation moving the actor while the ragdoll is off — the same thing
            // TransformApplySystem does every frame in production.
            LocalTransform animatedTransform = LocalTransform.FromPositionRotationScale(
                new float3(3f, 7f, 0f), quaternion.RotateZ(math.radians(20f)), 1f);
            bakingWorld.EntityManager.SetComponentData(actorEntity, animatedTransform);

            EnableRagdoll(actorEntity);
            RunCapture();

            LocalTransform recapturedTransform =
                bakingWorld.EntityManager.GetBuffer<RagdollRestPose>(actorEntity)[0].localTransform;
            AssertLocalTransformExactlyEqual(
                animatedTransform,
                recapturedTransform,
                "the second capture must read wherever the animation carried the actor, not the stale first-drop buffer");

            RagdollBody rootBody = bakingWorld.EntityManager.GetBuffer<RagdollBody>(actorEntity)[0];
            Assert.AreEqual(
                animatedTransform.Position.x, rootBody.state.position.x, 1e-5f,
                "The body's seeded world state must also come from the new pose.");
            Assert.AreEqual(animatedTransform.Position.y, rootBody.state.position.y, 1e-5f);

            RagdollState state = bakingWorld.EntityManager.GetComponentData<RagdollState>(actorEntity);
            Assert.AreEqual(RagdollStateFlags.RestoreNeeded, state.flags);
        }

        [Test]
        public void AnActorWhoseRigDeclaresNoRagdollBodies_IsUntouchedThroughout()
        {
            // A separate baking world with no ragdoll body anywhere: every ragdoll system's
            // RequireForUpdate<RagdollBody> then matches nothing at all, so OnUpdate never runs for
            // any of the five systems — the strongest form of "untouched".
            RigAsset rig;
            ClipSetAsset clipSet = CreateClipSet(out rig);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", rig, clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            Assert.IsFalse(bakingWorld.EntityManager.HasComponent<RagdollActor>(actorEntity), "Guard: opt-in must have produced nothing to toggle.");
            Assert.IsFalse(bakingWorld.EntityManager.HasBuffer<RagdollBody>(actorEntity));

            Entity torsoEntity = bakingWorld.GetPrimaryEntity(ActorBakeFixture.FindPart(actorGameObject, "Torso"));
            LocalTransform beforeActorTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(actorEntity);
            LocalTransform beforeTorsoTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(torsoEntity);

            CreateRealisticRagdollConfig();
            for (int frameIndex = 0; frameIndex < 5; frameIndex++)
            {
                RunOneRagdollFrame(1f / 60f);
            }

            LocalTransform afterActorTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(actorEntity);
            LocalTransform afterTorsoTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(torsoEntity);

            AssertLocalTransformExactlyEqual(beforeActorTransform, afterActorTransform, "actor untouched");
            AssertLocalTransformExactlyEqual(beforeTorsoTransform, afterTorsoTransform, "torso untouched");
        }

        // -----------------------------------------------------------------------------------
        // Fixture helpers.
        // -----------------------------------------------------------------------------------

        private ClipSetAsset CreateClipSet(out RigAsset rig)
        {
            rig = fixture.CreateRig("Rig");
            return fixture.CreateClipSet("Set", 0x4600UL);
        }

        private static RagdollBodyDefinition AddTargetBody(RigAsset rig, string displayName, uint targetId, uint bodyId)
        {
            RagdollBodyDefinition definition = new RagdollBodyDefinition
            {
                displayName = displayName,
                stableId = bodyId,
                address = new RigNodeAddress { kind = RigNodeAddressKind.RigTarget, targetId = targetId }
            };
            rig.ragdollBodies.Add(definition);
            return definition;
        }

        private static RagdollBodyDefinition AddPathBody(RigAsset rig, string displayName, string path, uint bodyId)
        {
            RagdollBodyDefinition definition = new RagdollBodyDefinition
            {
                displayName = displayName,
                stableId = bodyId,
                address = new RigNodeAddress { kind = RigNodeAddressKind.HierarchyPath, hierarchyPath = path }
            };
            rig.ragdollBodies.Add(definition);
            return definition;
        }

        /// <summary>Real, tuned values — the same ones <c>ConfigBootstrapSystem</c> creates in production — so a dropped body genuinely falls.</summary>
        private void CreateRealisticRagdollConfig()
        {
            if (bakingWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<RagdollConfig>()).CalculateEntityCount() > 0)
            {
                return;
            }
            bakingWorld.EntityManager.CreateSingleton(
                new RagdollConfig
                {
                    worldGravity = new float3(0f, -9.81f, 0f),
                    sleepLinearSpeed = 0.05f,
                    sleepAngularSpeed = 0.05f,
                    sleepDelaySeconds = 0.5f,
                    maxSubstepsPerFrame = 4,
                    fallbackGroundHeight = -1000f,
                    contactProbeRadius = 0.02f
                },
                "RagdollConfig");
        }

        private void EnableRagdoll(Entity actorEntity)
        {
            bakingWorld.EntityManager.SetComponentEnabled<RagdollActor>(actorEntity, true);
        }

        private void DisableRagdoll(Entity actorEntity)
        {
            bakingWorld.EntityManager.SetComponentEnabled<RagdollActor>(actorEntity, false);
        }

        private void RunCapture()
        {
            UpdateSystem<RagdollCaptureSystem>();
        }

        private void RunRelease()
        {
            UpdateSystem<RagdollReleaseSystem>();
        }

        private void RunOneRagdollFrame(float deltaTime)
        {
            elapsedTime += deltaTime;
            bakingWorld.World.SetTime(new TimeData(elapsedTime, deltaTime));

            UpdateSystem<RagdollProbeFallbackSystem>();
            UpdateSystem<RagdollSolveSystem>();
            UpdateSystem<RagdollApplySystem>();

            bakingWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void UpdateSystem<TSystem>() where TSystem : unmanaged, ISystem
        {
            SystemHandle systemHandle = bakingWorld.World.GetOrCreateSystem<TSystem>();
            systemHandle.Update(bakingWorld.World.Unmanaged);
            bakingWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private static void AssertLocalTransformExactlyEqual(LocalTransform expected, LocalTransform actual, string context)
        {
            Assert.AreEqual(expected.Position.x, actual.Position.x, 0f, context + ": Position.x");
            Assert.AreEqual(expected.Position.y, actual.Position.y, 0f, context + ": Position.y");
            Assert.AreEqual(expected.Position.z, actual.Position.z, 0f, context + ": Position.z");
            Assert.AreEqual(expected.Rotation.value.x, actual.Rotation.value.x, 0f, context + ": Rotation.x");
            Assert.AreEqual(expected.Rotation.value.y, actual.Rotation.value.y, 0f, context + ": Rotation.y");
            Assert.AreEqual(expected.Rotation.value.z, actual.Rotation.value.z, 0f, context + ": Rotation.z");
            Assert.AreEqual(expected.Rotation.value.w, actual.Rotation.value.w, 0f, context + ": Rotation.w");
            Assert.AreEqual(expected.Scale, actual.Scale, 0f, context + ": Scale");
        }

        private static void AssertPostTransformMatrixExactlyEqual(PostTransformMatrix expected, PostTransformMatrix actual, string context)
        {
            Assert.AreEqual(expected.Value.c0.x, actual.Value.c0.x, 0f, context + ": c0.x");
            Assert.AreEqual(expected.Value.c0.y, actual.Value.c0.y, 0f, context + ": c0.y");
            Assert.AreEqual(expected.Value.c0.z, actual.Value.c0.z, 0f, context + ": c0.z");
            Assert.AreEqual(expected.Value.c1.x, actual.Value.c1.x, 0f, context + ": c1.x");
            Assert.AreEqual(expected.Value.c1.y, actual.Value.c1.y, 0f, context + ": c1.y");
            Assert.AreEqual(expected.Value.c1.z, actual.Value.c1.z, 0f, context + ": c1.z");
            Assert.AreEqual(expected.Value.c2.x, actual.Value.c2.x, 0f, context + ": c2.x");
            Assert.AreEqual(expected.Value.c2.y, actual.Value.c2.y, 0f, context + ": c2.y");
            Assert.AreEqual(expected.Value.c2.z, actual.Value.c2.z, 0f, context + ": c2.z");
        }
    }
}
