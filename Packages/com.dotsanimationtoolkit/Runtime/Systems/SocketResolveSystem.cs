// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs after <c>TransformApplySystem</c>: places every socket-attached entity on the socket it
    /// follows. Composes the world transform from the actor's <c>LocalToWorld</c> and the part's
    /// freshly written <c>LocalTransform</c>, rather than reading the part's own <c>LocalToWorld</c>
    /// — Unity's transform systems run after this group, so that would still be last frame's.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(TransformApplySystem))]
    [BurstCompile]
    public partial struct SocketResolveSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SocketAttachment>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ResolveSocketsJob resolveJob = new ResolveSocketsJob
            {
                socketRegistryLookup = SystemAPI.GetComponentLookup<SocketRegistry>(true),
                clipRegistryLookup = SystemAPI.GetComponentLookup<ClipRegistry>(true),
                localToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true),
                localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
                partRefLookup = SystemAPI.GetBufferLookup<RigPartRef>(true),
                playbackLayerLookup = SystemAPI.GetBufferLookup<PlaybackLayer>(true)
            };
            state.Dependency = resolveJob.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    [WithNone(typeof(RigPartBinding))]
    internal partial struct ResolveSocketsJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<SocketRegistry> socketRegistryLookup;
        [ReadOnly] public ComponentLookup<ClipRegistry> clipRegistryLookup;
        [ReadOnly] public ComponentLookup<LocalToWorld> localToWorldLookup;

        // NativeDisableContainerSafetyRestriction is required, not just convenient: LocalTransform
        // appears both written (via the ref parameter, for the attachment entity) and read (via this
        // lookup, for the part being followed), which Unity's job safety system otherwise rejects as
        // aliasing (InvalidOperationException on every update). Disjointness is enforced by
        // [WithNone(typeof(RigPartBinding))] above, which excludes every part entity from this query,
        // so an entity can never be both the one written and one read.
        [ReadOnly] [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> localTransformLookup;
        [ReadOnly] public BufferLookup<RigPartRef> partRefLookup;
        [ReadOnly] public BufferLookup<PlaybackLayer> playbackLayerLookup;

        private void Execute(in SocketAttachment attachment, ref LocalTransform localTransform)
        {
            Entity actorEntity = attachment.actorRoot;
            if (actorEntity == Entity.Null
                || !socketRegistryLookup.HasComponent(actorEntity)
                || !localToWorldLookup.HasComponent(actorEntity))
            {
                return;
            }

            BlobAssetReference<SocketRegistryBlob> registryReference = socketRegistryLookup[actorEntity].Value;
            if (!registryReference.IsCreated)
            {
                return;
            }

            ref SocketRegistryBlob registry = ref registryReference.Value;
            int socketIndex = ResolveSocketIndex(ref registry, attachment.socketId);
            if (socketIndex < 0)
            {
                return;
            }

            ref SocketDefinitionBlob socket = ref registry.sockets[socketIndex];
            LocalToWorld actorLocalToWorld = localToWorldLookup[actorEntity];

            float3 socketLocalPosition = float3.zero;
            quaternion socketLocalRotation = quaternion.identity;

            // Bone sockets never inherit scale: the bone lives in a texture, and a VAT bake stores
            // the deformed result rather than a scale channel to read one from.
            float socketLocalScale = 1f;

            bool resolved;
            if (socket.mode == SocketAttachMode.RigTarget)
            {
                resolved = TryResolvePartLocal(
                    actorEntity, socket.targetIndex,
                    out socketLocalPosition, out socketLocalRotation, out socketLocalScale);
            }
            else
            {
                resolved = TryResolveBoneLocal(
                    actorEntity, ref registry, socketIndex, socket.layerIndex,
                    out socketLocalPosition, out socketLocalRotation);
            }

            if (!resolved)
            {
                return;
            }

            // Socket-space offsets compose before the actor matrix, so an offset stays put relative
            // to the hand as the hand rotates — which is the whole point of expressing it locally.
            float3 offsetInSocket = socket.localPosition + attachment.localOffset;
            float3 actorSpacePosition = socketLocalPosition + math.mul(socketLocalRotation, offsetInSocket);
            quaternion actorSpaceRotation = math.mul(socketLocalRotation, socket.localRotation);

            // LocalToWorld.Rotation orthonormalizes the basis for us, so a non-uniformly scaled
            // actor still yields a pure rotation rather than one that shears the attachment.
            float3 worldPosition = math.transform(actorLocalToWorld.Value, actorSpacePosition);
            quaternion worldRotation = math.mul(actorLocalToWorld.Rotation, actorSpaceRotation);

            localTransform.Position = worldPosition;
            localTransform.Rotation = worldRotation;
            localTransform.Scale = socketLocalScale;
        }

        private static int ResolveSocketIndex(ref SocketRegistryBlob registry, uint socketId)
        {
            int low = 0;
            int high = registry.sortedSocketIds.Length - 1;
            while (low <= high)
            {
                int middle = (low + high) >> 1;
                uint candidate = registry.sortedSocketIds[middle];
                if (candidate == socketId)
                {
                    return middle;
                }
                if (candidate < socketId)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return -1;
        }

        private bool TryResolvePartLocal(
            Entity actorEntity,
            int targetIndex,
            out float3 position,
            out quaternion rotation,
            out float scale)
        {
            position = float3.zero;
            rotation = quaternion.identity;
            scale = 1f;

            if (targetIndex < 0 || !partRefLookup.HasBuffer(actorEntity))
            {
                return false;
            }

            DynamicBuffer<RigPartRef> partRefs = partRefLookup[actorEntity];
            for (int partIndex = 0; partIndex < partRefs.Length; partIndex++)
            {
                if (partRefs[partIndex].targetIndex != targetIndex)
                {
                    continue;
                }
                Entity partEntity = partRefs[partIndex].part;
                if (!localTransformLookup.HasComponent(partEntity))
                {
                    return false;
                }
                LocalTransform partTransform = localTransformLookup[partEntity];
                position = partTransform.Position;
                rotation = partTransform.Rotation;
                scale = partTransform.Scale;
                return true;
            }
            return false;
        }

        /// <summary>Samples a bone socket's baked track at the driving layer's current time, using the same time-to-frame mapping <c>VatMaterialSystem</c> uses so an attachment never disagrees with the mesh it rides on.</summary>
        private bool TryResolveBoneLocal(
            Entity actorEntity,
            ref SocketRegistryBlob registry,
            int socketIndex,
            byte layerIndex,
            out float3 position,
            out quaternion rotation)
        {
            position = float3.zero;
            rotation = quaternion.identity;

            if (!playbackLayerLookup.HasBuffer(actorEntity) || !clipRegistryLookup.HasComponent(actorEntity))
            {
                return false;
            }

            DynamicBuffer<PlaybackLayer> layers = playbackLayerLookup[actorEntity];
            if (layerIndex >= layers.Length)
            {
                return false;
            }

            PlaybackLayer layer = layers[layerIndex];
            if ((layer.flags & PlaybackFlags.Active) == 0)
            {
                return false;
            }

            BlobAssetReference<ClipRegistryBlob> clipRegistryReference = clipRegistryLookup[actorEntity].Value;
            ref ClipRegistryBlob clipRegistry = ref clipRegistryReference.Value;
            if (layer.clipIndex < 0 || layer.clipIndex >= clipRegistry.clips.Length)
            {
                return false;
            }

            ref ClipBlob clip = ref clipRegistry.clips[layer.clipIndex];
            int trackIndex = ResolveClipTrackIndex(ref registry, clip.clipId, socketIndex);
            if (trackIndex < 0)
            {
                return false;
            }

            ref SocketClipTrackBlob track = ref registry.clipTracks[trackIndex];
            if (track.samples.Length == 0)
            {
                return false;
            }

            LoopMode resolvedLoopMode = ClipSampler.ResolveLoopMode(layer.loop, clip.defaultLoop);
            float mappedTime = ClipSampler.MapTime(layer.time, clip.duration, resolvedLoopMode);
            float exactSample = math.clamp(mappedTime * track.fps, 0f, track.samples.Length - 1);

            // Interpolated rather than snapped: an attachment that stepped between baked frames
            // would visibly stutter against a mesh the shader is lerping smoothly.
            int lowerIndex = (int)math.floor(exactSample);
            int upperIndex = math.min(lowerIndex + 1, track.samples.Length - 1);
            float fraction = exactSample - lowerIndex;

            ref SocketSampleBlob lowerSample = ref track.samples[lowerIndex];
            ref SocketSampleBlob upperSample = ref track.samples[upperIndex];
            position = math.lerp(lowerSample.position, upperSample.position, fraction);
            rotation = math.slerp(lowerSample.rotation, upperSample.rotation, fraction);
            return true;
        }

        private static int ResolveClipTrackIndex(ref SocketRegistryBlob registry, ulong clipId, int socketIndex)
        {
            for (int trackIndex = 0; trackIndex < registry.clipTracks.Length; trackIndex++)
            {
                ref SocketClipTrackBlob track = ref registry.clipTracks[trackIndex];
                if (track.clipId == clipId && track.socketIndex == socketIndex)
                {
                    return trackIndex;
                }
            }
            return -1;
        }
    }
}
