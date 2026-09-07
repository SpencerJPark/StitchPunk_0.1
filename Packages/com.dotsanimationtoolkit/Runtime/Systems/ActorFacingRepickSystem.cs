// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Re-picks a directional entry's clip when <see cref="ActorFacing.facing"/> changes: swaps the
    /// layer's clip in place, no crossfade — the same hard-cut re-pick <c>CutsceneTimelineSystem</c>
    /// does for a direction variant.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitLogicSystemGroup))]
    [UpdateAfter(typeof(CommandApplySystem))]
    [UpdateBefore(typeof(PlaybackTimeSystem))]
    [BurstCompile]
    public partial struct ActorFacingRepickSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorFacing>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            RepickFacingJob repickJob = new RepickFacingJob();
            state.Dependency = repickJob.ScheduleParallel(state.Dependency);
        }
    }

    // BoundsDirty is WithPresent, not WithAll: an EnabledRefRW<T> parameter defaults T into an All
    // query filter, which would silently restrict this job to actors whose bounds already happened
    // to be dirty — CommandApplySystem's ApplyAnimationCommandsJob documents the same trap.
    [BurstCompile]
    [WithPresent(typeof(BoundsDirty))]
    internal partial struct RepickFacingJob : IJobEntity
    {
        private void Execute(
            ref ActorFacing actorFacing,
            in ActorProfile actorProfile,
            in ClipRegistry clipRegistry,
            ref DynamicBuffer<PlaybackLayer> layers,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
        {
            if (actorFacing.facing == actorFacing.appliedFacing)
            {
                return;
            }

            // A hand-built or profile-less test actor carries an uncreated reference; the same
            // guard CommandApplySystem uses before dereferencing one.
            BlobAssetReference<ActorProfileBlob> profileReference = actorProfile.Value;
            if (!profileReference.IsCreated)
            {
                return;
            }

            ref ActorProfileBlob profileBlob = ref profileReference.Value;
            BlobAssetReference<ClipRegistryBlob> registryReference = clipRegistry.Value;
            ref ClipRegistryBlob registry = ref registryReference.Value;

            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                ref PlaybackLayer layer = ref layers.ElementAt(layerIndex);

                // A raw Play (no named entry) has nothing to re-pick against, and an inactive layer
                // has no clip on screen worth swapping.
                if (layer.animationKey == 0u || (layer.flags & PlaybackFlags.Active) == 0)
                {
                    continue;
                }

                if (!ActorProfileApi.TryFindAnimation(ref profileBlob, layer.animationKey, out int animationIndex))
                {
                    continue;
                }

                ref ActorAnimationBlob entry = ref profileBlob.animations[animationIndex];
                if (!entry.hasDirections)
                {
                    continue;
                }

                if (!ActorProfileApi.TryResolve(
                        ref profileBlob,
                        layer.animationKey,
                        actorFacing.facing,
                        out byte _,
                        out ClipId resolvedClip,
                        out int _))
                {
                    continue;
                }

                // A Two-coverage entry on a Six-turning actor folds every facing onto the same slot
                // it was already playing, so this compare is what keeps that case a no-op.
                if (resolvedClip == layer.clip)
                {
                    continue;
                }

                if (!ClipRegistryApi.TryResolveClip(ref registry, resolvedClip, out int resolvedClipIndex))
                {
                    continue;
                }

                // In-place swap only: time, speed, loop, flags, and the blend slots are left exactly
                // as they were, so this never starts a crossfade of its own.
                layer.clip = resolvedClip;
                layer.clipIndex = resolvedClipIndex;
                boundsDirtyEnabled.ValueRW = true;
            }

            actorFacing.appliedFacing = actorFacing.facing;
        }
    }
}
