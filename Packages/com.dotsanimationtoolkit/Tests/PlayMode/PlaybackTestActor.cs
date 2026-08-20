// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Builds the minimum actor the C4.3 playback systems need: a clip registry blob and an entity
    /// carrying the components <c>CommandApplySystem</c> and <c>PlaybackTimeSystem</c> read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entities are hand-built rather than baked, for the same reason <c>RigBindingSystemTests</c>
    /// builds its own: a bake would drag C3's bakers into every failure and make it ambiguous which
    /// half broke. These fixtures are about two systems' behaviour given a well-formed actor.
    /// </para>
    /// <para>
    /// Clips carry no tracks and no event markers. Nothing in C4.3 samples or emits; adding data the
    /// systems under test never read would only make a failure harder to localise.
    /// </para>
    /// </remarks>
    internal static class PlaybackTestActor
    {
        /// <summary>Authoring-side description of one clip in a test registry.</summary>
        internal sealed class ClipSpec
        {
            internal ulong clipId = 1;
            internal float duration = 1f;
            internal LoopMode defaultLoop = LoopMode.Once;
            internal float defaultBlendIn;
            internal float defaultBlendOut;

            /// <summary>Markers, which the bake guarantees are sorted ascending by normalized time.</summary>
            internal EventSpec[] events = Array.Empty<EventSpec>();

            /// <summary>Transform tracks, which the bake sorts by dense target index.</summary>
            internal TransformTrackSpec[] transformTracks = Array.Empty<TransformTrackSpec>();

            /// <summary>Sprite tracks, which the bake sorts by dense target index.</summary>
            internal SpriteTrackSpec[] spriteTracks = Array.Empty<SpriteTrackSpec>();

            /// <summary>First global VAT frame of this clip's range; -1 = the clip has no VAT range.</summary>
            internal int vatFrameStart = -1;

            /// <summary>VAT frames baked for this clip, including the duplicated loop-safe frame.</summary>
            internal int vatFrameCount;

            /// <summary>Rate the VAT frames were baked at.</summary>
            internal float vatFps;

            /// <summary>
            /// Conservative bounds in <em>offset</em> space — deltas from each target's rest pose,
            /// origin-centred by construction (architecture section 4.6, amendment A13).
            /// </summary>
            internal AABB offsetBounds = new AABB { Center = float3.zero, Extents = float3.zero };
        }

        /// <summary>Authoring-side description of one sprite track.</summary>
        internal sealed class SpriteTrackSpec
        {
            internal int targetIndex;
            internal SpriteFrameMode mode = SpriteFrameMode.Slice;

            /// <summary>Absolute frames or offsets from the rest slice (amendment A37).</summary>
            internal SpriteSliceSpace sliceSpace = SpriteSliceSpace.Absolute;

            internal SpriteKeySpec[] keys = Array.Empty<SpriteKeySpec>();
        }

        /// <summary>Authoring-side description of one sprite key.</summary>
        internal struct SpriteKeySpec
        {
            internal float normalizedTime;
            internal int sliceIndex;
            internal float4 atlasRect;
        }

        /// <summary>Builds a slice-mode sprite key; a negative index means "no change".</summary>
        internal static SpriteKeySpec SliceKey(float normalizedTime, int sliceIndex)
        {
            return new SpriteKeySpec
            {
                normalizedTime = normalizedTime,
                sliceIndex = sliceIndex,
                atlasRect = ClipSampler.IdentityAtlasRect
            };
        }

        /// <summary>Builds an atlas-mode sprite key: scale.xy, offset.zw.</summary>
        internal static SpriteKeySpec AtlasKey(float normalizedTime, float4 atlasRect)
        {
            return new SpriteKeySpec
            {
                normalizedTime = normalizedTime,
                sliceIndex = -1,
                atlasRect = atlasRect
            };
        }

        /// <summary>Authoring-side description of one transform track.</summary>
        internal sealed class TransformTrackSpec
        {
            internal int targetIndex;
            internal TrackBlendOp blendOp = TrackBlendOp.Override;
            internal AnimatedChannels channels = AnimatedChannels.PositionXY;
            internal TransformKeySpec[] keys = Array.Empty<TransformKeySpec>();
        }

        /// <summary>Authoring-side description of one transform key.</summary>
        internal struct TransformKeySpec
        {
            internal float normalizedTime;
            internal float3 position;
            internal float rotationZ;
            internal float3 scale;
            internal Interpolation interpolation;
        }

        /// <summary>Builds a single transform key; scale defaults to 1 so it is a no-op when unmasked.</summary>
        internal static TransformKeySpec Key(
            float normalizedTime,
            float positionX = 0f,
            float positionY = 0f,
            float positionZ = 0f,
            float rotationZ = 0f,
            float scaleX = 1f,
            float scaleY = 1f)
        {
            return new TransformKeySpec
            {
                normalizedTime = normalizedTime,
                position = new float3(positionX, positionY, positionZ),
                rotationZ = rotationZ,
                scale = new float3(scaleX, scaleY, 1f),
                interpolation = Interpolation.Linear
            };
        }

        /// <summary>Authoring-side description of one event marker.</summary>
        internal struct EventSpec
        {
            internal float normalizedTime;
            internal uint eventKey;
            internal int intParam;
            internal float floatParam;
            internal float windowSeconds;
        }

        /// <summary>Builds a marker at <paramref name="normalizedTime"/> carrying a user event key.</summary>
        internal static EventSpec Marker(
            float normalizedTime,
            uint eventKey,
            int intParam = 0,
            float floatParam = 0f,
            float windowSeconds = 0f)
        {
            return new EventSpec
            {
                normalizedTime = normalizedTime,
                eventKey = eventKey,
                intParam = intParam,
                floatParam = floatParam,
                windowSeconds = windowSeconds
            };
        }

        /// <summary>
        /// Builds a registry in the canonical layout the baker produces: clips sorted by ascending
        /// id, with <c>sortedClipIds</c> holding those ids in the same positions, so a clip's dense
        /// index is its position in ascending-id order — what
        /// <see cref="ClipRegistryUtil.TryResolveClip"/> returns.
        /// </summary>
        /// <remarks>The returned reference is caller-owned; the fixture must dispose it.</remarks>
        internal static BlobAssetReference<ClipRegistryBlob> BuildRegistry(
            ClipSpec[] clipSpecs,
            byte layerCount = 4,
            int targetCount = 0,
            int framesPerVariant = 1)
        {
            ClipSpec[] canonicalSpecs = new ClipSpec[clipSpecs.Length];
            Array.Copy(clipSpecs, canonicalSpecs, clipSpecs.Length);
            Array.Sort(canonicalSpecs, CompareByClipId);

            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref ClipRegistryBlob registryRoot = ref builder.ConstructRoot<ClipRegistryBlob>();
                registryRoot.schemaVersion = 2;
                registryRoot.setKey = 1;
                registryRoot.vatSetKey = 0;
                registryRoot.layerCount = layerCount;

                BlobBuilderArray<ClipBlob> clipArray = builder.Allocate(ref registryRoot.clips, canonicalSpecs.Length);
                BlobBuilderArray<ulong> sortedClipIdArray =
                    builder.Allocate(ref registryRoot.sortedClipIds, canonicalSpecs.Length);
                for (int denseClipIndex = 0; denseClipIndex < canonicalSpecs.Length; denseClipIndex++)
                {
                    ClipSpec clipSpec = canonicalSpecs[denseClipIndex];
                    ref ClipBlob clip = ref clipArray[denseClipIndex];
                    clip.clipId = clipSpec.clipId;
                    clip.debugName = new FixedString64Bytes("TestClip");
                    clip.duration = clipSpec.duration;
                    clip.defaultLoop = clipSpec.defaultLoop;
                    clip.defaultBlendIn = clipSpec.defaultBlendIn;
                    clip.defaultBlendOut = clipSpec.defaultBlendOut;
                    clip.vatFrameStart = clipSpec.vatFrameStart;
                    clip.vatFrameCount = clipSpec.vatFrameCount;
                    clip.vatFps = clipSpec.vatFps;
                    clip.offsetBounds = clipSpec.offsetBounds;
                    BlobBuilderArray<TransformTrackBlob> trackArray =
                        builder.Allocate(ref clip.transformTracks, clipSpec.transformTracks.Length);
                    for (int trackIndex = 0; trackIndex < clipSpec.transformTracks.Length; trackIndex++)
                    {
                        TransformTrackSpec trackSpec = clipSpec.transformTracks[trackIndex];
                        trackArray[trackIndex].targetIndex = trackSpec.targetIndex;
                        trackArray[trackIndex].blendOp = trackSpec.blendOp;
                        trackArray[trackIndex].channels = trackSpec.channels;

                        BlobBuilderArray<TransformKeyBlob> keyArray =
                            builder.Allocate(ref trackArray[trackIndex].keys, trackSpec.keys.Length);
                        for (int keyIndex = 0; keyIndex < trackSpec.keys.Length; keyIndex++)
                        {
                            TransformKeySpec keySpec = trackSpec.keys[keyIndex];
                            keyArray[keyIndex] = new TransformKeyBlob
                            {
                                normalizedTime = keySpec.normalizedTime,
                                position = keySpec.position,
                                rotation = new float3(0f, 0f, keySpec.rotationZ),
                                scale = keySpec.scale,
                                interpolation = keySpec.interpolation
                            };
                        }
                    }

                    BlobBuilderArray<SpriteTrackBlob> spriteTrackArray =
                        builder.Allocate(ref clip.spriteTracks, clipSpec.spriteTracks.Length);
                    for (int spriteTrackIndex = 0; spriteTrackIndex < clipSpec.spriteTracks.Length; spriteTrackIndex++)
                    {
                        SpriteTrackSpec spriteTrackSpec = clipSpec.spriteTracks[spriteTrackIndex];
                        spriteTrackArray[spriteTrackIndex].targetIndex = spriteTrackSpec.targetIndex;
                        spriteTrackArray[spriteTrackIndex].mode = spriteTrackSpec.mode;
                        spriteTrackArray[spriteTrackIndex].sliceSpace = spriteTrackSpec.sliceSpace;

                        BlobBuilderArray<SpriteKeyBlob> spriteKeyArray = builder.Allocate(
                            ref spriteTrackArray[spriteTrackIndex].keys, spriteTrackSpec.keys.Length);
                        for (int spriteKeyIndex = 0; spriteKeyIndex < spriteTrackSpec.keys.Length; spriteKeyIndex++)
                        {
                            SpriteKeySpec spriteKeySpec = spriteTrackSpec.keys[spriteKeyIndex];
                            spriteKeyArray[spriteKeyIndex] = new SpriteKeyBlob
                            {
                                normalizedTime = spriteKeySpec.normalizedTime,
                                sliceIndex = spriteKeySpec.sliceIndex,
                                atlasRect = spriteKeySpec.atlasRect
                            };
                        }
                    }

                    BlobBuilderArray<EventMarkerBlob> eventArray =
                        builder.Allocate(ref clip.events, clipSpec.events.Length);
                    for (int eventIndex = 0; eventIndex < clipSpec.events.Length; eventIndex++)
                    {
                        EventSpec eventSpec = clipSpec.events[eventIndex];
                        eventArray[eventIndex] = new EventMarkerBlob
                        {
                            normalizedTime = eventSpec.normalizedTime,
                            eventKey = eventSpec.eventKey,
                            intParam = eventSpec.intParam,
                            floatParam = eventSpec.floatParam,
                            windowSeconds = eventSpec.windowSeconds
                        };
                    }

                    sortedClipIdArray[denseClipIndex] = clipSpec.clipId;
                }

                // Target ids are 1-based so that dense index i belongs to id i + 1, which keeps the
                // ids distinguishable from the indices in a failure message.
                BlobBuilderArray<uint> sortedTargetIdArray =
                    builder.Allocate(ref registryRoot.sortedTargetIds, targetCount);
                BlobBuilderArray<float3> targetBoundsExtentsArray =
                    builder.Allocate(ref registryRoot.targetBoundsExtents, targetCount);
                BlobBuilderArray<int> targetFramesPerVariantArray =
                    builder.Allocate(ref registryRoot.targetFramesPerVariant, targetCount);
                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    sortedTargetIdArray[targetIndex] = (uint)(targetIndex + 1);
                    targetBoundsExtentsArray[targetIndex] = new float3(0.5f, 0.5f, 0.5f);
                    targetFramesPerVariantArray[targetIndex] = framesPerVariant;
                }
                registryRoot.vatInfo = new VatTextureInfoBlob
                {
                    flavor = VatFlavor.BoneMatrix,
                    textureWidth = 0,
                    rowsPerFrame = 1,
                    boneOrVertexCount = 0
                };

                return builder.CreateBlobAssetReference<ClipRegistryBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }

        private static int CompareByClipId(ClipSpec firstSpec, ClipSpec secondSpec)
        {
            return firstSpec.clipId.CompareTo(secondSpec.clipId);
        }

        /// <summary>
        /// Creates an actor root shaped like a baked one: layers reset to "no clip", the command
        /// gate disabled, no events pending, and bounds already clean so that a test asserting a
        /// system dirtied them is asserting something.
        /// </summary>
        internal static Entity CreateActor(
            World world,
            BlobAssetReference<ClipRegistryBlob> registry,
            int layerCount = 2)
        {
            EntityManager entityManager = world.EntityManager;
            Entity actorEntity = entityManager.CreateEntity();

            entityManager.AddComponentData(actorEntity, new ClipRegistry { Value = registry });

            DynamicBuffer<PlaybackLayer> layers = entityManager.AddBuffer<PlaybackLayer>(actorEntity);
            layers.ResizeUninitialized(layerCount);
            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                layers[layerIndex] = NewLayer();
            }

            entityManager.AddBuffer<AnimationCommand>(actorEntity);
            entityManager.AddComponent<AnimationCommandPending>(actorEntity);
            entityManager.SetComponentEnabled<AnimationCommandPending>(actorEntity, false);

            entityManager.AddBuffer<AnimEventOutput>(actorEntity);
            entityManager.AddComponent<AnimEventsPending>(actorEntity);
            entityManager.SetComponentEnabled<AnimEventsPending>(actorEntity, false);

            entityManager.AddComponentData(actorEntity, new AnimEventMask { bits = 0UL });
            entityManager.SetComponentEnabled<AnimEventMask>(actorEntity, false);

            entityManager.AddComponent<BoundsDirty>(actorEntity);
            entityManager.SetComponentEnabled<BoundsDirty>(actorEntity, false);

            // The presentation half of the toolkit needs these three. They are added to every test
            // actor so one builder serves both halves; the C4.3/C4.4 systems ignore them.
            entityManager.AddBuffer<RigPartRef>(actorEntity);
            entityManager.AddComponentData(actorEntity, new SampleSettings { rateHz = 0f, phase01 = 0f });
            entityManager.AddComponent<AnimVisible>(actorEntity);
            entityManager.SetComponentEnabled<AnimVisible>(actorEntity, true);

            // Actor-space rest bounds, deliberately off-origin: a rig centred on the origin cannot
            // tell a correct actor-space union from one that mistook offset space for actor space,
            // which is the defect amendment A13 exists to prevent.
            entityManager.AddComponentData(actorEntity, new ActorRestBounds
            {
                value = new AABB { Center = new float3(2f, 3f, 0f), Extents = new float3(1f, 1.5f, 0.5f) }
            });
            entityManager.AddComponentData(actorEntity, new RenderBounds { Value = default });

            // LocalToWorld is what TransformUsageFlags.Dynamic gives a real baked actor root, and
            // AnimLodDistanceSystem measures against it. AnimSampleState carries the LOD-3 freeze
            // signature (A34). AnimLod is deliberately absent — it is opt-in (A23), so the default
            // fixture must exercise the "no AnimLod" path that most actors take.
            entityManager.AddComponentData(actorEntity, new LocalToWorld { Value = float4x4.identity });
            entityManager.AddComponentData(actorEntity, new AnimSampleState { sampledClipSignature = 0 });

            return actorEntity;
        }

        /// <summary>
        /// Creates a part entity bound to <paramref name="actorEntity"/> at
        /// <paramref name="targetIndex"/>, carrying everything the presentation systems read and
        /// write, and appends it to the actor's <see cref="RigPartRef"/> buffer.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The rest pose defaults to a <em>non-identity</em> one — offset from the origin, rotated,
        /// and non-uniformly scaled. A fixture built on an identity rest pose cannot tell "the
        /// system composited correctly" from "the system wrote the clip value and lost the rest
        /// pose", because at the origin with unit scale the two produce the same numbers. That is
        /// the shape of a test which passes under both the correct and the broken implementation.
        /// </para>
        /// <para>
        /// <c>PostTransformMatrix</c> is seeded from the rest scale, which is what
        /// <c>TransformUsageFlags.NonUniformScale</c> makes real transform baking do.
        /// </para>
        /// </remarks>
        internal static Entity AddPart(
            World world,
            Entity actorEntity,
            int targetIndex,
            float3 restPosition = default,
            float restRotationZ = 0f,
            float3 restScale = default,
            int restSliceIndex = 0,
            bool asFlipbookPlane = false,
            int vatDrivingLayerIndex = -1)
        {
            EntityManager entityManager = world.EntityManager;

            float3 resolvedRestScale = math.all(restScale == float3.zero)
                ? new float3(1f, 1f, 1f)
                : restScale;
            TargetRestPose restPose = new TargetRestPose
            {
                localPosition = restPosition,
                rotation = new float3(0f, 0f, restRotationZ),
                scale = resolvedRestScale,
                restSliceIndex = restSliceIndex
            };

            Entity partEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(partEntity, new RigPartBinding
            {
                actorRoot = actorEntity,
                targetIndex = targetIndex
            });
            entityManager.AddComponentData(partEntity, restPose);
            entityManager.AddComponentData(partEntity, new TargetPose
            {
                localPosition = restPose.localPosition,
                rotation = restPose.rotation,
                scale = restPose.scale,
                sliceIndex = restPose.restSliceIndex,
                atlasRect = ClipSampler.IdentityAtlasRect
            });
            entityManager.AddComponentData(partEntity, LocalTransform.FromPosition(restPose.localPosition));
            entityManager.AddComponentData(partEntity, new PostTransformMatrix
            {
                Value = float4x4.Scale(resolvedRestScale.x, resolvedRestScale.y, 1f)
            });
            entityManager.AddComponent<AnimVisible>(partEntity);
            entityManager.SetComponentEnabled<AnimVisible>(partEntity, true);

            if (asFlipbookPlane)
            {
                // RigTargetBaker adds these as a pair, seeded the same way, because which of the two
                // a clip drives is a per-track decision rather than a property of the part.
                entityManager.AddComponentData(
                    partEntity,
                    new SpriteSliceProperty { Value = restPose.restSliceIndex });
                entityManager.AddComponentData(
                    partEntity,
                    new AtlasFrameProperty { Value = ClipSampler.IdentityAtlasRect });
            }

            if (vatDrivingLayerIndex >= 0)
            {
                entityManager.AddComponentData(
                    partEntity,
                    new VatDriven { layerIndex = (byte)vatDrivingLayerIndex });
                entityManager.AddComponentData(partEntity, new VatFrameAProperty { Value = 0f });
                entityManager.AddComponentData(partEntity, new VatFrameBProperty { Value = 0f });
                entityManager.AddComponentData(partEntity, new VatBlendProperty { Value = 0f });
            }

            entityManager.AddComponentData(partEntity, new RenderBounds { Value = default });

            entityManager.GetBuffer<RigPartRef>(actorEntity).Add(new RigPartRef
            {
                part = partEntity,
                targetIndex = targetIndex
            });

            return partEntity;
        }

        /// <summary>The state the baker seeds an unplayed layer with.</summary>
        internal static PlaybackLayer NewLayer()
        {
            return new PlaybackLayer
            {
                clipIndex = -1,
                previousClipIndex = -1,
                speed = 1f,
                previousSpeed = 1f,
                loop = LoopMode.UseClipDefault,
                previousLoop = LoopMode.UseClipDefault,
                flags = PlaybackFlags.None
            };
        }

        /// <summary>Overwrites one layer's state and re-enables nothing — a direct state seed.</summary>
        internal static void SetLayer(World world, Entity actorEntity, int layerIndex, PlaybackLayer layer)
        {
            DynamicBuffer<PlaybackLayer> layers = world.EntityManager.GetBuffer<PlaybackLayer>(actorEntity);
            layers[layerIndex] = layer;
        }

        /// <summary>Reads one layer's settled state.</summary>
        internal static PlaybackLayer GetLayer(World world, Entity actorEntity, int layerIndex)
        {
            return world.EntityManager.GetBuffer<PlaybackLayer>(actorEntity)[layerIndex];
        }

        /// <summary>
        /// Appends a command and opens the gate — the same two-step pairing
        /// <c>AnimationCommandUtil</c> performs, done through the <c>EntityManager</c> because a
        /// test has no <c>EnabledRefRW</c> to hand it.
        /// </summary>
        internal static void EnqueueCommand(World world, Entity actorEntity, AnimationCommand command)
        {
            EntityManager entityManager = world.EntityManager;
            DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(actorEntity);
            commands.Add(command);
            entityManager.SetComponentEnabled<AnimationCommandPending>(actorEntity, true);
        }

        /// <summary>Builds a Play command element.</summary>
        internal static AnimationCommand PlayCommand(
            byte layerIndex,
            ulong clipId,
            float speed = 1f,
            LoopMode loop = LoopMode.UseClipDefault,
            float blendDuration = float.NaN)
        {
            return new AnimationCommand
            {
                kind = CommandKind.Play,
                layerIndex = layerIndex,
                clip = new ClipId(clipId),
                speed = speed,
                loop = loop,
                blendDuration = blendDuration,
                time = 0f
            };
        }

        /// <summary>Builds a Queue command element.</summary>
        internal static AnimationCommand QueueCommand(
            byte layerIndex,
            ulong clipId,
            float speed = 1f,
            LoopMode loop = LoopMode.UseClipDefault,
            float blendDuration = float.NaN)
        {
            return new AnimationCommand
            {
                kind = CommandKind.Queue,
                layerIndex = layerIndex,
                clip = new ClipId(clipId),
                speed = speed,
                loop = loop,
                blendDuration = blendDuration,
                time = 0f
            };
        }

        /// <summary>Builds a Stop command element.</summary>
        internal static AnimationCommand StopCommand(byte layerIndex, float blendDuration = float.NaN)
        {
            return new AnimationCommand
            {
                kind = CommandKind.Stop,
                layerIndex = layerIndex,
                clip = default,
                speed = 0f,
                loop = LoopMode.UseClipDefault,
                blendDuration = blendDuration,
                time = 0f
            };
        }
    }
}
