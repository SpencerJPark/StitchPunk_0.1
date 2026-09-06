// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs after <c>EventEmissionSystem</c> in <see cref="AnimationToolkitLogicSystemGroup"/>:
    /// rebuilds every actor's <see cref="AnimEventMask"/> from where its layers currently stand.
    /// The ordering isn't a data dependency — it fixes which channel of a shared marker lands first
    /// within the frame, pulse then window.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitLogicSystemGroup))]
    [UpdateAfter(typeof(EventEmissionSystem))]
    [BurstCompile]
    public partial struct EventWindowSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlaybackLayer>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            RebuildEventWindowsJob rebuildJob = new RebuildEventWindowsJob();
            state.Dependency = rebuildJob.ScheduleParallel(state.Dependency);
        }
    }

    // AnimEventMask is WithPresent for the same reason AnimEventsPending is in
    // EmitAnimationEventsJob: it is disabled on most actors, and a plain EnabledRefRW<T> parameter
    // would enrol it as an enabled-only filter, so no actor's first window could ever be set.
    [BurstCompile]
    [WithPresent(typeof(AnimEventMask))]
    internal partial struct RebuildEventWindowsJob : IJobEntity
    {
        private void Execute(
            in DynamicBuffer<PlaybackLayer> layers,
            in ClipRegistry clipRegistry,
            ref AnimEventMask eventMask,
            EnabledRefRW<AnimEventMask> eventMaskEnabled)
        {
            BlobAssetReference<ClipRegistryBlob> registryReference = clipRegistry.Value;
            ref ClipRegistryBlob registry = ref registryReference.Value;

            ulong openBits = 0UL;

            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                PlaybackLayer layer = layers[layerIndex];

                // Unlike event emission, a layer that finished this frame is deliberately not
                // special-cased. A completion is an instant and belongs to the pulse channel; a
                // window is a position, and the position of a stopped layer holds nothing open.
                if ((layer.flags & PlaybackFlags.Active) == 0)
                {
                    continue;
                }

                openBits |= CollectLayerWindows(ref registry, layer);
            }

            eventMask.bits = openBits;
            eventMaskEnabled.ValueRW = openBits != 0UL;
        }

        private static ulong CollectLayerWindows(ref ClipRegistryBlob registry, in PlaybackLayer layer)
        {
            if (layer.clipIndex < 0 || layer.clipIndex >= registry.clips.Length)
            {
                return 0UL;
            }

            ref ClipBlob clip = ref registry.clips[layer.clipIndex];
            if (clip.events.Length == 0)
            {
                return 0UL;
            }

            LoopMode resolvedLoopMode = ClipSampler.ResolveLoopMode(layer.loop, clip.defaultLoop);
            bool isReverse = layer.speed < 0f;
            ulong layerBits = 0UL;

            for (int eventIndex = 0; eventIndex < clip.events.Length; eventIndex++)
            {
                ref EventMarkerBlob marker = ref clip.events[eventIndex];
                if (marker.windowSeconds <= 0f)
                {
                    continue;
                }

                ulong markerBit = AnimEventMaskKeys.BitOf(marker.eventKey);
                if (markerBit == 0UL)
                {
                    // A pulse-only key that was nonetheless authored with a window; at runtime it
                    // simply has no bit to set.
                    continue;
                }

                // Two markers may share a key — a repeated hit frame in one clip is the ordinary
                // case. Once one of them has the bit open the others cannot change the answer, so
                // this skips their window math rather than ORing a set bit onto itself.
                if ((layerBits & markerBit) != 0UL)
                {
                    continue;
                }

                if (EventWindowMath.IsWindowOpen(
                        marker.normalizedTime,
                        marker.windowSeconds,
                        layer.time,
                        clip.duration,
                        resolvedLoopMode,
                        isReverse))
                {
                    layerBits |= markerBit;
                }
            }

            return layerBits;
        }
    }
}
