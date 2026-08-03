// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Recomputes an actor's render bounds when the set of clips it references changes
    /// (architecture section 5.8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Gated on the <see cref="BoundsDirty"/> enableable, never on a change filter.</strong>
    /// A change-version filter on <see cref="PlaybackLayer"/> is the obvious implementation and it
    /// cannot work: <c>PlaybackTimeSystem</c> writes <c>time</c> into that buffer every frame for
    /// every active actor, so the buffer's change version bumps every frame and the filter
    /// degenerates to always-true. The failure is invisible — the bounds stay correct, they are just
    /// recomputed for every actor forever.
    /// </para>
    /// <para>
    /// <strong>This system is the sole reset path.</strong> `CommandApplySystem` and
    /// `PlaybackTimeSystem` enable the tag; nothing else disables it. A frame that only advances
    /// time therefore leaves the tag disabled, this system's query empty, and every
    /// <see cref="RenderBounds"/> untouched.
    /// </para>
    /// <para>
    /// <strong>Offset space is not actor space (amendment A13).</strong>
    /// <see cref="ClipBlob.offsetBounds"/> is built from transform keys, which are offsets from a
    /// part's rest pose, so every box the clip bake produces is centred on the origin. Writing one
    /// into <see cref="RenderBounds"/> directly would give any rig whose parts sit away from the
    /// origin a box smaller than its own silhouette, and it would cull visibly. The actor-space
    /// answer is the Minkowski sum of the rest box and the offset box — centres add, extents add —
    /// which is exactly "every part could be anywhere in its rest box, displaced by anything in the
    /// offset box".
    /// </para>
    /// <para>
    /// Parts receive the actor's union rather than their own tightened box. Per-part tightening is
    /// an explicit non-goal (§5.8): it would need a per-part offset union the clip bake does not
    /// produce, to save culling precision on entities that are already inside the actor's box.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Unions one actor's referenced clip bounds into actor space and publishes them.
    /// </summary>
    /// <remarks>
    /// Each actor writes only its own root and the parts in its own <see cref="RigPartRef"/> buffer,
    /// and an entity belongs to exactly one actor — the same ownership argument that makes
    /// <c>RigBindingSystem</c>'s lookup sound.
    /// </remarks>
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
                // until the blend completes — which is one of the three moments PlaybackTimeSystem
                // re-dirties the tag.
                if ((layer.flags & PlaybackFlags.Blending) != 0)
                {
                    EncapsulateClipOffsets(
                        ref registry, layer.previousClipIndex, ref offsetMinimum, ref offsetMaximum);
                }
            }

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

        /// <summary>
        /// Grows an offset-space min/max by one clip's <see cref="ClipBlob.offsetBounds"/>.
        /// Unresolved clip indices contribute nothing.
        /// </summary>
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
