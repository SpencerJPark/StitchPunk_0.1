// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Text;
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
    /// The G1 regression test (Phase D4, amendment A50, spec §9 G1, §10): a settled ragdoll must
    /// sleep, and its settled pose must survive <c>TransformApplySystem</c> stomping every visible
    /// part's <c>LocalTransform</c> every frame — "the single most likely source of 'the ragdoll
    /// works for one frame and then reverts'" (<c>RagdollApplySystem</c>'s own remarks).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rather than driving a real playing clip through <c>TransformApplySystem</c> to reproduce the
    /// stomp, this fixture writes directly onto the sleeping body's node <c>LocalTransform</c> —
    /// exactly the effect an animated part's write would have, isolated from everything else that
    /// write depends on. That is the minimal, direct exercise of the actual contract
    /// <c>RagdollApplySystem</c>'s remarks describe: "this system is written with no early return on
    /// sleep at all."
    /// </para>
    /// </remarks>
    public sealed class RagdollSleepTests
    {
        private ActorBakeFixture fixture;
        private BakingTestWorld bakingWorld;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            fixture = new ActorBakeFixture();
            bakingWorld = new BakingTestWorld("RagdollSleepTests");
            elapsedTime = 0d;
        }

        [TearDown]
        public void TearDown()
        {
            bakingWorld.Dispose();
            fixture.DestroyAll();
        }

        [Test]
        public void ASettledRagdoll_Sleeps_AndItsSettledPoseSurvivesTransformApplySystemStompingItEveryFrame()
        {
            RigAsset rig;
            ClipSetAsset clipSet = CreateClipSet(out rig);
            AddPathBody(rig, "Whole Actor", string.Empty, 100u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", rig, clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();
            CreateFastSettlingRagdollConfig();

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);

            // Start close to the fallback ground plane (y = 0) so the body settles within a bounded
            // number of frames rather than free-falling indefinitely.
            LocalTransform startTransform = LocalTransform.FromPosition(new float3(0f, 0.6f, 0f));
            bakingWorld.EntityManager.SetComponentData(actorEntity, startTransform);

            EnableRagdoll(actorEntity);
            RunCapture();

            bool wentToSleep = false;
            const int maxFrames = 600; // 10 seconds at 60 Hz — generous, but bounded.
            List<string> diagnostics = new List<string>();
            for (int frameIndex = 0; frameIndex < maxFrames; frameIndex++)
            {
                RunOneRagdollFrame(1f / 60f);
                RagdollState currentState = bakingWorld.EntityManager.GetComponentData<RagdollState>(actorEntity);
                if (frameIndex % 30 == 0 || frameIndex > maxFrames - 5)
                {
                    RagdollBody diagnosticBody = bakingWorld.EntityManager.GetBuffer<RagdollBody>(actorEntity)[0];
                    diagnostics.Add(
                        "frame=" + frameIndex +
                        " y=" + diagnosticBody.state.position.y +
                        " linVel=" + diagnosticBody.state.linearVelocity +
                        " angVel=" + diagnosticBody.state.angularVelocity +
                        " sleepTimer=" + currentState.sleepTimer +
                        " flags=" + currentState.flags);
                }
                if ((currentState.flags & RagdollStateFlags.Sleeping) != 0)
                {
                    wentToSleep = true;
                    break;
                }
            }

            if (!wentToSleep)
            {
                StringBuilder dump = new StringBuilder();
                for (int diagnosticIndex = 0; diagnosticIndex < diagnostics.Count; diagnosticIndex++)
                {
                    dump.Append(diagnostics[diagnosticIndex]).Append('\n');
                }
                Assert.Fail(
                    "Guard: the body must actually settle and sleep within " + maxFrames +
                    " frames, or nothing below tests what it claims to.\n" + dump);
            }

            LocalTransform settledTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(actorEntity);
            RagdollBody settledBody = bakingWorld.EntityManager.GetBuffer<RagdollBody>(actorEntity)[0];

            // The stomp: a value nothing about the settled ragdoll would produce, written directly
            // onto the same LocalTransform component TransformApplySystem writes every frame for an
            // animated part.
            LocalTransform stompedTransform = LocalTransform.FromPositionRotationScale(
                new float3(999f, 999f, 999f), quaternion.RotateZ(math.radians(123f)), 1f);
            bakingWorld.EntityManager.SetComponentData(actorEntity, stompedTransform);

            // One more full ragdoll frame. RagdollSolveSystem must skip dynamics for a sleeping
            // actor (leaving RagdollBody.state exactly as it was), but RagdollApplySystem must still
            // run unconditionally and overwrite the stomp with the settled pose.
            RunOneRagdollFrame(1f / 60f);

            RagdollBody bodyAfterStomp = bakingWorld.EntityManager.GetBuffer<RagdollBody>(actorEntity)[0];
            Assert.AreEqual(
                settledBody.state.position.x, bodyAfterStomp.state.position.x, 1e-6f,
                "A sleeping actor's body state must not change: RagdollSolveSystem skips dynamics while asleep.");
            Assert.AreEqual(settledBody.state.position.y, bodyAfterStomp.state.position.y, 1e-6f);
            Assert.AreEqual(settledBody.state.position.z, bodyAfterStomp.state.position.z, 1e-6f);

            LocalTransform correctedTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(actorEntity);
            Assert.AreNotEqual(
                stompedTransform.Position.x, correctedTransform.Position.x,
                "RagdollApplySystem must have overwritten the stomp; this is the defect G1 warns about.");
            Assert.AreEqual(
                settledTransform.Position.x, correctedTransform.Position.x, 1e-4f,
                "The corrected pose must be the settled ragdoll pose, not merely 'not the stomp'.");
            Assert.AreEqual(settledTransform.Position.y, correctedTransform.Position.y, 1e-4f);
            Assert.AreEqual(settledTransform.Position.z, correctedTransform.Position.z, 1e-4f);
        }

        // -----------------------------------------------------------------------------------
        // Fixture helpers.
        // -----------------------------------------------------------------------------------

        private ClipSetAsset CreateClipSet(out RigAsset rig)
        {
            rig = fixture.CreateRig("Rig");
            return fixture.CreateClipSet("Set", 0x4700UL);
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

        /// <summary>
        /// Tuned so a dropped box lying near the fallback ground plane settles and sleeps within a
        /// few dozen frames rather than the production defaults' half-second delay stacked on a slow
        /// approach.
        /// </summary>
        private void CreateFastSettlingRagdollConfig()
        {
            bakingWorld.EntityManager.CreateSingleton(
                new RagdollConfig
                {
                    worldGravity = new float3(0f, -9.81f, 0f),
                    sleepLinearSpeed = 0.1f,
                    sleepAngularSpeed = 0.1f,
                    sleepDelaySeconds = 0.2f,
                    maxSubstepsPerFrame = 8,
                    fallbackGroundHeight = 0f,
                    contactProbeRadius = 0.02f
                },
                "RagdollConfig");
        }

        private void EnableRagdoll(Entity actorEntity)
        {
            bakingWorld.EntityManager.SetComponentEnabled<RagdollActor>(actorEntity, true);
        }

        private void RunCapture()
        {
            UpdateSystem<RagdollCaptureSystem>();
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
    }
}
