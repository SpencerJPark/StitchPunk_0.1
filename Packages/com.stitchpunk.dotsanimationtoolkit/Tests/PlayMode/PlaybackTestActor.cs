// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
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
        }

        /// <summary>Authoring-side description of one event marker.</summary>
        internal struct EventSpec
        {
            internal float normalizedTime;
            internal uint eventKey;
            internal int intParam;
            internal float floatParam;
        }

        /// <summary>Builds a marker at <paramref name="normalizedTime"/> carrying a user event key.</summary>
        internal static EventSpec Marker(float normalizedTime, uint eventKey, int intParam = 0, float floatParam = 0f)
        {
            return new EventSpec
            {
                normalizedTime = normalizedTime,
                eventKey = eventKey,
                intParam = intParam,
                floatParam = floatParam
            };
        }

        /// <summary>
        /// Builds a registry in the canonical layout the baker produces: clips sorted by ascending
        /// id, with <c>sortedClipIds</c> holding those ids in the same positions, so a clip's dense
        /// index is its position in ascending-id order — what
        /// <see cref="ClipRegistryUtil.TryResolveClip"/> returns.
        /// </summary>
        /// <remarks>The returned reference is caller-owned; the fixture must dispose it.</remarks>
        internal static BlobAssetReference<ClipRegistryBlob> BuildRegistry(ClipSpec[] clipSpecs, byte layerCount = 4)
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
                    clip.vatFrameStart = -1;
                    clip.vatFrameCount = 0;
                    clip.vatFps = 0f;
                    clip.offsetBounds = new AABB { Center = float3.zero, Extents = float3.zero };
                    builder.Allocate(ref clip.transformTracks, 0);
                    builder.Allocate(ref clip.spriteTracks, 0);

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
                            floatParam = eventSpec.floatParam
                        };
                    }

                    sortedClipIdArray[denseClipIndex] = clipSpec.clipId;
                }

                builder.Allocate(ref registryRoot.sortedTargetIds, 0);
                builder.Allocate(ref registryRoot.targetBoundsExtents, 0);
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

            entityManager.AddComponent<BoundsDirty>(actorEntity);
            entityManager.SetComponentEnabled<BoundsDirty>(actorEntity, false);

            return actorEntity;
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
