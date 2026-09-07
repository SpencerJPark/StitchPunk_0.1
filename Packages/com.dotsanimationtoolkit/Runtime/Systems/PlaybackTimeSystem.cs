// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs after <c>CommandApplySystem</c> in <see cref="AnimationToolkitLogicSystemGroup"/>:
    /// advances playback time, blends and queues for every actor, every frame. A queued clip
    /// promotes one frame after its predecessor finishes, not at the instant it finishes, so
    /// <c>EventEmissionSystem</c> (later this same group) still sees the finished clip in place.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitLogicSystemGroup))]
    [UpdateAfter(typeof(CommandApplySystem))]
    [BurstCompile]
    public partial struct PlaybackTimeSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlaybackLayer>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            AdvancePlaybackJob advanceJob = new AdvancePlaybackJob
            {
                deltaTime = SystemAPI.Time.DeltaTime
            };
            state.Dependency = advanceJob.ScheduleParallel(state.Dependency);
        }
    }

    // BoundsDirty is WithPresent: a plain EnabledRefRW<T> parameter would enrol it as an All
    // filter, so every actor would advance on its first frame, then freeze the moment the bounds
    // pass cleaned it, with no error anywhere.
    [BurstCompile]
    [WithPresent(typeof(BoundsDirty))]
    internal partial struct AdvancePlaybackJob : IJobEntity
    {
        public float deltaTime;

        // Per-layer stepping lives in PlaybackTimeMath.Advance, shared with the Actor Editor's
        // preview composer, so the two cannot silently disagree about when a clip finishes.
        private void Execute(
            ref DynamicBuffer<PlaybackLayer> layers,
            in ClipRegistry clipRegistry,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
        {
            BlobAssetReference<ClipRegistryBlob> registryReference = clipRegistry.Value;
            ref ClipRegistryBlob registry = ref registryReference.Value;

            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                ref PlaybackLayer layer = ref layers.ElementAt(layerIndex);
                PlaybackTimeMath.Advance(ref layer, ref registry, deltaTime, out bool boundsDirty);
                if (boundsDirty)
                {
                    boundsDirtyEnabled.ValueRW = true;
                }
            }
        }
    }
}
