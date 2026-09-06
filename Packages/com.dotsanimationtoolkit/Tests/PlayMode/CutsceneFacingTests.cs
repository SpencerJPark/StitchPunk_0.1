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
    /// Covers runtime facing (amendment A65 §3.2): a cutscene that walks an actor along its root
    /// lane must say which way that actor is facing, and must re-pick its direction set's variant
    /// when the walk turns — the gap Phase G recorded and never closed, which is why an actor could
    /// only face correctly in the editor preview.
    /// </summary>
    /// <remarks>
    /// Blobs are hand-built, matching this suite's convention: the bake has its own fixtures, and a
    /// bake defect must not be able to masquerade as a player defect.
    /// </remarks>
    public sealed class CutsceneFacingTests
    {
        private const uint SlotId = 1;
        private const ulong EastClipId = 700;
        private const ulong NorthClipId = 701;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private double elapsedTime;
        private readonly List<BlobAssetReference<CutsceneBlob>> cutsceneBlobs =
            new List<BlobAssetReference<CutsceneBlob>>();

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CutsceneFacingTests");
            elapsedTime = 0d;
            registry = PlaybackTestActor.BuildRegistry(new[]
            {
                new PlaybackTestActor.ClipSpec { clipId = EastClipId, duration = 2f, defaultLoop = LoopMode.Loop },
                new PlaybackTestActor.ClipSpec { clipId = NorthClipId, duration = 2f, defaultLoop = LoopMode.Loop }
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
        public void RootTravel_WritesCutsceneFacingAngle()
        {
            Entity actorEntity = PlaybackTestActor.CreateActor(testWorld, registry, layerCount: 2);
            testWorld.EntityManager.AddComponentData(actorEntity, LocalTransform.Identity);

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildTurningCutsceneBlob(withVariants: false);
            cutsceneBlobs.Add(cutsceneBlob);
            Entity requestEntity = CutscenePlaybackApi.CreatePlayRequest(testWorld.EntityManager, cutsceneBlob);
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

        [Test]
        public void FacingChange_ReissuesTheDirectionVariantWithTimeCarried()
        {
            const float CarriedClipTime = 1.234f;

            Entity actorEntity = PlaybackTestActor.CreateActor(testWorld, registry, layerCount: 2);
            testWorld.EntityManager.AddComponentData(actorEntity, LocalTransform.Identity);

            BlobAssetReference<CutsceneBlob> cutsceneBlob = BuildTurningCutsceneBlob(withVariants: true);
            cutsceneBlobs.Add(cutsceneBlob);
            Entity requestEntity = CutscenePlaybackApi.CreatePlayRequest(testWorld.EntityManager, cutsceneBlob);
            testWorld.EntityManager.GetBuffer<CutsceneActorBinding>(requestEntity).Add(new CutsceneActorBinding
            {
                slotId = SlotId,
                actorEntity = actorEntity
            });

            Advance(0.5f);
            DynamicBuffer<AnimationCommand> commands =
                testWorld.EntityManager.GetBuffer<AnimationCommand>(actorEntity);
            Assert.AreEqual(1, CountPlays(commands, EastClipId),
                "the block starts on the variant its facing already calls for, not on the authored side");
            Assert.AreEqual(0, CountPlays(commands, NorthClipId));

            // Stand in for the playback systems this fixture does not run: the layer has been
            // playing for a while, and that phase is what the swap has to carry over.
            DynamicBuffer<PlaybackLayer> layers = testWorld.EntityManager.GetBuffer<PlaybackLayer>(actorEntity);
            PlaybackLayer layer = layers[0];
            layer.time = CarriedClipTime;
            layers[0] = layer;

            Advance(2.1f);

            commands = testWorld.EntityManager.GetBuffer<AnimationCommand>(actorEntity);
            int northPlayIndex = -1;
            for (int commandIndex = 0; commandIndex < commands.Length; commandIndex++)
            {
                if (commands[commandIndex].kind == CommandKind.Play
                    && commands[commandIndex].clip.Value == NorthClipId)
                {
                    northPlayIndex = commandIndex;
                }
            }
            Assert.GreaterOrEqual(northPlayIndex, 0,
                "turning onto a facing the set serves with another clip must re-pick the variant");
            Assert.AreEqual(0f, commands[northPlayIndex].blendDuration, 1e-4f,
                "a variant swap is the same motion continuing, not a transition to blend");
            Assert.Less(northPlayIndex + 1, commands.Length,
                "the swap must be followed by the SetTime that carries the phase");
            Assert.AreEqual(CommandKind.SetTime, commands[northPlayIndex + 1].kind);
            Assert.AreEqual(CarriedClipTime, commands[northPlayIndex + 1].time, 1e-4f,
                "without the carried time the walk cycle restarts on frame 0 every time the actor turns");

            Advance(0.5f);
            commands = testWorld.EntityManager.GetBuffer<AnimationCommand>(actorEntity);
            Assert.AreEqual(1, CountPlays(commands, NorthClipId),
                "the variant is re-picked on the turn, not re-issued every frame after it");
        }

        private static int CountPlays(DynamicBuffer<AnimationCommand> commands, ulong clipId)
        {
            int count = 0;
            for (int commandIndex = 0; commandIndex < commands.Length; commandIndex++)
            {
                if (commands[commandIndex].kind == CommandKind.Play
                    && commands[commandIndex].clip.Value == clipId)
                {
                    count++;
                }
            }
            return count;
        }

        private void Advance(float deltaTime)
        {
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));
            SystemHandle timelineSystem = testWorld.GetOrCreateSystem<CutsceneTimelineSystem>();
            timelineSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        /// <summary>
        /// One Actor slot walking east for 2 s and then north for 2 s, playing one looping block for
        /// the whole run. With <paramref name="withVariants"/> the block's clip is a member of an
        /// eight-direction set, so the turn calls for a different clip rather than the same one
        /// mirrored.
        /// </summary>
        private static BlobAssetReference<CutsceneBlob> BuildTurningCutsceneBlob(bool withVariants)
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 5;
                root.cutsceneKey = 1UL;

                BlobBuilderArray<CutsceneSlotMetaBlob> slots = builder.Allocate(ref root.slots, 1);
                slots[0] = new CutsceneSlotMetaBlob { slotId = SlotId, kind = CutsceneSlotKind.Actor };

                BlobBuilderArray<CutsceneSegmentBlob> segments = builder.Allocate(ref root.segments, 1);
                ref CutsceneSegmentBlob segment = ref segments[0];
                segment.duration = 4f;
                segment.holdId = default;

                BlobBuilderArray<CutsceneSlotSegmentBlob> slotTracks = builder.Allocate(ref segment.slotTracks, 1);
                ref CutsceneSlotSegmentBlob slotSegment = ref slotTracks[0];

                BlobBuilderArray<CutsceneClipBlockBlob> clipBlocks =
                    builder.Allocate(ref slotSegment.clipBlocks, 1);
                clipBlocks[0] = new CutsceneClipBlockBlob
                {
                    clipId = EastClipId,
                    start = 0f,
                    duration = 4f,
                    loop = true,
                    blendDuration = 0f,
                    directionVariants = withVariants
                        ? new CutsceneDirectionVariantsBlob
                        {
                            hasVariants = true,
                            south = EastClipId,
                            southEast = EastClipId,
                            east = EastClipId,
                            northEast = NorthClipId,
                            north = NorthClipId,
                            targetDirections = AnimationDirections.Eight,
                            effectiveDirections = AnimationDirections.Eight
                        }
                        : default
                };

                BlobBuilderArray<CutsceneTransformKeyBlob> transformKeys =
                    builder.Allocate(ref slotSegment.transformKeys, 3);
                transformKeys[0] = RootKey(0f, new float3(0f, 0f, 0f));
                transformKeys[1] = RootKey(2f, new float3(10f, 0f, 0f));
                transformKeys[2] = RootKey(4f, new float3(10f, 0f, 10f));

                builder.Allocate(ref slotSegment.facingKeys, 0);
                builder.Allocate(ref slotSegment.partTracks, 0);
                builder.Allocate(ref slotSegment.attachMarkers, 0);
                builder.Allocate(ref slotSegment.markKeys, 0);

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
