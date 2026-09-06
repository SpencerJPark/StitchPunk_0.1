// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Turns every billboard root to face the viewer and publishes the frame it resolved. Runs
    /// after <c>TransformApplySystem</c> in <see cref="AnimationToolkitPresentationSystemGroup"/>,
    /// so the animated pose is the billboard's rest orientation, not the other way around.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(TransformApplySystem))]
    [BurstCompile]
    public partial struct BillboardResolveSystem : ISystem
    {
        private ComponentLookup<LocalTransform> localTransformLookup;
        private ComponentLookup<Parent> parentLookup;

        /// <inheritdoc />
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BillboardRootElement>();

            // Without a camera there is nothing to face. The host writes this singleton; the package
            // never reads a Camera, because it cannot know which of a host's cameras matters.
            state.RequireForUpdate<AnimationToolkitCameraData>();

            localTransformLookup = state.GetComponentLookup<LocalTransform>();
            parentLookup = state.GetComponentLookup<Parent>(true);
        }

        /// <inheritdoc />
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            AnimationToolkitCameraData cameraData = SystemAPI.GetSingleton<AnimationToolkitCameraData>();

            localTransformLookup.Update(ref state);
            parentLookup.Update(ref state);

            ResolveBillboardRootsJob resolveJob = new ResolveBillboardRootsJob
            {
                cameraPosition = cameraData.position,
                cameraForward = cameraData.forward,
                localTransformLookup = localTransformLookup,
                parentLookup = parentLookup
            };
            state.Dependency = resolveJob.ScheduleParallel(state.Dependency);
        }
    }

    // Parallel across actors is safe because every entity touched belongs to the actor being
    // processed (a billboard root is a node of that actor's own prefab hierarchy, never shared) —
    // NativeDisableParallelForRestriction asserts exactly that.
    [BurstCompile]
    [WithAll(typeof(AnimVisible))]
    internal partial struct ResolveBillboardRootsJob : IJobEntity
    {
        public float3 cameraPosition;
        public float3 cameraForward;

        [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> localTransformLookup;

        [ReadOnly] public ComponentLookup<Parent> parentLookup;

        private void Execute(
            ref DynamicBuffer<BillboardRootElement> rootElements,
            in DynamicBuffer<PlaybackLayer> layers,
            in ClipRegistry clipRegistry)
        {
            for (int rootIndex = 0; rootIndex < rootElements.Length; rootIndex++)
            {
                BillboardRootElement rootElement = rootElements[rootIndex];
                if (!localTransformLookup.HasComponent(rootElement.node))
                {
                    continue;
                }

                // The authored settings are the rest state; a clip's billboard track keys on top of
                // them rather than replacing them, so a rig that sits permanently three-quarters-on
                // can still be animated off that rest.
                BillboardSettings settings = rootElement.settings;
                ApplyKeyedChannels(ref settings, rootElement.rootId, in layers, in clipRegistry);

                LocalTransform nodeWorld = ComputeWorldTransform(rootElement.node);

                quaternion resolvedRotation;
                bool resolved = BillboardMath.TryResolve(
                    settings,
                    nodeWorld.Position,
                    cameraPosition,
                    cameraForward,
                    nodeWorld.Rotation,
                    out resolvedRotation);

                // Published either way. A consumer reading the frame must never have to ask whether
                // the value it just read is real or a leftover from the last frame that succeeded.
                rootElement.resolvedRotation = resolvedRotation;
                rootElements[rootIndex] = rootElement;

                if (!resolved)
                {
                    continue;
                }

                // The world result is converted back into the node's own parent space, which is what
                // cancels an outer billboard root's rotation before this one applies its own.
                quaternion parentWorldRotation = ComputeParentWorldRotation(rootElement.node);
                RefRW<LocalTransform> nodeTransform = localTransformLookup.GetRefRW(rootElement.node);
                nodeTransform.ValueRW.Rotation =
                    math.mul(math.inverse(parentWorldRotation), resolvedRotation);
            }
        }

        /// <summary>
        /// Folds a clip's keyed billboard channels into a root's authored settings. Highest active
        /// layer carrying a track for this root wins; the angle offset adds to the authored one
        /// (a displacement), while blend weight and the enable flag replace it (absolute statements).
        /// </summary>
        private void ApplyKeyedChannels(
            ref BillboardSettings settings,
            uint rootId,
            in DynamicBuffer<PlaybackLayer> layers,
            in ClipRegistry clipRegistry)
        {
            if (!clipRegistry.Value.IsCreated)
            {
                return;
            }
            ref ClipRegistryBlob registry = ref clipRegistry.Value.Value;

            for (int layerIndex = layers.Length - 1; layerIndex >= 0; layerIndex--)
            {
                PlaybackLayer layer = layers[layerIndex];
                if ((layer.flags & PlaybackFlags.Active) == 0)
                {
                    continue;
                }
                if (layer.clipIndex < 0 || layer.clipIndex >= registry.clips.Length)
                {
                    continue;
                }

                ref ClipBlob clip = ref registry.clips[layer.clipIndex];
                int trackIndex = FindBillboardTrack(ref clip, rootId);
                if (trackIndex < 0)
                {
                    continue;
                }

                LoopMode resolvedLoop =
                    layer.loop == LoopMode.UseClipDefault ? clip.defaultLoop : layer.loop;
                float normalizedTime =
                    ClipSampler.MapTimeNormalized(layer.time, clip.duration, resolvedLoop);

                float keyedAngleOffset;
                float keyedBlendWeight;
                bool keyedEnabled;
                ClipSampler.SampleBillboardTrack(
                    ref clip.billboardTracks[trackIndex],
                    normalizedTime,
                    out keyedAngleOffset,
                    out keyedBlendWeight,
                    out keyedEnabled);

                settings.angleOffsetRadians += keyedAngleOffset;
                settings.blendWeight = keyedBlendWeight;
                settings.enabled = settings.enabled && keyedEnabled;
                return;
            }
        }

        // Linear over an array a clip almost always leaves empty; a binary search would be correct
        // and not worth the readability cost at these sizes.
        private static int FindBillboardTrack(ref ClipBlob clip, uint rootId)
        {
            for (int trackIndex = 0; trackIndex < clip.billboardTracks.Length; trackIndex++)
            {
                if (clip.billboardTracks[trackIndex].rootId == rootId)
                {
                    return trackIndex;
                }
            }
            return -1;
        }

        // Live LocalTransform, not LocalToWorld: the pose written moments ago in this same group
        // hasn't reached LocalToWorld yet, and an ancestor billboard root's freshly written rotation
        // must be visible here — that visibility is what stops a nested root turning twice.
        private LocalTransform ComputeWorldTransform(Entity node)
        {
            LocalTransform accumulated = localTransformLookup[node];
            Entity walker = node;

            // Bounded by the rig's depth, which is small. A cycle is impossible: Entities' parent
            // hierarchy is a tree by construction.
            while (parentLookup.HasComponent(walker))
            {
                Entity parent = parentLookup[walker].Value;
                if (!localTransformLookup.HasComponent(parent))
                {
                    break;
                }

                LocalTransform parentTransform = localTransformLookup[parent];
                accumulated = new LocalTransform
                {
                    Position = parentTransform.Position
                        + math.mul(parentTransform.Rotation, accumulated.Position * parentTransform.Scale),
                    Rotation = math.mul(parentTransform.Rotation, accumulated.Rotation),
                    Scale = parentTransform.Scale * accumulated.Scale
                };
                walker = parent;
            }
            return accumulated;
        }

        private quaternion ComputeParentWorldRotation(Entity node)
        {
            if (!parentLookup.HasComponent(node))
            {
                return quaternion.identity;
            }
            Entity parent = parentLookup[node].Value;
            if (!localTransformLookup.HasComponent(parent))
            {
                return quaternion.identity;
            }
            return ComputeWorldTransform(parent).Rotation;
        }
    }
}
