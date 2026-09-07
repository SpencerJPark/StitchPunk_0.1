// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// One playback layer's per-frame time advance: queue promotion, blend progression, and
    /// loop/Once time mapping. <c>PlaybackTimeSystem</c>'s job and the Actor Editor's preview
    /// composer both call this, so the two cannot silently disagree about when a clip finishes.
    /// </summary>
    [BurstCompile]
    public static class PlaybackTimeMath
    {
        /// <summary>
        /// Advances one layer exactly as <c>PlaybackTimeSystem</c>'s job does: clears the one-frame
        /// <see cref="PlaybackFlags.FinishedThisFrame"/> pulse, promotes a queued clip left over from
        /// last frame, advances any running blend, then advances the current clip.
        /// </summary>
        /// <param name="boundsDirty">True if this layer's referenced clip set changed and its render bounds need re-union.</param>
        [BurstCompile]
        public static void Advance(
            ref PlaybackLayer layer, ref ClipRegistryBlob registry, float deltaTime, out bool boundsDirty)
        {
            boundsDirty = false;

            // Cleared for every layer, including inactive ones, before anything can set it again: a
            // Once clip finishes and deactivates in the same frame, so this is the only place left
            // that ever un-latches its pulse.
            layer.flags &= ~PlaybackFlags.FinishedThisFrame;

            if ((layer.flags & PlaybackFlags.Active) == 0)
            {
                return;
            }

            // A clip that finished last frame with something queued behind it is promoted here, at
            // the start of the advance — deliberately one frame after the completion, so a later
            // event pass still sees the finished clip in place for one whole frame.
            if ((layer.flags & PlaybackFlags.Finished) != 0 && (layer.flags & PlaybackFlags.HasQueued) != 0)
            {
                PromoteQueuedClip(ref layer, ref registry, ref boundsDirty);
            }

            if ((layer.flags & PlaybackFlags.Blending) != 0)
            {
                AdvanceBlend(ref layer, deltaTime, ref boundsDirty);
                if ((layer.flags & PlaybackFlags.Active) == 0)
                {
                    // A Stop fade ran to completion and took the layer with it.
                    return;
                }
            }

            if (layer.clipIndex < 0 || layer.clipIndex >= registry.clips.Length)
            {
                return;
            }

            AdvanceCurrentClip(ref layer, ref registry, deltaTime, ref boundsDirty);
        }

        /// <summary>Advances the crossfade: the outgoing clip keeps running on its own speed and loop mode while the blend weight climbs, and the source slot is released when it reaches 1.</summary>
        [BurstCompile]
        public static void AdvanceBlend(ref PlaybackLayer layer, float deltaTime, ref bool boundsDirty)
        {
            layer.previousTime += deltaTime * layer.previousSpeed;
            layer.blendElapsed += deltaTime;

            if (layer.blendElapsed < layer.blendDuration)
            {
                return;
            }

            bool wasFadingOutToNothing = layer.clipIndex < 0;

            layer.flags &= ~PlaybackFlags.Blending;
            layer.blendElapsed = 0f;
            layer.blendDuration = 0f;
            layer.previousClip = default;
            layer.previousClipIndex = -1;
            layer.previousTime = 0f;
            layer.previousSpeed = 0f;
            layer.previousLoop = LoopMode.UseClipDefault;

            boundsDirty = true; // the layer now references one clip fewer, so the bounds union shrinks

            if (wasFadingOutToNothing)
            {
                layer.flags = PlaybackFlags.None;
                layer.time = 0f;
                layer.timeAtFrameStart = 0f;
                layer.speed = 0f;
                layer.loop = LoopMode.UseClipDefault;
                layer.animationKey = 0u; // the Stop fade just finished deactivating the layer
            }
        }

        /// <summary>Advances the current clip's time and applies its loop mode.</summary>
        [BurstCompile]
        public static void AdvanceCurrentClip(
            ref PlaybackLayer layer, ref ClipRegistryBlob registry, float deltaTime, ref bool boundsDirty)
        {
            ref ClipBlob clip = ref registry.clips[layer.clipIndex];
            LoopMode resolvedLoopMode = ClipSampler.ResolveLoopMode(layer.loop, clip.defaultLoop);

            layer.timeAtFrameStart = layer.time;
            layer.time += deltaTime * layer.speed;

            // Loop and PingPong never finish: their stored time keeps climbing and the sampler folds
            // it. Only Once has an end to reach. Time is never wrapped here, only at sampling time,
            // or EventWrapMath.CollectCrossings would lose the lap count it needs to fire markers on
            // a frame long enough to cross the loop point more than once.
            if (resolvedLoopMode != LoopMode.Once)
            {
                return;
            }

            bool playingForward = layer.speed >= 0f;
            bool reachedEnd = playingForward ? layer.time >= clip.duration : layer.time <= 0f;
            if (!reachedEnd)
            {
                return;
            }

            layer.time = playingForward ? clip.duration : 0f;
            layer.flags |= PlaybackFlags.Finished | PlaybackFlags.FinishedThisFrame;

            // A queued follow-up is promoted at the top of the NEXT advance, not here — see
            // PromoteQueuedClip. The layer stays active and holds its final pose until then, which
            // is what a Once clip does anyway.
            if ((layer.flags & PlaybackFlags.HasQueued) != 0)
            {
                return;
            }

            layer.flags &= ~PlaybackFlags.Active;
            layer.animationKey = 0u;
            boundsDirty = true;
        }

        /// <summary>
        /// Promotes the one-deep queue slot into the current slot, crossfading from the pose the
        /// finished clip ended on. Deferred to the top of the next advance rather than run at the
        /// instant of completion: promoting immediately would replace the layer's clip fields before
        /// a later event pass runs in the same group, so that pass would name the wrong clip on
        /// completion and silently drop every marker in the finishing clip's last segment.
        /// </summary>
        [BurstCompile]
        public static void PromoteQueuedClip(ref PlaybackLayer layer, ref ClipRegistryBlob registry, ref bool boundsDirty)
        {
            if (!ClipRegistryApi.TryResolveClip(ref registry, layer.queuedClip, out int promotedClipIndex))
            {
                layer.flags &= ~(PlaybackFlags.Active | PlaybackFlags.HasQueued);
                layer.animationKey = 0u;
                boundsDirty = true;
                return;
            }

            ref ClipBlob promotedClip = ref registry.clips[promotedClipIndex];
            float promotedBlend = math.max(layer.queuedBlend, 0f);

            if (promotedBlend > 0f)
            {
                layer.previousClip = layer.clip;
                layer.previousClipIndex = layer.clipIndex;
                layer.previousTime = layer.time;
                layer.previousSpeed = layer.speed;
                layer.previousLoop = layer.loop; // same demotion order PlaybackCommandMath.ApplyPlay uses, and for the same reason

                layer.blendElapsed = 0f;
                layer.blendDuration = promotedBlend;
                layer.flags |= PlaybackFlags.Blending;
            }

            layer.clip = layer.queuedClip;
            layer.clipIndex = promotedClipIndex;
            layer.speed = layer.queuedSpeed;
            layer.loop = layer.queuedLoop;
            layer.animationKey = 0u; // queueing has no named-entry counterpart yet; a promoted clip is always raw
            layer.time = layer.queuedSpeed < 0f ? promotedClip.duration : 0f;
            layer.timeAtFrameStart = layer.time; // re-snapshotted so the promoted clip's event window starts at its own start, not where the finished clip left off

            layer.queuedClip = default;
            layer.queuedSpeed = 0f;
            layer.queuedLoop = LoopMode.UseClipDefault;
            layer.queuedBlend = 0f;
            layer.flags &= ~(PlaybackFlags.HasQueued | PlaybackFlags.Finished);

            boundsDirty = true;
        }
    }
}
