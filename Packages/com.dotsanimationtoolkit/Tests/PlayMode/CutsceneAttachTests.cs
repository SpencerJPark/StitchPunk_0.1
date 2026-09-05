// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers the attach lane's runtime half (amendment A63 §3.3): a slot bound to a host stops
    /// obeying its own root lane, a detach hands the entity back independent with a signal the host
    /// can act on, and a skip leaves the same world a watched run would.
    /// </summary>
    /// <remarks>
    /// The blob is hand-built rather than baked, matching this suite's convention — a bake defect
    /// and a system defect must never be able to masquerade as each other.
    /// </remarks>
    public sealed class CutsceneAttachTests
    {
        private const uint PropSlotId = 1;
        private const uint HostSlotId = 2;
        private const uint HandSocketId = 7;

        private World testWorld;
        private double elapsedTime;
        private readonly List<BlobAssetReference<CutsceneBlob>> cutsceneBlobs =
            new List<BlobAssetReference<CutsceneBlob>>();
        private BlobAssetReference<SocketRegistryBlob> socketRegistry;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CutsceneAttachTests");
            elapsedTime = 0d;
            socketRegistry = BuildSocketRegistry(HandSocketId);
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;

            for (int i = 0; i < cutsceneBlobs.Count; i++)
            {
                if (cutsceneBlobs[i].IsCreated)
                {
                    cutsceneBlobs[i].Dispose();
                }
            }
            cutsceneBlobs.Clear();

            if (socketRegistry.IsCreated)
            {
                socketRegistry.Dispose();
            }
        }

        [Test]
        public void AttachToSocket_AddsSocketAttachment_AndSuspendsRootLane()
        {
            Entity propEntity;
            Entity hostEntity;
            CreateStage(
                attachTime: 1f, socketId: HandSocketId, hideWhileAttached: false,
                detachTime: -1f, detachImpulse: float3.zero,
                propEntity: out propEntity, hostEntity: out hostEntity);

            Advance(0.5f);
            Assert.AreEqual(2.5f, PropPosition(propEntity).x, 1e-4f,
                "sanity: before the attach the prop is driven by its own root lane");

            Advance(0.5f);
            Assert.IsTrue(testWorld.EntityManager.HasComponent<SocketAttachment>(propEntity),
                "the attach marker must bind the prop through SocketAttachment (decision A63-D1)");
            SocketAttachment attachment = testWorld.EntityManager.GetComponentData<SocketAttachment>(propEntity);
            Assert.AreEqual(hostEntity, attachment.actorRoot);
            Assert.AreEqual(HandSocketId, attachment.socketId);
            Assert.IsFalse(testWorld.EntityManager.HasComponent<Parent>(propEntity),
                "a socket attachment is never also parented - that transforms it twice");

            Advance(0.5f);
            Assert.AreEqual(2.5f, PropPosition(propEntity).x, 1e-4f,
                "while attached the host owns the transform; the root lane must not keep writing it");
        }

        /// <summary>
        /// Detach (A63-T2) and hide/unhide (A63-T3) in one run: the same attachment carries both,
        /// and asserting them separately would need two identical fixtures.
        /// </summary>
        [Test]
        public void Detach_RestoresIndependence_AndRaisesTheSignal()
        {
            Entity propEntity;
            Entity hostEntity;
            CreateStage(
                attachTime: 1f, socketId: HandSocketId, hideWhileAttached: true,
                detachTime: 2f, detachImpulse: new float3(0f, 5f, 0f),
                propEntity: out propEntity, hostEntity: out hostEntity);

            Advance(1f);
            Assert.IsTrue(testWorld.EntityManager.HasComponent<SocketAttachment>(propEntity),
                "sanity: the prop is attached before the detach is asserted");
            Assert.IsTrue(testWorld.EntityManager.HasComponent<DisableRendering>(propEntity),
                "hideWhileAttached must suppress rendering for as long as the attachment lasts");

            Advance(1f);
            Assert.IsFalse(testWorld.EntityManager.HasComponent<SocketAttachment>(propEntity),
                "detach must leave the entity independent again");
            Assert.IsFalse(testWorld.EntityManager.HasComponent<DisableRendering>(propEntity),
                "detach must reveal an entity it hid");
            Assert.IsTrue(testWorld.EntityManager.HasComponent<CutsceneDetachSignal>(propEntity));
            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<CutsceneDetachSignal>(propEntity),
                "the signal is enabled for the host to read on the detach frame");

            CutsceneDetachSignal signal = testWorld.EntityManager.GetComponentData<CutsceneDetachSignal>(propEntity);
            Assert.AreEqual(5f, signal.worldImpulse.y, 1e-4f,
                "an identity host rotation passes the authored impulse straight through");
            Assert.AreEqual(hostEntity, signal.previousHost);
        }

        [Test]
        public void Skip_AppliesRemainingAttachMarkers()
        {
            Entity propEntity;
            Entity hostEntity;
            Entity requestEntity = CreateStage(
                attachTime: 1f, socketId: HandSocketId, hideWhileAttached: true,
                detachTime: 2f, detachImpulse: new float3(0f, 5f, 0f),
                propEntity: out propEntity, hostEntity: out hostEntity);

            CutscenePlaybackApi.RequestSkip(testWorld.EntityManager, requestEntity);
            Advance(0.1f);

            Assert.IsTrue(
                testWorld.EntityManager.GetComponentData<CutscenePlaybackState>(requestEntity).isComplete,
                "sanity: the skip actually completed the cutscene");
            Assert.IsFalse(testWorld.EntityManager.HasComponent<SocketAttachment>(propEntity),
                "a skip replays the attach and the detach, landing on the same end state (A63-D3)");
            Assert.IsFalse(testWorld.EntityManager.HasComponent<DisableRendering>(propEntity),
                "the hide a skipped attach applied must be lifted by the detach it also replays");
            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<CutsceneDetachSignal>(propEntity),
                "a host waiting on the detach signal must still get it from a skipped run");
        }

        private float3 PropPosition(Entity propEntity)
        {
            return testWorld.EntityManager.GetComponentData<LocalTransform>(propEntity).Position;
        }

        /// <summary>
        /// One Prop slot riding one host Actor slot: the prop's root lane runs (0,0,0) → (10,0,0)
        /// over 2 s while the attach lane binds it at <paramref name="attachTime"/> and, when
        /// <paramref name="detachTime"/> is non-negative, releases it again.
        /// </summary>
        private Entity CreateStage(
            float attachTime, uint socketId, bool hideWhileAttached, float detachTime, float3 detachImpulse,
            out Entity propEntity, out Entity hostEntity)
        {
            EntityManager entityManager = testWorld.EntityManager;

            propEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(propEntity, LocalTransform.Identity);
            // No renderer needed: DisableRendering is only ever applied to entities that carry
            // MaterialMeshInfo, which is what makes it the right marker to assert on.
            entityManager.AddComponentData(propEntity, default(MaterialMeshInfo));

            hostEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(hostEntity, LocalTransform.Identity);
            entityManager.AddComponentData(hostEntity, new SocketRegistry { Value = socketRegistry });

            BlobAssetReference<CutsceneBlob> cutsceneBlob =
                BuildAttachCutsceneBlob(attachTime, socketId, hideWhileAttached, detachTime, detachImpulse);
            cutsceneBlobs.Add(cutsceneBlob);

            Entity requestEntity = CutscenePlaybackApi.CreatePlayRequest(entityManager, cutsceneBlob);
            DynamicBuffer<CutsceneActorBinding> bindings =
                entityManager.GetBuffer<CutsceneActorBinding>(requestEntity);
            bindings.Add(new CutsceneActorBinding { slotId = PropSlotId, actorEntity = propEntity });
            bindings.Add(new CutsceneActorBinding { slotId = HostSlotId, actorEntity = hostEntity });
            return requestEntity;
        }

        private void Advance(float deltaTime)
        {
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));
            SystemHandle timelineSystem = testWorld.GetOrCreateSystem<CutsceneTimelineSystem>();
            timelineSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private static BlobAssetReference<CutsceneBlob> BuildAttachCutsceneBlob(
            float attachTime, uint socketId, bool hideWhileAttached, float detachTime, float3 detachImpulse)
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 3;
                root.cutsceneKey = 1UL;

                BlobBuilderArray<CutsceneSlotMetaBlob> slots = builder.Allocate(ref root.slots, 2);
                slots[0] = new CutsceneSlotMetaBlob { slotId = PropSlotId, kind = CutsceneSlotKind.Prop };
                slots[1] = new CutsceneSlotMetaBlob { slotId = HostSlotId, kind = CutsceneSlotKind.Actor };

                BlobBuilderArray<CutsceneSegmentBlob> segments = builder.Allocate(ref root.segments, 1);
                ref CutsceneSegmentBlob segment = ref segments[0];
                segment.duration = 3f;
                segment.holdId = default;

                BlobBuilderArray<CutsceneSlotSegmentBlob> slotTracks = builder.Allocate(ref segment.slotTracks, 2);

                ref CutsceneSlotSegmentBlob propSegment = ref slotTracks[0];
                builder.Allocate(ref propSegment.clipBlocks, 0);
                BlobBuilderArray<CutsceneTransformKeyBlob> propKeys =
                    builder.Allocate(ref propSegment.transformKeys, 2);
                propKeys[0] = new CutsceneTransformKeyBlob
                {
                    time = 0f,
                    position = float3.zero,
                    rotation = float3.zero,
                    scale = new float3(1f, 1f, 1f),
                    interpolation = Interpolation.Linear
                };
                propKeys[1] = new CutsceneTransformKeyBlob
                {
                    time = 2f,
                    position = new float3(10f, 0f, 0f),
                    rotation = float3.zero,
                    scale = new float3(1f, 1f, 1f),
                    interpolation = Interpolation.Linear
                };
                builder.Allocate(ref propSegment.facingKeys, 0);
                builder.Allocate(ref propSegment.partTracks, 0);

                int markerCount = detachTime >= 0f ? 2 : 1;
                BlobBuilderArray<CutsceneAttachMarkerBlob> attachMarkers =
                    builder.Allocate(ref propSegment.attachMarkers, markerCount);
                attachMarkers[0] = new CutsceneAttachMarkerBlob
                {
                    time = attachTime,
                    kind = CutsceneAttachKind.Attach,
                    hostSlotIndex = 1,
                    socketId = socketId,
                    localOffset = float3.zero,
                    localRotation = quaternion.identity,
                    hideWhileAttached = hideWhileAttached
                };
                if (markerCount == 2)
                {
                    attachMarkers[1] = new CutsceneAttachMarkerBlob
                    {
                        time = detachTime,
                        kind = CutsceneAttachKind.Detach,
                        hostSlotIndex = -1,
                        localRotation = quaternion.identity,
                        detachImpulse = detachImpulse
                    };
                }

                ref CutsceneSlotSegmentBlob hostSegment = ref slotTracks[1];
                builder.Allocate(ref hostSegment.clipBlocks, 0);
                builder.Allocate(ref hostSegment.transformKeys, 0);
                builder.Allocate(ref hostSegment.facingKeys, 0);
                builder.Allocate(ref hostSegment.partTracks, 0);
                builder.Allocate(ref hostSegment.attachMarkers, 0);

                builder.Allocate(ref segment.cameraKeys, 0);
                builder.Allocate(ref segment.cameraCutTimes, 0);
                builder.Allocate(ref segment.events, 0);

                return builder.CreateBlobAssetReference<CutsceneBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }

        private static BlobAssetReference<SocketRegistryBlob> BuildSocketRegistry(uint socketId)
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref SocketRegistryBlob root = ref builder.ConstructRoot<SocketRegistryBlob>();
                root.schemaVersion = 1;
                root.rigKey = 1UL;

                BlobBuilderArray<uint> ids = builder.Allocate(ref root.sortedSocketIds, 1);
                ids[0] = socketId;
                BlobBuilderArray<SocketDefinitionBlob> definitions = builder.Allocate(ref root.sockets, 1);
                definitions[0] = new SocketDefinitionBlob
                {
                    mode = SocketAttachMode.RigTarget,
                    targetIndex = 0,
                    layerIndex = 0,
                    localPosition = float3.zero,
                    localRotation = quaternion.identity
                };
                builder.Allocate(ref root.clipTracks, 0);

                return builder.CreateBlobAssetReference<SocketRegistryBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }
    }
}
