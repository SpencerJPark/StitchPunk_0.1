// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Entities;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Rebuilds every actor's <see cref="AnimEventMask"/> from where its layers currently stand
    /// (architecture section 5.5, amendment A45) — the sustained counterpart to
    /// <see cref="EventEmissionSystem"/>'s one-frame pulses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rebuilt from zero, never accumulated.</strong> The job ORs together the windows open
    /// on each active layer and assigns the result; it never reads the previous frame's bits. That
    /// is the whole interrupt story: a Play command swaps the layer's clip, this system reads the
    /// new clip's markers on the very next frame, and the interrupted swing's damage window closes
    /// without any command path having to know windows exist. A countdown-based design would have
    /// needed a cancel on every one of those paths.
    /// </para>
    /// <para>
    /// <strong>Runs after <see cref="EventEmissionSystem"/>.</strong> Not because it reads anything
    /// that system writes — it does not — but so the two channels of one marker land in a fixed
    /// order within the frame: the pulse first, the state second. A consumer reading both for the
    /// same marker therefore never sees the window open on a frame before the pulse it belongs to.
    /// </para>
    /// <para>
    /// <strong>Never gated on <see cref="AnimVisible"/></strong>, for the same reason event emission
    /// is not: a window is gameplay. An actor swinging behind the camera still connects.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Recomputes one actor's open-window mask across all of its playback layers.
    /// </summary>
    /// <remarks>
    /// <see cref="AnimEventMask"/> is declared <c>WithPresent</c> for the same reason
    /// <see cref="AnimEventsPending"/> is in <c>EmitAnimationEventsJob</c>: it is disabled on every
    /// actor holding no window, which is most of them on most frames, and an
    /// <c>EnabledRefRW&lt;T&gt;</c> parameter alone would enrol it as an enabled-only filter — so
    /// the job would only ever run for actors that already had a window open, and no actor's first
    /// window could ever be set.
    /// </remarks>
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

        /// <summary>
        /// The bits held open by one layer's current clip at that layer's current time.
        /// </summary>
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
                    // A pulse-only key that was nonetheless authored with a window. Validation
                    // rule V20 reports this at bake time; at runtime it simply has no bit to set.
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
