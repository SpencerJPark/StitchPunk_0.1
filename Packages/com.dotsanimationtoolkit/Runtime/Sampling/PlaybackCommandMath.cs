// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// One layer's Play/Stop state change, with no dependency on entities or command buffers.
    /// <c>CommandApplySystem</c> and the Actor Editor's preview composer both call this, so a
    /// triggered animation and a played-in-game one always fold the same way.
    /// </summary>
    [BurstCompile]
    public static class PlaybackCommandMath
    {
        /// <summary>
        /// Starts <paramref name="clip"/> on the layer, demoting whatever was playing into the
        /// crossfade source. The queue slot is left alone. Always zeroes <see
        /// cref="PlaybackLayer.animationKey"/> — a named play re-stamps its own key afterward.
        /// </summary>
        /// <param name="blendDuration">Seconds; NaN resolves to the incoming clip's own default blend-in.</param>
        /// <returns>False when <paramref name="clip"/> does not resolve in <paramref name="registry"/>; the layer is left untouched.</returns>
        [BurstCompile]
        public static bool ApplyPlay(
            ref PlaybackLayer layer,
            ref ClipRegistryBlob registry,
            ClipId clip,
            float speed,
            LoopMode loop,
            float blendDuration,
            out bool boundsDirty)
        {
            boundsDirty = false;

            if (!ClipRegistryApi.TryResolveClip(ref registry, clip, out int incomingClipIndex))
            {
                return false;
            }

            ref ClipBlob incomingClip = ref registry.clips[incomingClipIndex];

            // NaN means "no opinion", which resolves to the clip's authored blend-in. 0 is a
            // different, reachable answer — a hard cut — so the two must never collapse together.
            float resolvedBlendDuration = math.isnan(blendDuration)
                ? math.max(incomingClip.defaultBlendIn, 0f)
                : math.max(blendDuration, 0f);

            int outgoingClipIndex = layer.clipIndex;
            bool isLayerActive = (layer.flags & PlaybackFlags.Active) != 0;
            bool hasOutgoingClip = isLayerActive && outgoingClipIndex >= 0;

            if (resolvedBlendDuration > 0f)
            {
                if (hasOutgoingClip)
                {
                    layer.previousClip = layer.clip;
                    layer.previousClipIndex = outgoingClipIndex;
                    layer.previousTime = layer.time;
                    layer.previousSpeed = layer.speed;
                    layer.previousLoop = layer.loop; // must run before layer.loop is overwritten below, or a crossfading Once clip wraps instead of holding
                }
                else if (!isLayerActive || layer.previousClipIndex < 0)
                {
                    // Nothing to fade from on this layer, so fade in from the pose the layers below
                    // composited. ClipSampler.CompositeLayers reads an empty previous slot as "lerp
                    // from the incoming pose", which is exactly a layer easing in over the ones beneath it.
                    ClearPreviousSlot(ref layer);
                }

                // A layer stopped with a fade keeps its outgoing clip in the previous slot, so a
                // Play arriving mid-fade crossfades out of it rather than dropping it — the branch
                // above deliberately leaves that case alone.
                layer.blendElapsed = 0f;
                layer.blendDuration = resolvedBlendDuration;
                layer.flags |= PlaybackFlags.Blending;
            }
            else
            {
                ClearBlendSource(ref layer);
            }

            layer.clip = clip;
            layer.clipIndex = incomingClipIndex;
            layer.speed = speed;
            layer.loop = loop;
            layer.animationKey = 0u;

            // Reverse playback starts at the end, or the first advance would immediately clamp a
            // Once clip and report it finished before a single frame of it was shown.
            layer.time = speed < 0f ? incomingClip.duration : 0f;
            layer.timeAtFrameStart = layer.time;

            layer.flags |= PlaybackFlags.Active;
            layer.flags &= ~(PlaybackFlags.Finished | PlaybackFlags.FinishedThisFrame);

            if (outgoingClipIndex != incomingClipIndex)
            {
                boundsDirty = true;
            }

            return true;
        }

        /// <summary>Stops the layer, either at once or by fading the current clip out to nothing. Clears the queue in both cases.</summary>
        /// <param name="blendDurationOverride">Seconds; NaN resolves to the outgoing clip's own default blend-out.</param>
        [BurstCompile]
        public static void ApplyStop(
            ref PlaybackLayer layer, ref ClipRegistryBlob registry, float blendDurationOverride, out bool boundsDirty)
        {
            boundsDirty = false;

            int outgoingClipIndex = layer.clipIndex;
            bool hasOutgoingClip = (layer.flags & PlaybackFlags.Active) != 0 && outgoingClipIndex >= 0;

            float fadeDuration;
            if (math.isnan(blendDurationOverride))
            {
                fadeDuration = hasOutgoingClip
                    ? math.max(registry.clips[outgoingClipIndex].defaultBlendOut, 0f)
                    : 0f;
            }
            else
            {
                fadeDuration = math.max(blendDurationOverride, 0f);
            }

            // A stop cancels what was going to happen next, in both branches. Leaving a queued clip
            // behind would arm the layer to restart on its own the next time anything finished.
            ClearQueue(ref layer);

            if (hasOutgoingClip && fadeDuration > 0f)
            {
                layer.previousClip = layer.clip;
                layer.previousClipIndex = outgoingClipIndex;
                layer.previousTime = layer.time;
                layer.previousSpeed = layer.speed;
                layer.previousLoop = layer.loop;
                layer.blendElapsed = 0f;
                layer.blendDuration = fadeDuration;

                // Still Active: the layer has no current clip but is fading one out, and the time
                // advance deactivates it when the fade completes.
                layer.flags |= PlaybackFlags.Active | PlaybackFlags.Blending;
                layer.flags &= ~(PlaybackFlags.Finished | PlaybackFlags.FinishedThisFrame);

                layer.clip = default;
                layer.clipIndex = -1;
                layer.time = 0f;
                layer.timeAtFrameStart = 0f;
                layer.speed = 0f;
                layer.loop = LoopMode.UseClipDefault;
            }
            else
            {
                ClearBlendSource(ref layer);
                layer.clip = default;
                layer.clipIndex = -1;
                layer.time = 0f;
                layer.timeAtFrameStart = 0f;
                layer.speed = 0f;
                layer.loop = LoopMode.UseClipDefault;
                layer.flags = PlaybackFlags.None;
                layer.animationKey = 0u; // the layer deactivates on the spot, so the key clears with it
            }

            if (outgoingClipIndex != layer.clipIndex)
            {
                boundsDirty = true;
            }
        }

        private static void ClearBlendSource(ref PlaybackLayer layer)
        {
            ClearPreviousSlot(ref layer);
            layer.blendElapsed = 0f;
            layer.blendDuration = 0f;
            layer.flags &= ~PlaybackFlags.Blending;
        }

        // An empty previous slot with a running blend is the "fade in from the layers below" state;
        // this clears the slot without touching the blend itself.
        private static void ClearPreviousSlot(ref PlaybackLayer layer)
        {
            layer.previousClip = default;
            layer.previousClipIndex = -1;
            layer.previousTime = 0f;
            layer.previousSpeed = 0f;
            layer.previousLoop = LoopMode.UseClipDefault;
        }

        private static void ClearQueue(ref PlaybackLayer layer)
        {
            layer.queuedClip = default;
            layer.queuedSpeed = 0f;
            layer.queuedLoop = LoopMode.UseClipDefault;
            layer.queuedBlend = 0f;
            layer.flags &= ~PlaybackFlags.HasQueued;
        }
    }
}
