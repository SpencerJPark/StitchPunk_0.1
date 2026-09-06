// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers the marks lane's runtime half (amendment A64 §3.3): the order reaches the bound
    /// entity, a rendezvous hold resumes the instant everyone has arrived, and a mover that never
    /// arrives is placed rather than left to softlock the scene.
    /// </summary>
    /// <remarks>
    /// The blob is hand-built rather than baked, matching this suite's convention — a bake defect
    /// and a system defect must never be able to masquerade as each other.
    /// </remarks>
    public sealed class CutsceneMarkTests
    {
        private const uint WalkerSlotId = 1;
        private static readonly float3 MarkPosition = new float3(5f, 0f, 0f);

        private World testWorld;
        private double elapsedTime;
        private readonly List<BlobAssetReference<CutsceneBlob>> cutsceneBlobs =
            new List<BlobAssetReference<CutsceneBlob>>();

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CutsceneMarkTests");
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

            for (int i = 0; i < cutsceneBlobs.Count; i++)
            {
                if (cutsceneBlobs[i].IsCreated)
                {
                    cutsceneBlobs[i].Dispose();
                }
            }
            cutsceneBlobs.Clear();
        }

        [Test]
        public void MarkTime_EnablesMoveToMarkOnTheBoundEntity()
        {
            Entity walkerEntity;
            CreateStage(timeoutSeconds: 0f, walkerEntity: out walkerEntity);

            Advance(0.25f);
            Assert.IsFalse(testWorld.EntityManager.HasComponent<CutsceneMoveToMark>(walkerEntity),
                "sanity: nothing is ordered before the mark's own time");

            Advance(0.25f);
            Assert.IsTrue(testWorld.EntityManager.HasComponent<CutsceneMoveToMark>(walkerEntity));
            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<CutsceneMoveToMark>(walkerEntity),
                "the order is enabled for the host to act on (decision A64-D1)");
            CutsceneMoveToMark order =
                testWorld.EntityManager.GetComponentData<CutsceneMoveToMark>(walkerEntity);
            Assert.AreEqual(MarkPosition.x, order.position.x, 1e-4f);
            Assert.AreEqual(0.5f, order.toleranceMeters, 1e-4f);
        }

        [Test]
        public void RendezvousHold_AutoReleasesWhenEveryMarkIsReached()
        {
            Entity walkerEntity;
            Entity requestEntity = CreateStage(timeoutSeconds: 0f, walkerEntity: out walkerEntity);

            Advance(1f);
            Assert.AreEqual(0, SegmentIndex(requestEntity),
                "the clock waits at the rendezvous hold while the walker is still on its way");
            Assert.IsTrue(
                testWorld.EntityManager.GetComponentData<CutscenePlaybackState>(requestEntity).isPausedOnHold);

            Advance(0.25f);
            Assert.AreEqual(0, SegmentIndex(requestEntity),
                "waiting is the point: an outstanding mark must not release on its own");

            // What the host's movement would have done, minus the pathfinding the toolkit refuses
            // to grow: the walker is now standing on the mark.
            SetWalkerPosition(walkerEntity, MarkPosition);
            Advance(0.25f);

            Assert.IsFalse(testWorld.EntityManager.IsComponentEnabled<CutsceneMoveToMark>(walkerEntity),
                "arrival disables the order rather than leaving the host to guess");
            Assert.AreEqual(1, SegmentIndex(requestEntity),
                "with nothing outstanding the rendezvous hold releases itself");
        }

        [Test]
        public void MarkTimeout_TeleportsAndReleases()
        {
            Entity walkerEntity;
            Entity requestEntity = CreateStage(timeoutSeconds: 0.5f, walkerEntity: out walkerEntity);

            LogAssert.Expect(LogType.Warning, new Regex("did not reach its mark"));

            Advance(0.5f);
            Advance(0.25f);
            Advance(0.25f);
            Assert.AreEqual(MarkPosition.x, WalkerPosition(walkerEntity).x, 1e-4f,
                "a mover that never arrives is placed on the mark once its timeout expires");
            Assert.IsFalse(testWorld.EntityManager.IsComponentEnabled<CutsceneMoveToMark>(walkerEntity));

            Advance(0.25f);
            Assert.AreEqual(1, SegmentIndex(requestEntity),
                "the timeout exists so a stuck mover cannot softlock the rendezvous hold");
        }

        private int SegmentIndex(Entity requestEntity)
        {
            return testWorld.EntityManager.GetComponentData<CutscenePlaybackState>(requestEntity).segmentIndex;
        }

        private float3 WalkerPosition(Entity walkerEntity)
        {
            return testWorld.EntityManager.GetComponentData<LocalTransform>(walkerEntity).Position;
        }

        private void SetWalkerPosition(Entity walkerEntity, float3 position)
        {
            LocalTransform localTransform =
                testWorld.EntityManager.GetComponentData<LocalTransform>(walkerEntity);
            localTransform.Position = position;
            testWorld.EntityManager.SetComponentData(walkerEntity, localTransform);
        }

        /// <summary>
        /// One Actor slot with one mark at 0.5 s, a 1 s segment ending on a rendezvous hold, and a
        /// second segment after it. The slot has no root keys at all, so nothing but the mark can
        /// move the walker — a root lane would make "did it arrive?" untestable.
        /// </summary>
        private Entity CreateStage(float timeoutSeconds, out Entity walkerEntity)
        {
            EntityManager entityManager = testWorld.EntityManager;

            walkerEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(walkerEntity, LocalTransform.Identity);

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildMarkCutsceneBlob(timeoutSeconds);
            cutsceneBlobs.Add(cutsceneBlob);

            Entity requestEntity = CutscenePlaybackApi.CreatePlayRequest(entityManager, cutsceneBlob);
            DynamicBuffer<CutsceneActorBinding> bindings =
                entityManager.GetBuffer<CutsceneActorBinding>(requestEntity);
            bindings.Add(new CutsceneActorBinding { slotId = WalkerSlotId, actorEntity = walkerEntity });
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

        private static BlobAssetReference<CutsceneBlob> BuildMarkCutsceneBlob(float timeoutSeconds)
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 4;
                root.cutsceneKey = 1UL;

                BlobBuilderArray<CutsceneSlotMetaBlob> slots = builder.Allocate(ref root.slots, 1);
                slots[0] = new CutsceneSlotMetaBlob { slotId = WalkerSlotId, kind = CutsceneSlotKind.Actor };

                BlobBuilderArray<CutsceneSegmentBlob> segments = builder.Allocate(ref root.segments, 2);

                ref CutsceneSegmentBlob rendezvousSegment = ref segments[0];
                rendezvousSegment.duration = 1f;
                FixedString64Bytes holdId = default;
                holdId.CopyFromTruncated("Rendezvous");
                rendezvousSegment.holdId = holdId;
                rendezvousSegment.autoReleaseWhenMarksReached = true;
                BlobBuilderArray<CutsceneSlotSegmentBlob> rendezvousTracks =
                    builder.Allocate(ref rendezvousSegment.slotTracks, 1);
                AllocateEmptySlotSegment(ref builder, ref rendezvousTracks[0]);
                BlobBuilderArray<CutsceneMarkKeyBlob> markKeys =
                    builder.Allocate(ref rendezvousTracks[0].markKeys, 1);
                markKeys[0] = new CutsceneMarkKeyBlob
                {
                    time = 0.5f,
                    position = MarkPosition,
                    facingRadians = 0f,
                    toleranceMeters = 0.5f,
                    timeoutSeconds = timeoutSeconds
                };
                builder.Allocate(ref rendezvousSegment.cameraKeys, 0);
                builder.Allocate(ref rendezvousSegment.cameraCutTimes, 0);
                builder.Allocate(ref rendezvousSegment.events, 0);

                ref CutsceneSegmentBlob departSegment = ref segments[1];
                departSegment.duration = 1f;
                departSegment.holdId = default;
                departSegment.autoReleaseWhenMarksReached = false;
                BlobBuilderArray<CutsceneSlotSegmentBlob> departTracks =
                    builder.Allocate(ref departSegment.slotTracks, 1);
                AllocateEmptySlotSegment(ref builder, ref departTracks[0]);
                builder.Allocate(ref departTracks[0].markKeys, 0);
                builder.Allocate(ref departSegment.cameraKeys, 0);
                builder.Allocate(ref departSegment.cameraCutTimes, 0);
                builder.Allocate(ref departSegment.events, 0);

                return builder.CreateBlobAssetReference<CutsceneBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }

        private static void AllocateEmptySlotSegment(
            ref BlobBuilder builder, ref CutsceneSlotSegmentBlob slotSegment)
        {
            builder.Allocate(ref slotSegment.clipBlocks, 0);
            builder.Allocate(ref slotSegment.transformKeys, 0);
            builder.Allocate(ref slotSegment.facingKeys, 0);
            builder.Allocate(ref slotSegment.partTracks, 0);
            builder.Allocate(ref slotSegment.attachMarkers, 0);
        }
    }
}
