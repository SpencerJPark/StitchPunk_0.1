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
    /// Covers amendment A73 §3.3 auto locomotion: the profile's moving/standing entry plays from the
    /// bound actor's own real displacement, and an authored block on the locomotion layer suppresses
    /// it until a stop key hands the layer back.
    /// </summary>
    /// <remarks>The blob is hand-built, matching this suite's cutscene runtime convention.</remarks>
    public sealed class CutsceneLocomotionTests
    {
        private const uint SlotId = 1;
        private const uint IdleAnimationKey = 1;
        private const uint WalkAnimationKey = 2;
        private const uint SitAnimationKey = 3;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private double elapsedTime;
        private readonly List<BlobAssetReference<CutsceneBlob>> cutsceneBlobs = new List<BlobAssetReference<CutsceneBlob>>();
        private readonly List<BlobAssetReference<ActorProfileBlob>> profileBlobs = new List<BlobAssetReference<ActorProfileBlob>>();
        private readonly List<Object> scriptableObjects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CutsceneLocomotionTests");
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

        /// <summary>Idle and Walk on Base; optionally Sit on Base too, for the authored-wins fixture.</summary>
        private Entity CreateActor(bool includeSitEntry)
        {
            ActorProfileAsset profileAsset = ScriptableObject.CreateInstance<ActorProfileAsset>();
            scriptableObjects.Add(profileAsset);
            profileAsset.layers[0].animations.Add(new ActorAnimationDefinition
            {
                animationKey = IdleAnimationKey, hasDirections = false, clip = CreateClip("IdleClip", 501UL)
            });
            profileAsset.layers[0].animations.Add(new ActorAnimationDefinition
            {
                animationKey = WalkAnimationKey, hasDirections = false, clip = CreateClip("WalkClip", 502UL)
            });
            if (includeSitEntry)
            {
                profileAsset.layers[0].animations.Add(new ActorAnimationDefinition
                {
                    animationKey = SitAnimationKey, hasDirections = false, clip = CreateClip("SitClip", 503UL)
                });
            }

            BlobAssetReference<ActorProfileBlob> profileBlob;
            Entity actorEntity = PlaybackTestActor.CreateActorWithProfile(
                testWorld, registry, profileAsset, out profileBlob, layerCount: 2);
            profileBlobs.Add(profileBlob);
            testWorld.EntityManager.AddComponentData(actorEntity, LocalTransform.Identity);
            return actorEntity;
        }

        private ClipAsset CreateClip(string assetName, ulong clipStableId)
        {
            ClipAsset clip = PlaybackTestActor.CreateClipAsset(assetName, clipStableId);
            scriptableObjects.Add(clip);
            return clip;
        }

        [Test]
        public void RootTravel_PlaysTheMovingEntry_ThenTheStandingOneWhenStill()
        {
            Entity actorEntity = CreateActor(includeSitEntry: false);

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildLocomotionOnlyBlob(travelDuration: 2f, segmentDuration: 4f);
            cutsceneBlobs.Add(cutsceneBlob);
            Entity requestEntity = CutsceneApi.CreatePlayRequest(testWorld.EntityManager, cutsceneBlob);
            testWorld.EntityManager.GetBuffer<CutsceneActorBinding>(requestEntity).Add(new CutsceneActorBinding
            {
                slotId = SlotId,
                actorEntity = actorEntity
            });

            AdvanceTo(1f, 0.1f);
            DynamicBuffer<PlaybackLayer> layers = testWorld.EntityManager.GetBuffer<PlaybackLayer>(actorEntity);
            Assert.AreEqual(WalkAnimationKey, layers[0].animationKey, "still travelling at 1s: the moving entry plays");

            AdvanceTo(3f, 0.1f);
            layers = testWorld.EntityManager.GetBuffer<PlaybackLayer>(actorEntity);
            Assert.AreEqual(IdleAnimationKey, layers[0].animationKey, "root travel stopped at 2s: by 3s the standing entry has taken over");
        }

        [Test]
        public void AuthoredBlockOnTheLocomotionLayer_SuppressesAuto_UntilAStopKey()
        {
            Entity actorEntity = CreateActor(includeSitEntry: true);

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildSitThenStopBlob();
            cutsceneBlobs.Add(cutsceneBlob);
            Entity requestEntity = CutsceneApi.CreatePlayRequest(testWorld.EntityManager, cutsceneBlob);
            testWorld.EntityManager.GetBuffer<CutsceneActorBinding>(requestEntity).Add(new CutsceneActorBinding
            {
                slotId = SlotId,
                actorEntity = actorEntity
            });

            AdvanceTo(1f, 0.1f);
            DynamicBuffer<PlaybackLayer> layers = testWorld.EntityManager.GetBuffer<PlaybackLayer>(actorEntity);
            Assert.AreEqual(SitAnimationKey, layers[0].animationKey,
                "the authored Sit block claims Base from its start; auto locomotion must not touch it");

            AdvanceTo(3f, 0.1f);
            layers = testWorld.EntityManager.GetBuffer<PlaybackLayer>(actorEntity);
            Assert.AreEqual(WalkAnimationKey, layers[0].animationKey,
                "the stop key at 2s hands Base back to auto locomotion, and the actor is still travelling");
        }

        private void AdvanceTo(float targetElapsed, float step)
        {
            SystemHandle timelineSystem = testWorld.GetOrCreateSystem<CutsceneTimelineSystem>();
            SystemHandle commandApplySystem = testWorld.GetOrCreateSystem<CommandApplySystem>();
            while (elapsedTime + step <= targetElapsed + 1e-4f)
            {
                elapsedTime += step;
                testWorld.SetTime(new TimeData(elapsedTime, step));
                timelineSystem.Update(testWorld.Unmanaged);
                testWorld.EntityManager.CompleteAllTrackedJobs();
                commandApplySystem.Update(testWorld.Unmanaged);
                testWorld.EntityManager.CompleteAllTrackedJobs();
            }
        }

        /// <summary>One Actor slot, locomotion Idle/Walk on Base, root travel (0,0,0) to (4,0,0) over <paramref name="travelDuration"/>s then holding still for the rest of the segment.</summary>
        private static BlobAssetReference<CutsceneBlob> BuildLocomotionOnlyBlob(float travelDuration, float segmentDuration)
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 6;
                root.cutsceneKey = 1UL;

                BlobBuilderArray<CutsceneSlotMetaBlob> slots = builder.Allocate(ref root.slots, 1);
                slots[0] = new CutsceneSlotMetaBlob
                {
                    slotId = SlotId,
                    kind = CutsceneSlotKind.Actor,
                    locomotion = new CutsceneLocomotionBlob
                    {
                        enabled = true,
                        standingKey = IdleAnimationKey,
                        movingKey = WalkAnimationKey,
                        speedThreshold = 0.05f
                    }
                };

                BlobBuilderArray<CutsceneSegmentBlob> segments = builder.Allocate(ref root.segments, 1);
                ref CutsceneSegmentBlob segment = ref segments[0];
                segment.duration = segmentDuration;
                segment.holdId = default;

                BlobBuilderArray<CutsceneSlotSegmentBlob> slotTracks = builder.Allocate(ref segment.slotTracks, 1);
                ref CutsceneSlotSegmentBlob slotSegment = ref slotTracks[0];

                builder.Allocate(ref slotSegment.clipBlocks, 0);
                BlobBuilderArray<CutsceneTransformKeyBlob> transformKeys = builder.Allocate(ref slotSegment.transformKeys, 2);
                transformKeys[0] = RootKey(0f, float3.zero);
                transformKeys[1] = RootKey(travelDuration, new float3(4f, 0f, 0f));
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

        /// <summary>One Actor slot, locomotion Idle/Walk on Base, a Sit block on Base at 0s (duration 4s), a stop on Base at 2s, root travel (0,0,0)->(8,0,0) over the whole 4s.</summary>
        private static BlobAssetReference<CutsceneBlob> BuildSitThenStopBlob()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 6;
                root.cutsceneKey = 1UL;

                BlobBuilderArray<CutsceneSlotMetaBlob> slots = builder.Allocate(ref root.slots, 1);
                slots[0] = new CutsceneSlotMetaBlob
                {
                    slotId = SlotId,
                    kind = CutsceneSlotKind.Actor,
                    locomotion = new CutsceneLocomotionBlob
                    {
                        enabled = true,
                        standingKey = IdleAnimationKey,
                        movingKey = WalkAnimationKey,
                        speedThreshold = 0.05f
                    }
                };

                BlobBuilderArray<CutsceneSegmentBlob> segments = builder.Allocate(ref root.segments, 1);
                ref CutsceneSegmentBlob segment = ref segments[0];
                segment.duration = 4f;
                segment.holdId = default;

                BlobBuilderArray<CutsceneSlotSegmentBlob> slotTracks = builder.Allocate(ref segment.slotTracks, 1);
                ref CutsceneSlotSegmentBlob slotSegment = ref slotTracks[0];

                BlobBuilderArray<CutsceneClipBlockBlob> clipBlocks = builder.Allocate(ref slotSegment.clipBlocks, 1);
                clipBlocks[0] = new CutsceneClipBlockBlob
                {
                    animationKey = SitAnimationKey, start = 0f, duration = 4f, loop = LoopMode.Loop, blendDuration = float.NaN, speed = 1f
                };

                BlobBuilderArray<CutsceneTransformKeyBlob> transformKeys = builder.Allocate(ref slotSegment.transformKeys, 2);
                transformKeys[0] = RootKey(0f, float3.zero);
                transformKeys[1] = RootKey(4f, new float3(8f, 0f, 0f));

                builder.Allocate(ref slotSegment.facingKeys, 0);
                builder.Allocate(ref slotSegment.partTracks, 0);
                builder.Allocate(ref slotSegment.attachMarkers, 0);
                builder.Allocate(ref slotSegment.markKeys, 0);

                BlobBuilderArray<CutsceneLayerStopBlob> layerStops = builder.Allocate(ref slotSegment.layerStops, 1);
                layerStops[0] = new CutsceneLayerStopBlob { time = 2f, layerIndex = 0, blendOut = 0f };

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
