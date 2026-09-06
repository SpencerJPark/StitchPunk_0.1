// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs after <c>TransformSampleSystem</c>: recomputes an actor's render bounds when the set of
    /// clips it references changes. Gated on the <see cref="BoundsDirty"/> enableable, never a
    /// change-version filter — <c>PlaybackTimeSystem</c> writes <c>time</c> into
    /// <see cref="PlaybackLayer"/> every frame, so a change filter there degenerates to always-true.
    /// This system is the sole path that disables the tag again.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(TransformSampleSystem))]
    [BurstCompile]
    public partial struct RenderBoundsUpdateSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BoundsDirty>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            UpdateActorBoundsJob boundsJob = new UpdateActorBoundsJob
            {
                renderBoundsLookup = SystemAPI.GetComponentLookup<RenderBounds>()
            };
            state.Dependency = boundsJob.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    [WithAll(typeof(BoundsDirty))]
    internal partial struct UpdateActorBoundsJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public ComponentLookup<RenderBounds> renderBoundsLookup;

        private void Execute(
            Entity actorEntity,
            in DynamicBuffer<PlaybackLayer> layers,
            in DynamicBuffer<RigPartRef> partRefs,
            in ClipRegistry clipRegistry,
            in ActorRestBounds actorRestBounds,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
        {
            BlobAssetReference<ClipRegistryBlob> registryReference = clipRegistry.Value;
            ref ClipRegistryBlob registry = ref registryReference.Value;

            // Starts empty rather than at the rest box: an actor referencing no clip at all should
            // fall back to its rest bounds exactly, which the zero-offset default below gives it.
            float3 offsetMinimum = float3.zero;
            float3 offsetMaximum = float3.zero;

            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                PlaybackLayer layer = layers[layerIndex];
                if ((layer.flags & PlaybackFlags.Active) == 0)
                {
                    continue;
                }

                EncapsulateClipOffsets(ref registry, layer.clipIndex, ref offsetMinimum, ref offsetMaximum);

                // A crossfading layer still shows the outgoing clip, so its box stays in the union
                // until the blend completes — which is one of the moments PlaybackTimeSystem re-dirties the tag.
                if ((layer.flags & PlaybackFlags.Blending) != 0)
                {
                    EncapsulateClipOffsets(
                        ref registry, layer.previousClipIndex, ref offsetMinimum, ref offsetMaximum);
                }
            }

            // Offset bounds are origin-centered, not actor space (transform keys are offsets from a
            // part's rest pose); the actor-space box is the Minkowski sum of the rest box and the
            // offset box — centres add, extents add.
            float3 offsetCentre = (offsetMaximum + offsetMinimum) * 0.5f;
            float3 offsetExtents = (offsetMaximum - offsetMinimum) * 0.5f;

            AABB actorBounds = new AABB
            {
                Center = actorRestBounds.value.Center + offsetCentre,
                Extents = actorRestBounds.value.Extents + offsetExtents
            };

            if (renderBoundsLookup.HasComponent(actorEntity))
            {
                renderBoundsLookup[actorEntity] = new RenderBounds { Value = actorBounds };
            }
            for (int partRefIndex = 0; partRefIndex < partRefs.Length; partRefIndex++)
            {
                Entity partEntity = partRefs[partRefIndex].part;
                if (renderBoundsLookup.HasComponent(partEntity))
                {
                    renderBoundsLookup[partEntity] = new RenderBounds { Value = actorBounds };
                }
            }

            // The sole reset path. Deleting this line leaves every actor permanently dirty, which
            // produces correct bounds at permanent cost — so a test must fail on it, not a profiler.
            boundsDirtyEnabled.ValueRW = false;
        }

        /// <summary>Grows an offset-space min/max by one clip's <see cref="ClipBlob.offsetBounds"/>. Unresolved clip indices contribute nothing.</summary>
        private static void EncapsulateClipOffsets(
            ref ClipRegistryBlob registry,
            int clipIndex,
            ref float3 offsetMinimum,
            ref float3 offsetMaximum)
        {
            if (clipIndex < 0 || clipIndex >= registry.clips.Length)
            {
                return;
            }

            AABB clipOffsetBounds = registry.clips[clipIndex].offsetBounds;
            offsetMinimum = math.min(offsetMinimum, clipOffsetBounds.Center - clipOffsetBounds.Extents);
            offsetMaximum = math.max(offsetMaximum, clipOffsetBounds.Center + clipOffsetBounds.Extents);
        }
    }
}
