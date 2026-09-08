// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers runtime facing: a cutscene that walks an actor along its root lane must say which way
    /// that actor is facing (amendment A65 §3.2), and — amendment A73 §3.3 — must fold that angle
    /// onto the bound actor's own <c>ActorProfile.turnDirections</c> and write
    /// <see cref="ActorFacing"/>, honour a Fixed-or-Auto facing key, and latch a resolved mark's
    /// arrival facing until the actor moves again.
    /// </summary>
    /// <remarks>
    /// Blobs are hand-built, matching this suite's convention: the bake has its own fixtures, and a
    /// bake defect must not be able to masquerade as a player defect.
    /// </remarks>
    public sealed class CutsceneFacingTests
    {
        private const uint SlotId = 1;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private double elapsedTime;
        private readonly List<BlobAssetReference<CutsceneBlob>> cutsceneBlobs =
            new List<BlobAssetReference<CutsceneBlob>>();
        private readonly List<BlobAssetReference<ActorProfileBlob>> profileBlobs =
            new List<BlobAssetReference<ActorProfileBlob>>();
        private readonly List<Object> scriptableObjects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CutsceneFacingTests");
            elapsedTime = 0d;
            registry = PlaybackTestActor.BuildRegistry(new PlaybackTestActor.ClipSpec[0]);
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
            for (int i = 0; i < cutsceneBlobs.Count; i++)
            {
                if (cutsceneBlobs[i].IsCreated)
                {
                    cutsceneBlobs[i].Dispose();
                }
            }
            cutsceneBlobs.Clear();

            for (int i = 0; i < profileBlobs.Count; i++)
            {
                if (profileBlobs[i].IsCreated)
                {
                    profileBlobs[i].Dispose();
                }
            }
            profileBlobs.Clear();

            for (int i = 0; i < scriptableObjects.Count; i++)
            {
                if (scriptableObjects[i] != null)
                {
                    Object.DestroyImmediate(scriptableObjects[i]);
                }
            }
            scriptableObjects.Clear();
        }

        private Entity CreateActorWithTurnDirections(AnimationDirections turnDirections)
        {
            ActorProfileAsset profileAsset = ScriptableObject.CreateInstance<ActorProfileAsset>();
            scriptableObjects.Add(profileAsset);
            profileAsset.turnDirections = turnDirections;

            BlobAssetReference<ActorProfileBlob> profileBlob;
            Entity actorEntity = PlaybackTestActor.CreateActorWithProfile(
                testWorld, registry, profileAsset, out profileBlob, layerCount: 2);
            profileBlobs.Add(profileBlob);
            testWorld.EntityManager.AddComponentData(actorEntity, LocalTransform.Identity);
            return actorEntity;
        }

        [Test]
        public void RootTravel_WritesCutsceneFacingAngle()
        {
            Entity actorEntity = CreateActorWithTurnDirections(AnimationDirections.Eight);

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildTurningCutsceneBlob();
            cutsceneBlobs.Add(cutsceneBlob);
            Entity requestEntity = CutsceneApi.CreatePlayRequest(testWorld.EntityManager, cutsceneBlob);
            testWorld.EntityManager.GetBuffer<CutsceneActorBinding>(requestEntity).Add(new CutsceneActorBinding
            {
                slotId = SlotId,
                actorEntity = actorEntity
            });

            Advance(0.5f);

            Assert.IsTrue(testWorld.EntityManager.HasComponent<CutsceneFacing>(actorEntity),
                "a cutscene driving an actor must say which way it is facing");
            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<CutsceneFacing>(actorEntity));
            // 0 degrees is east in this package's model: FacingResolver reads a vector whose x is
            // east and whose y is north, and the angle is measured from +x toward +z.
            Assert.AreEqual(0f,
                testWorld.EntityManager.GetComponentData<CutsceneFacing>(actorEntity).angleDegrees, 1e-3f,
                "travelling along +x is facing east");

            // Turning onto +z: the same lane, a quarter turn later.
            Advance(2.1f);
            Assert.AreEqual(90f,
                testWorld.EntityManager.GetComponentData<CutsceneFacing>(actorEntity).angleDegrees, 1e-3f,
                "and travelling along +z is facing north");
        }

        /// <summary>Amendment A73-D2/§3.3: the resolved angle folds onto the bound actor's own profile turn granularity and is written into ActorFacing.</summary>
        [Test]
        public void RootTravel_WritesActorFacing_SnappedAtTheProfilesTurnDirections()
        {
            Entity actorEntity = CreateActorWithTurnDirections(AnimationDirections.Six);

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildTurningCutsceneBlob();
            cutsceneBlobs.Add(cutsceneBlob);
            Entity requestEntity = CutsceneApi.CreatePlayRequest(testWorld.EntityManager, cutsceneBlob);
            testWorld.EntityManager.GetBuffer<CutsceneActorBinding>(requestEntity).Add(new CutsceneActorBinding
            {
                slotId = SlotId,
                actorEntity = actorEntity
            });

            Advance(2.1f);

            Assert.AreEqual(Direction.North,
                testWorld.EntityManager.GetComponentData<ActorFacing>(actorEntity).facing,
                "travelling along +z folds onto North on a Six-turning profile");
            Assert.AreEqual(90f,
                testWorld.EntityManager.GetComponentData<CutsceneFacing>(actorEntity).angleDegrees, 1e-3f);
        }

        /// <summary>Amendment A73 §3.3: a Fixed key pins the angle; an Auto key at a later time cancels it and facing derives again from root travel.</summary>
        [Test]
        public void AutoFacingKey_HandsFacingBackToTravel()
        {
            Entity actorEntity = CreateActorWithTurnDirections(AnimationDirections.Eight);

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildFixedThenAutoFacingBlob();
            cutsceneBlobs.Add(cutsceneBlob);
            Entity requestEntity = CutsceneApi.CreatePlayRequest(testWorld.EntityManager, cutsceneBlob);
            testWorld.EntityManager.GetBuffer<CutsceneActorBinding>(requestEntity).Add(new CutsceneActorBinding
            {
                slotId = SlotId,
                actorEntity = actorEntity
            });

            Advance(0.5f);
            Assert.AreEqual(180f,
                testWorld.EntityManager.GetComponentData<CutsceneFacing>(actorEntity).angleDegrees, 1e-3f,
                "the Fixed key at 0s pins west (180 degrees) until the Auto key");

            Advance(1f);
            Assert.AreEqual(0f,
                testWorld.EntityManager.GetComponentData<CutsceneFacing>(actorEntity).angleDegrees, 1e-3f,
                "the Auto key at 1s hands facing back to root travel, which is heading east");
        }

        /// <summary>Amendment A73 §3.3: a resolved mark latches its arrival facing until the actor moves again.</summary>
        [Test]
        public void MarkArrival_LatchesTheMarksFacing()
        {
            Entity actorEntity = CreateActorWithTurnDirections(AnimationDirections.Six);

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildMarkCutsceneBlob();
            cutsceneBlobs.Add(cutsceneBlob);
            Entity requestEntity = CutsceneApi.CreatePlayRequest(testWorld.EntityManager, cutsceneBlob);
            testWorld.EntityManager.GetBuffer<CutsceneActorBinding>(requestEntity).Add(new CutsceneActorBinding
            {
                slotId = SlotId,
                actorEntity = actorEntity
            });

            Advance(0.1f);
            Assert.IsTrue(testWorld.EntityManager.HasComponent<CutsceneMoveToMark>(actorEntity),
                "sanity: the mark order reached the actor");

            LocalTransform localTransform = testWorld.EntityManager.GetComponentData<LocalTransform>(actorEntity);
            localTransform.Position = new float3(5f, 0f, 0f);
            testWorld.EntityManager.SetComponentData(actorEntity, localTransform);

            Advance(0.1f);
            Advance(0.1f);

            Assert.AreEqual(Direction.North,
                testWorld.EntityManager.GetComponentData<ActorFacing>(actorEntity).facing,
                "the mark's arrival facing (90 degrees) latches and folds onto North");
        }

        private void Advance(float deltaTime)
        {
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));
            SystemHandle timelineSystem = testWorld.GetOrCreateSystem<CutsceneTimelineSystem>();
            timelineSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        /// <summary>One Actor slot walking east for 2 s and then north for 2 s. No clip blocks — these fixtures test facing only.</summary>
        private static BlobAssetReference<CutsceneBlob> BuildTurningCutsceneBlob()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 6;
                root.cutsceneKey = 1UL;

                BlobBuilderArray<CutsceneSlotMetaBlob> slots = builder.Allocate(ref root.slots, 1);
                slots[0] = new CutsceneSlotMetaBlob { slotId = SlotId, kind = CutsceneSlotKind.Actor };

                BlobBuilderArray<CutsceneSegmentBlob> segments = builder.Allocate(ref root.segments, 1);
                ref CutsceneSegmentBlob segment = ref segments[0];
                segment.duration = 4f;
                segment.holdId = default;

                BlobBuilderArray<CutsceneSlotSegmentBlob> slotTracks = builder.Allocate(ref segment.slotTracks, 1);
                ref CutsceneSlotSegmentBlob slotSegment = ref slotTracks[0];

                builder.Allocate(ref slotSegment.clipBlocks, 0);

                BlobBuilderArray<CutsceneTransformKeyBlob> transformKeys =
                    builder.Allocate(ref slotSegment.transformKeys, 3);
                transformKeys[0] = RootKey(0f, new float3(0f, 0f, 0f));
                transformKeys[1] = RootKey(2f, new float3(10f, 0f, 0f));
                transformKeys[2] = RootKey(4f, new float3(10f, 0f, 10f));

                builder.Allocate(ref slotSegment.facingKeys, 0);
                builder.Allocate(ref slotSegment.partTracks, 0);
                builder.Allocate(ref slotSegment.attachMarkers, 0);
                builder.Allocate(ref slotSegment.markKeys, 0);
                builder.Allocate(ref slotSegment.layerStops, 0);

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

        /// <summary>One Actor slot: a Fixed facing key at 180 degrees at t=0, an Auto key at t=1s, root travelling east for 2s.</summary>
        private static BlobAssetReference<CutsceneBlob> BuildFixedThenAutoFacingBlob()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 6;
                root.cutsceneKey = 1UL;

                BlobBuilderArray<CutsceneSlotMetaBlob> slots = builder.Allocate(ref root.slots, 1);
                slots[0] = new CutsceneSlotMetaBlob { slotId = SlotId, kind = CutsceneSlotKind.Actor };

                BlobBuilderArray<CutsceneSegmentBlob> segments = builder.Allocate(ref root.segments, 1);
                ref CutsceneSegmentBlob segment = ref segments[0];
                segment.duration = 2f;
                segment.holdId = default;

                BlobBuilderArray<CutsceneSlotSegmentBlob> slotTracks = builder.Allocate(ref segment.slotTracks, 1);
                ref CutsceneSlotSegmentBlob slotSegment = ref slotTracks[0];

                builder.Allocate(ref slotSegment.clipBlocks, 0);

                BlobBuilderArray<CutsceneTransformKeyBlob> transformKeys =
                    builder.Allocate(ref slotSegment.transformKeys, 2);
                transformKeys[0] = RootKey(0f, new float3(0f, 0f, 0f));
                transformKeys[1] = RootKey(2f, new float3(10f, 0f, 0f));

                BlobBuilderArray<CutsceneFacingKeyBlob> facingKeys = builder.Allocate(ref slotSegment.facingKeys, 2);
                facingKeys[0] = new CutsceneFacingKeyBlob { time = 0f, angleRadians = math.radians(180f), isAuto = false };
                facingKeys[1] = new CutsceneFacingKeyBlob { time = 1f, angleRadians = 0f, isAuto = true };

                builder.Allocate(ref slotSegment.partTracks, 0);
                builder.Allocate(ref slotSegment.attachMarkers, 0);
                builder.Allocate(ref slotSegment.markKeys, 0);
                builder.Allocate(ref slotSegment.layerStops, 0);

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

        /// <summary>One Actor slot with one mark at t=0: position (5,0,0), arrival facing 90 degrees (north), tolerance 0.5.</summary>
        private static BlobAssetReference<CutsceneBlob> BuildMarkCutsceneBlob()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 6;
                root.cutsceneKey = 1UL;

                BlobBuilderArray<CutsceneSlotMetaBlob> slots = builder.Allocate(ref root.slots, 1);
                slots[0] = new CutsceneSlotMetaBlob { slotId = SlotId, kind = CutsceneSlotKind.Actor };

                BlobBuilderArray<CutsceneSegmentBlob> segments = builder.Allocate(ref root.segments, 1);
                ref CutsceneSegmentBlob segment = ref segments[0];
                segment.duration = 2f;
                segment.holdId = default;

                BlobBuilderArray<CutsceneSlotSegmentBlob> slotTracks = builder.Allocate(ref segment.slotTracks, 1);
                ref CutsceneSlotSegmentBlob slotSegment = ref slotTracks[0];

                builder.Allocate(ref slotSegment.clipBlocks, 0);
                builder.Allocate(ref slotSegment.transformKeys, 0);
                builder.Allocate(ref slotSegment.facingKeys, 0);
                builder.Allocate(ref slotSegment.partTracks, 0);
                builder.Allocate(ref slotSegment.attachMarkers, 0);

                BlobBuilderArray<CutsceneMarkKeyBlob> markKeys = builder.Allocate(ref slotSegment.markKeys, 1);
                markKeys[0] = new CutsceneMarkKeyBlob
                {
                    time = 0f,
                    position = new float3(5f, 0f, 0f),
                    facingRadians = math.radians(90f),
                    toleranceMeters = 0.5f,
                    timeoutSeconds = 0f
                };
                builder.Allocate(ref slotSegment.layerStops, 0);

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

        private static CutsceneTransformKeyBlob RootKey(float time, float3 position)
        {
            return new CutsceneTransformKeyBlob
            {
                time = time,
                position = position,
                rotation = float3.zero,
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.Linear
            };
        }
    }
}
