// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

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

                // Cleared for every layer, including inactive ones, before anything can set it
                // again. FinishedThisFrame is a one-frame pulse, and the layer that raised it was
                // deactivated by the same completion — so leaving the clear inside the active-only
                // path would latch the flag on forever and make PlaybackApi.HasFinishedThisFrame
                // report a completion that happened minutes ago.
                layer.flags &= ~PlaybackFlags.FinishedThisFrame;

                if ((layer.flags & PlaybackFlags.Active) == 0)
                {
                    continue;
                }

                // A clip that finished last frame with something queued behind it is promoted here,
                // at the start of the advance — deliberately one frame after the completion, so that
                // EventEmissionSystem got a whole frame to see the finished clip still in place.
                if ((layer.flags & PlaybackFlags.Finished) != 0
                    && (layer.flags & PlaybackFlags.HasQueued) != 0)
                {
                    PromoteQueuedClip(ref layer, ref registry, boundsDirtyEnabled);
                }

                if ((layer.flags & PlaybackFlags.Blending) != 0)
                {
                    AdvanceBlend(ref layer, deltaTime, boundsDirtyEnabled);
                    if ((layer.flags & PlaybackFlags.Active) == 0)
                    {
                        // A Stop fade ran to completion and took the layer with it.
                        continue;
                    }
                }

                if (layer.clipIndex < 0 || layer.clipIndex >= registry.clips.Length)
                {
                    continue;
                }

                AdvanceCurrentClip(ref layer, ref registry, deltaTime, boundsDirtyEnabled);
            }
        }

        /// <summary>Advances the crossfade: the outgoing clip keeps running on its own speed and loop mode while the blend weight climbs, and the source slot is released when it reaches 1.</summary>
        private static void AdvanceBlend(
            ref PlaybackLayer layer,
            float deltaTime,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
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

            boundsDirtyEnabled.ValueRW = true; // the layer now references one clip fewer, so the bounds union shrinks

            if (wasFadingOutToNothing)
            {
                layer.flags = PlaybackFlags.None;
                layer.time = 0f;
                layer.timeAtFrameStart = 0f;
                layer.speed = 0f;
                layer.loop = LoopMode.UseClipDefault;
            }
        }

        /// <summary>Advances the current clip's time and applies its loop mode.</summary>
        private static void AdvanceCurrentClip(
            ref PlaybackLayer layer,
            ref ClipRegistryBlob registry,
            float deltaTime,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
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
            boundsDirtyEnabled.ValueRW = true;
        }

        /// <summary>
        /// Promotes the one-deep queue slot into the current slot, crossfading from the pose the
        /// finished clip ended on. Deferred to the top of the next advance rather than run at the
        /// instant of completion: promoting immediately would replace the layer's clip fields before
        /// <c>EventEmissionSystem</c> runs later in the same group, so that system would name the
        /// wrong clip in its <c>ClipFinished</c> event and silently drop every marker in the
        /// finishing clip's last segment — exactly where a hit frame or footstep sits.
        /// </summary>
        private static void PromoteQueuedClip(
            ref PlaybackLayer layer,
            ref ClipRegistryBlob registry,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
        {
            if (!ClipRegistryApi.TryResolveClip(ref registry, layer.queuedClip, out int promotedClipIndex))
            {
                layer.flags &= ~(PlaybackFlags.Active | PlaybackFlags.HasQueued);
                boundsDirtyEnabled.ValueRW = true;
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
                layer.previousLoop = layer.loop; // same demotion order as CommandApplySystem.ApplyPlay, and for the same reason

                layer.blendElapsed = 0f;
                layer.blendDuration = promotedBlend;
                layer.flags |= PlaybackFlags.Blending;
            }

            layer.clip = layer.queuedClip;
            layer.clipIndex = promotedClipIndex;
            layer.speed = layer.queuedSpeed;
            layer.loop = layer.queuedLoop;
            layer.time = layer.queuedSpeed < 0f ? promotedClip.duration : 0f;
            layer.timeAtFrameStart = layer.time; // re-snapshotted so the promoted clip's event window starts at its own start, not where the finished clip left off

            layer.queuedClip = default;
            layer.queuedSpeed = 0f;
            layer.queuedLoop = LoopMode.UseClipDefault;
            layer.queuedBlend = 0f;
            layer.flags &= ~(PlaybackFlags.HasQueued | PlaybackFlags.Finished);

            boundsDirtyEnabled.ValueRW = true;
        }
    }
}
