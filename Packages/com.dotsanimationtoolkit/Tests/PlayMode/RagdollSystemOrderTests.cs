// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// The behavioural half of the ragdoll group's ordering contract (Phase D4, amendment A50,
    /// spec §7, §10): a socket attached to a ragdolling node resolves to the ragdoll's pose <em>this</em>
    /// frame, not one frame late. <c>rigged-characters.md</c> already lists "attachment is one frame
    /// behind" as a known symptom; this proves it is not reintroduced. The attribute-level half —
    /// that the group and its five systems declare the right <c>UpdateInGroup</c>/<c>UpdateAfter</c>/
    /// <c>UpdateBefore</c> edges — lives in <c>SystemGroupStructureTests</c>, alongside every other
    /// group-membership assertion in the package.
    /// </summary>
    public sealed class RagdollSystemOrderTests
    {
        private const uint TorsoSocketId = 500;

        private ActorBakeFixture fixture;
        private BakingTestWorld bakingWorld;
        private double elapsedTime;
        private BlobAssetReference<SocketRegistryBlob> socketRegistry;

        [SetUp]
        public void SetUp()
        {
            fixture = new ActorBakeFixture();
            bakingWorld = new BakingTestWorld("RagdollSystemOrderTests");
            elapsedTime = 0d;
        }

        [TearDown]
        public void TearDown()
        {
            bakingWorld.Dispose();
            fixture.DestroyAll();
            if (socketRegistry.IsCreated)
            {
                socketRegistry.Dispose();
            }
        }

        /// <summary>
        /// Catches: the ragdoll group ordered before <c>SocketResolveSystem</c> writes, or the
        /// group reading a billboard frame from before this frame's <c>BillboardResolveSystem</c>
        /// pass. Discriminated by value, in the same spirit as
        /// <c>SocketResolveSystemTests.TheSocketResolvesFromThisFramesTransform_NotThePreviousFrames</c>:
        /// gravity moves the torso body down from its captured, at-rest y every single frame it
        /// runs, so a socket reading a stale (pre-drop or previous-frame) pose would report the rest
        /// y unchanged, while a socket reading this frame's applied pose reports a strictly lower y.
        /// </summary>
        [Test]
        public void ASocketOnARagdollingNode_ResolvesToThisFramesAppliedPose_NotThePreviousFrames()
        {
            RigAsset rig;
            ClipSetAsset clipSet = CreateClipSet(out rig);
            AddTargetBody(rig, "Torso", ActorBakeFixture.TorsoTargetId, 100u);
            GameObject actorGameObject = fixture.CreateStandardActor("Actor", rig, clipSet, false);

            bakingWorld.Bake(actorGameObject);
            bakingWorld.AssertNoUnexpectedToolkitErrors();
            CreateRealisticRagdollConfig();

            Entity actorEntity = bakingWorld.GetPrimaryEntity(actorGameObject);
            Entity torsoEntity = bakingWorld.GetPrimaryEntity(ActorBakeFixture.FindPart(actorGameObject, "Torso"));

            // Deterministic composition math for the socket: identity actor pose in both the
            // transform the ragdoll composes against and the matrix SocketResolveSystem composes
            // against.
            bakingWorld.EntityManager.SetComponentData(actorEntity, LocalTransform.Identity);
            SetOrAddLocalToWorld(actorEntity, float4x4.identity);

            socketRegistry = BuildSocketRegistry(
                RigTargetSocket(TorsoSocketId, ActorBakeFixture.TorsoDenseIndex, float3.zero));
            bakingWorld.EntityManager.AddComponentData(actorEntity, new SocketRegistry { Value = socketRegistry });

            Entity attachmentEntity = bakingWorld.EntityManager.CreateEntity();
            bakingWorld.EntityManager.AddComponentData(attachmentEntity, new SocketAttachment
            {
                actorRoot = actorEntity,
                socketId = TorsoSocketId,
                localOffset = float3.zero
            });
            bakingWorld.EntityManager.AddComponentData(attachmentEntity, LocalTransform.Identity);

            float restY = bakingWorld.EntityManager.GetComponentData<LocalTransform>(torsoEntity).Position.y;

            EnableRagdoll(actorEntity);
            RunCapture();

            // One full frame in exactly the declared group order: Billboard resolve, then the
            // ragdoll group's own internal order (probe -> solve -> apply -> release), then socket
            // resolve.
            RunOneFrameInDeclaredOrder(1f / 60f);

            LocalTransform torsoAfterFrame = bakingWorld.EntityManager.GetComponentData<LocalTransform>(torsoEntity);
            Assert.Less(
                torsoAfterFrame.Position.y, restY - 1e-5f,
                "Guard: the torso must have genuinely fallen this frame, or the assertion below proves nothing.");

            LocalTransform attachmentTransform = bakingWorld.EntityManager.GetComponentData<LocalTransform>(attachmentEntity);

            Assert.AreNotEqual(
                restY, attachmentTransform.Position.y,
                "A one-frame-late socket would still report the captured rest y; it must not.");
            Assert.AreEqual(
                torsoAfterFrame.Position.x, attachmentTransform.Position.x, 1e-4f,
                "The socket must agree with the pose RagdollApplySystem actually wrote this frame.");
            Assert.AreEqual(torsoAfterFrame.Position.y, attachmentTransform.Position.y, 1e-4f);
            Assert.AreEqual(torsoAfterFrame.Position.z, attachmentTransform.Position.z, 1e-4f);
        }

        // -----------------------------------------------------------------------------------
        // Fixture helpers.
        // -----------------------------------------------------------------------------------

        private ClipSetAsset CreateClipSet(out RigAsset rig)
        {
            rig = fixture.CreateRig("Rig");
            return fixture.CreateClipSet("Set", 0x4800UL);
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

        private void CreateRealisticRagdollConfig()
        {
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

        private void SetOrAddLocalToWorld(Entity entity, float4x4 value)
        {
            if (bakingWorld.EntityManager.HasComponent<LocalToWorld>(entity))
            {
                bakingWorld.EntityManager.SetComponentData(entity, new LocalToWorld { Value = value });
            }
            else
            {
                bakingWorld.EntityManager.AddComponentData(entity, new LocalToWorld { Value = value });
            }
        }

        private void EnableRagdoll(Entity actorEntity)
        {
            bakingWorld.EntityManager.SetComponentEnabled<RagdollActor>(actorEntity, true);
        }

        private void RunCapture()
        {
            UpdateSystem<RagdollCaptureSystem>();
        }

        /// <summary>
        /// One toolkit frame, in exactly the order spec §7's diagram declares:
        /// <c>BillboardResolveSystem</c>, then the ragdoll group's own five systems in their
        /// declared internal order, then <c>SocketResolveSystem</c>.
        /// </summary>
        private void RunOneFrameInDeclaredOrder(float deltaTime)
        {
            elapsedTime += deltaTime;
            bakingWorld.World.SetTime(new TimeData(elapsedTime, deltaTime));

            UpdateSystem<BillboardResolveSystem>();
            UpdateSystem<RagdollProbeFallbackSystem>();
            UpdateSystem<RagdollSolveSystem>();
            UpdateSystem<RagdollApplySystem>();
            UpdateSystem<RagdollReleaseSystem>();
            UpdateSystem<SocketResolveSystem>();

            bakingWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void UpdateSystem<TSystem>() where TSystem : unmanaged, ISystem
        {
            SystemHandle systemHandle = bakingWorld.World.GetOrCreateSystem<TSystem>();
            systemHandle.Update(bakingWorld.World.Unmanaged);
            bakingWorld.EntityManager.CompleteAllTrackedJobs();
        }

        // -----------------------------------------------------------------------------------
        // Socket registry blob construction — mirrors SocketResolveSystemTests' own builder.
        // -----------------------------------------------------------------------------------

        private struct SocketSpec
        {
            internal uint socketId;
            internal int targetIndex;
            internal float3 localPosition;
        }

        private static SocketSpec RigTargetSocket(uint socketId, int targetIndex, float3 localPosition)
        {
            return new SocketSpec { socketId = socketId, targetIndex = targetIndex, localPosition = localPosition };
        }

        private static BlobAssetReference<SocketRegistryBlob> BuildSocketRegistry(params SocketSpec[] socketSpecs)
        {
            BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref SocketRegistryBlob root = ref blobBuilder.ConstructRoot<SocketRegistryBlob>();
                root.schemaVersion = SocketRegistryBuilder.SchemaVersion;
                root.rigKey = 1UL;

                BlobBuilderArray<uint> idArray = blobBuilder.Allocate(ref root.sortedSocketIds, socketSpecs.Length);
                BlobBuilderArray<SocketDefinitionBlob> definitionArray =
                    blobBuilder.Allocate(ref root.sockets, socketSpecs.Length);
                for (int socketIndex = 0; socketIndex < socketSpecs.Length; socketIndex++)
                {
                    SocketSpec socketSpec = socketSpecs[socketIndex];
                    idArray[socketIndex] = socketSpec.socketId;
                    definitionArray[socketIndex] = new SocketDefinitionBlob
                    {
                        mode = SocketAttachMode.RigTarget,
                        targetIndex = socketSpec.targetIndex,
                        layerIndex = 0,
                        localPosition = socketSpec.localPosition,
                        localRotation = quaternion.identity
                    };
                }

                blobBuilder.Allocate(ref root.clipTracks, 0);

                return blobBuilder.CreateBlobAssetReference<SocketRegistryBlob>(Allocator.Persistent);
            }
            finally
            {
                blobBuilder.Dispose();
            }
        }
    }
}
