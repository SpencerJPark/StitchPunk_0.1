// Copyright (c) 2026 Spencer Park. All rights reserved.

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
    /// Covers <c>CutsceneTimelineSystem</c>'s central contract (Phase G §4, §6, HANDOFF's own
    /// framing): a skipped cutscene and a fully played-through one must leave the exact same world
    /// state, not merely a close one.
    /// </summary>
    /// <remarks>
    /// The blob is built by hand with a raw <see cref="BlobBuilder"/> rather than through
    /// <c>CutsceneBlobBuilder</c> (Authoring), matching this suite's existing convention
    /// (<see cref="PlaybackTestActor.BuildRegistry"/> does the same for <c>ClipRegistryBlob</c>) —
    /// PlayMode fixtures exercise the runtime systems against a well-formed blob, never the bake
    /// pipeline, so a bake defect and a system defect can never be confused with each other.
    /// </remarks>
    public sealed class CutsceneTimelineSystemTests
    {
        private const ulong WalkClipId = 500;
        private const uint SlotId = 1;
        private const uint FireOnSkipEventKey = 42;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private double elapsedTime;
        private readonly List<BlobAssetReference<CutsceneBlob>> cutsceneBlobs = new List<BlobAssetReference<CutsceneBlob>>();

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CutsceneTimelineSystemTests");
            elapsedTime = 0d;
            registry = PlaybackTestActor.BuildRegistry(new[]
            {
                new PlaybackTestActor.ClipSpec { clipId = WalkClipId, duration = 2f, defaultLoop = LoopMode.Once }
            });
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
        }

        [Test]
        public void SkippedAndPlayedThrough_LeaveTheSameFinalTransformAndEvents()
        {
            float3 playedThroughPosition;
            int playedThroughEventCount;
            RunToCompletion(playThrough: true, out playedThroughPosition, out playedThroughEventCount);

            float3 skippedPosition;
            int skippedEventCount;
            RunToCompletion(playThrough: false, out skippedPosition, out skippedEventCount);

            Assert.AreEqual(playedThroughPosition.x, skippedPosition.x, 1e-4f, "root x position must match exactly");
            Assert.AreEqual(playedThroughPosition.y, skippedPosition.y, 1e-4f, "root y position must match exactly");
            Assert.AreEqual(playedThroughPosition.z, skippedPosition.z, 1e-4f, "root z position must match exactly");
            Assert.AreEqual(10f, playedThroughPosition.x, 1e-4f, "sanity: the played-through run actually reached the final key");
            Assert.AreEqual(playedThroughEventCount, skippedEventCount, "a skip must fire every event a play-through would have");
            Assert.AreEqual(1, skippedEventCount, "sanity: exactly one event was authored");
        }

        /// <summary>
        /// Covers Phase G7: a Prop slot has no rig, no clip lane, and no <c>AnimationCommand</c>/
        /// <c>RigPartRef</c> on its bound entity at all — just a transform. Root motion must still
        /// apply through the same <c>ApplyPose</c> path an Actor slot uses.
        /// </summary>
        [Test]
        public void PropSlot_WithNoRigOrCommandBuffer_StillGetsRootMotion()
        {
            Entity propEntity = testWorld.EntityManager.CreateEntity();
            testWorld.EntityManager.AddComponentData(propEntity, LocalTransform.Identity);

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildPropOnlyCutsceneBlob();
            cutsceneBlobs.Add(cutsceneBlob);
            Entity requestEntity = CutscenePlaybackApi.CreatePlayRequest(testWorld.EntityManager, cutsceneBlob);
            testWorld.EntityManager.GetBuffer<CutsceneActorBinding>(requestEntity).Add(new CutsceneActorBinding
            {
                slotId = SlotId,
                actorEntity = propEntity
            });

            Advance(1f);

            float3 position = testWorld.EntityManager.GetComponentData<LocalTransform>(propEntity).Position;
            Assert.AreEqual(5f, position.x, 1e-4f, "a prop with no rig or clip lane must still get root motion");
            Assert.IsTrue(
                testWorld.EntityManager.GetComponentData<CutscenePlaybackState>(requestEntity).isComplete);
        }

        /// <summary>
        /// Covers amendment A62 defect 2: a slot with no root keys must not be teleported to the
        /// world origin every frame. Both <c>CutsceneBlobSampler.SampleTransform</c>'s old zero
        /// default and the editor's identical <c>CutscenePoseSampler.Sample</c> wrote it unconditionally.
        /// </summary>
        [Test]
        public void EmptyRootLane_LeavesTheBoundTransformAlone()
        {
            Entity propEntity = testWorld.EntityManager.CreateEntity();
            testWorld.EntityManager.AddComponentData(propEntity, LocalTransform.FromPosition(new float3(3f, 0f, 0f)));

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildEmptyRootLaneCutsceneBlob();
            cutsceneBlobs.Add(cutsceneBlob);
            Entity requestEntity = CutscenePlaybackApi.CreatePlayRequest(testWorld.EntityManager, cutsceneBlob);
            testWorld.EntityManager.GetBuffer<CutsceneActorBinding>(requestEntity).Add(new CutsceneActorBinding
            {
                slotId = SlotId,
                actorEntity = propEntity
            });

            Advance(0.5f);

            float3 position = testWorld.EntityManager.GetComponentData<LocalTransform>(propEntity).Position;
            Assert.AreEqual(3f, position.x, 1e-4f, "a slot with no root keys must not be moved at all");
            Assert.AreEqual(0f, position.y, 1e-4f);
            Assert.AreEqual(0f, position.z, 1e-4f);
        }

        [Test]
        public void Skip_MarksComplete_AndStopsTheActorLayer()
        {
            Entity actorEntity = CreateBoundActor(out Entity requestEntity);
            CutscenePlaybackApi.RequestSkip(testWorld.EntityManager, requestEntity);
            Advance(0.1f);

            CutscenePlaybackState playbackState =
                testWorld.EntityManager.GetComponentData<CutscenePlaybackState>(requestEntity);
            Assert.IsTrue(playbackState.isComplete);

            DynamicBuffer<AnimationCommand> commands = testWorld.EntityManager.GetBuffer<AnimationCommand>(actorEntity);
            bool sawStop = false;
            for (int i = 0; i < commands.Length; i++)
            {
                if (commands[i].kind == CommandKind.Stop)
                {
                    sawStop = true;
                }
            }
            Assert.IsTrue(sawStop, "skip must release the actor's clip layer, per spec §6's end/skip contract");
        }

        /// <summary>Amendment A61-T1: <c>CreatePlayRequestFromStage</c> must copy every <c>CutsceneStageBinding</c> into the request's <c>CutsceneActorBinding</c> buffer, in order.</summary>
        [Test]
        public void CreatePlayRequestFromStage_CopiesEveryStageBinding()
        {
            const uint SecondSlotId = SlotId + 1;

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildTestCutsceneBlob();
            cutsceneBlobs.Add(cutsceneBlob);

            Entity boundEntityA = testWorld.EntityManager.CreateEntity();
            Entity boundEntityB = testWorld.EntityManager.CreateEntity();

            Entity stageEntity = testWorld.EntityManager.CreateEntity();
            testWorld.EntityManager.AddComponentData(stageEntity, new CutsceneStage
            {
                blob = cutsceneBlob,
                cutsceneKey = cutsceneBlob.Value.cutsceneKey
            });
            DynamicBuffer<CutsceneStageBinding> stageBindings =
                testWorld.EntityManager.AddBuffer<CutsceneStageBinding>(stageEntity);
            stageBindings.Add(new CutsceneStageBinding { slotId = SlotId, target = boundEntityA });
            stageBindings.Add(new CutsceneStageBinding { slotId = SecondSlotId, target = boundEntityB });

            Entity requestEntity =
                CutscenePlaybackApi.CreatePlayRequestFromStage(testWorld.EntityManager, stageEntity);

            DynamicBuffer<CutsceneActorBinding> actorBindings =
                testWorld.EntityManager.GetBuffer<CutsceneActorBinding>(requestEntity);
            Assert.AreEqual(2, actorBindings.Length, "one CutsceneActorBinding per CutsceneStageBinding");
            Assert.AreEqual(SlotId, actorBindings[0].slotId);
            Assert.AreEqual(boundEntityA, actorBindings[0].actorEntity);
            Assert.AreEqual(SecondSlotId, actorBindings[1].slotId);
            Assert.AreEqual(boundEntityB, actorBindings[1].actorEntity);
        }

        private void RunToCompletion(bool playThrough, out float3 finalPosition, out int finalEventCount)
        {
            World runWorld = new World("CutsceneTimelineSystemTests_Run");
            try
            {
                Entity actorEntity = CreateBoundActorIn(runWorld, out Entity requestEntity);

                if (playThrough)
                {
                    double localElapsed = 0d;
                    for (int step = 0; step < 5; step++)
                    {
                        localElapsed += 0.5d;
                        runWorld.SetTime(new TimeData(localElapsed, 0.5f));
                        SystemHandle timelineSystem = runWorld.GetOrCreateSystem<CutsceneTimelineSystem>();
                        timelineSystem.Update(runWorld.Unmanaged);
                        runWorld.EntityManager.CompleteAllTrackedJobs();
                    }
                }
                else
                {
                    CutscenePlaybackApi.RequestSkip(runWorld.EntityManager, requestEntity);
                    runWorld.SetTime(new TimeData(0.5d, 0.5f));
                    SystemHandle timelineSystem = runWorld.GetOrCreateSystem<CutsceneTimelineSystem>();
                    timelineSystem.Update(runWorld.Unmanaged);
                    runWorld.EntityManager.CompleteAllTrackedJobs();
                }

                Assert.IsTrue(
                    runWorld.EntityManager.GetComponentData<CutscenePlaybackState>(requestEntity).isComplete,
                    "the run must actually reach completion for this comparison to mean anything");

                finalPosition = runWorld.EntityManager.GetComponentData<LocalTransform>(actorEntity).Position;
                finalEventCount = runWorld.EntityManager.GetBuffer<AnimEventOutput>(requestEntity).Length;
            }
            finally
            {
                runWorld.Dispose();
            }
        }

        private Entity CreateBoundActor(out Entity requestEntity)
        {
            return CreateBoundActorIn(testWorld, out requestEntity);
        }

        /// <summary>Builds one actor bound to a fresh cutscene play request in <paramref name="world"/>: one segment, one clip block, root motion from (0,0,0) to (10,0,0), and one fire-on-skip event at t=1.</summary>
        private Entity CreateBoundActorIn(World world, out Entity requestEntity)
        {
            Entity actorEntity = PlaybackTestActor.CreateActor(world, registry, layerCount: 2);
            world.EntityManager.AddComponentData(actorEntity, LocalTransform.Identity);

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildTestCutsceneBlob();
            cutsceneBlobs.Add(cutsceneBlob);
            requestEntity = CutscenePlaybackApi.CreatePlayRequest(world.EntityManager, cutsceneBlob, layerIndex: 0);
            world.EntityManager.GetBuffer<CutsceneActorBinding>(requestEntity).Add(new CutsceneActorBinding
            {
                slotId = SlotId,
                actorEntity = actorEntity
            });

            return actorEntity;
        }

        private void Advance(float deltaTime)
        {
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));
            SystemHandle timelineSystem = testWorld.GetOrCreateSystem<CutsceneTimelineSystem>();
            timelineSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        /// <summary>One Prop slot, one segment, no holds: root motion only, spanning [0, 1].</summary>
        private static BlobAssetReference<CutsceneBlob> BuildPropOnlyCutsceneBlob()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 1;
                root.cutsceneKey = 1UL;

                BlobBuilderArray<CutsceneSlotMetaBlob> slots = builder.Allocate(ref root.slots, 1);
                slots[0] = new CutsceneSlotMetaBlob { slotId = SlotId, kind = CutsceneSlotKind.Prop };

                BlobBuilderArray<CutsceneSegmentBlob> segments = builder.Allocate(ref root.segments, 1);
                ref CutsceneSegmentBlob segment = ref segments[0];
                segment.duration = 1f;
                segment.holdId = default;

                BlobBuilderArray<CutsceneSlotSegmentBlob> slotTracks = builder.Allocate(ref segment.slotTracks, 1);
                ref CutsceneSlotSegmentBlob slotSegment = ref slotTracks[0];

                builder.Allocate(ref slotSegment.clipBlocks, 0);
                BlobBuilderArray<CutsceneTransformKeyBlob> transformKeys =
                    builder.Allocate(ref slotSegment.transformKeys, 2);
                transformKeys[0] = new CutsceneTransformKeyBlob
                {
                    time = 0f,
                    position = new float3(0f, 0f, 0f),
                    rotation = float3.zero,
                    scale = new float3(1f, 1f, 1f),
                    interpolation = Interpolation.Linear
                };
                transformKeys[1] = new CutsceneTransformKeyBlob
                {
                    time = 1f,
                    position = new float3(5f, 0f, 0f),
                    rotation = float3.zero,
                    scale = new float3(1f, 1f, 1f),
                    interpolation = Interpolation.Linear
                };
                builder.Allocate(ref slotSegment.facingKeys, 0);
                builder.Allocate(ref slotSegment.partTracks, 0);

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

        /// <summary>One Prop slot, one segment, zero root keys — the amendment A62 defect 2 shape.</summary>
        private static BlobAssetReference<CutsceneBlob> BuildEmptyRootLaneCutsceneBlob()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 1;
                root.cutsceneKey = 1UL;

                BlobBuilderArray<CutsceneSlotMetaBlob> slots = builder.Allocate(ref root.slots, 1);
                slots[0] = new CutsceneSlotMetaBlob { slotId = SlotId, kind = CutsceneSlotKind.Prop };

                BlobBuilderArray<CutsceneSegmentBlob> segments = builder.Allocate(ref root.segments, 1);
                ref CutsceneSegmentBlob segment = ref segments[0];
                segment.duration = 1f;
                segment.holdId = default;

                BlobBuilderArray<CutsceneSlotSegmentBlob> slotTracks = builder.Allocate(ref segment.slotTracks, 1);
                ref CutsceneSlotSegmentBlob slotSegment = ref slotTracks[0];

                builder.Allocate(ref slotSegment.clipBlocks, 0);
                builder.Allocate(ref slotSegment.transformKeys, 0);
                builder.Allocate(ref slotSegment.facingKeys, 0);
                builder.Allocate(ref slotSegment.partTracks, 0);

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

        /// <summary>One segment, no holds: a clip block and root motion both spanning [0, 2], one event at t=1.</summary>
        private static BlobAssetReference<CutsceneBlob> BuildTestCutsceneBlob()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 1;
                root.cutsceneKey = 1UL;

                BlobBuilderArray<CutsceneSlotMetaBlob> slots = builder.Allocate(ref root.slots, 1);
                slots[0] = new CutsceneSlotMetaBlob { slotId = SlotId, kind = CutsceneSlotKind.Actor };

                BlobBuilderArray<CutsceneSegmentBlob> segments = builder.Allocate(ref root.segments, 1);
                ref CutsceneSegmentBlob segment = ref segments[0];
                segment.duration = 2f;
                segment.holdId = default;

                BlobBuilderArray<CutsceneSlotSegmentBlob> slotTracks = builder.Allocate(ref segment.slotTracks, 1);
                ref CutsceneSlotSegmentBlob slotSegment = ref slotTracks[0];

                BlobBuilderArray<CutsceneClipBlockBlob> clipBlocks = builder.Allocate(ref slotSegment.clipBlocks, 1);
                clipBlocks[0] = new CutsceneClipBlockBlob { clipId = WalkClipId, start = 0f, duration = 2f, loop = false };

                BlobBuilderArray<CutsceneTransformKeyBlob> transformKeys =
                    builder.Allocate(ref slotSegment.transformKeys, 2);
                transformKeys[0] = new CutsceneTransformKeyBlob
                {
                    time = 0f,
                    position = new float3(0f, 0f, 0f),
                    rotation = float3.zero,
                    scale = new float3(1f, 1f, 1f),
                    interpolation = Interpolation.Linear
                };
                transformKeys[1] = new CutsceneTransformKeyBlob
                {
                    time = 2f,
                    position = new float3(10f, 0f, 0f),
                    rotation = float3.zero,
                    scale = new float3(1f, 1f, 1f),
                    interpolation = Interpolation.Linear
                };

                builder.Allocate(ref slotSegment.facingKeys, 0);
                builder.Allocate(ref slotSegment.partTracks, 0);

                builder.Allocate(ref segment.cameraKeys, 0);
                builder.Allocate(ref segment.cameraCutTimes, 0);

                BlobBuilderArray<CutsceneEventMarkerBlob> events = builder.Allocate(ref segment.events, 1);
                events[0] = new CutsceneEventMarkerBlob
                {
                    time = 1f,
                    eventKey = FireOnSkipEventKey,
                    intParam = 0,
                    floatParam = 0f,
                    fireOnSkip = true
                };

                return builder.CreateBlobAssetReference<CutsceneBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }
    }
}
