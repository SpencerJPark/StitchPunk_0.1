// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Advances playback time, blends and queues for every actor, every frame
    /// (architecture section 5.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Never gated on <see cref="AnimVisible"/>.</strong> This system lives in the logic
    /// group precisely so that an actor behind the camera keeps exact time, finishes its clips on
    /// schedule and reports those completions. Animation-driven gameplay that stops when the camera
    /// looks away is the desync the group split exists to prevent (section 5.1).
    /// </para>
    /// <para>
    /// <strong>Time is not wrapped here.</strong> <see cref="PlaybackLayer.time"/> accumulates on
    /// the un-wrapped timeline and <c>ClipSampler.MapTime</c> folds it into the clip's window at
    /// sampling time. Wrapping the stored value would destroy the lap count that
    /// <c>EventWrapMath.CollectCrossings</c> needs to fire markers on a frame long enough to cross
    /// the loop point more than once (section 5.5). Only <see cref="LoopMode.Once"/> writes a
    /// clamped value back, because a Once clip genuinely stops there.
    /// </para>
    /// <para>
    /// <strong>What sets <see cref="BoundsDirty"/> here.</strong> Queue promotion, Once completion
    /// and blend completion — the three moments this system changes which clips a layer references.
    /// Not a change-version filter on <see cref="PlaybackLayer"/>: this system writes <c>time</c>
    /// into that buffer every frame for every active actor, so such a filter is always true and the
    /// bounds pass degenerates into running unconditionally (section 5.8).
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Advances one actor's layers by a frame: blends, then time, then loop handling, then the
    /// queue.
    /// </summary>
    /// <remarks>
    /// <see cref="BoundsDirty"/> is declared <c>WithPresent</c>. An <c>EnabledRefRW&lt;T&gt;</c>
    /// parameter enrols <c>T</c> in the query as an <em>All</em> component by default, which would
    /// restrict this job to actors whose bounds were already dirty — every actor would advance on
    /// its first frame, then freeze the moment the bounds pass cleaned it, with no error anywhere.
    /// </remarks>
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
                // path would latch the flag on forever and make PlaybackQuery.FinishedThisFrame
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

        /// <summary>
        /// Advances the crossfade: the outgoing clip keeps running on its own speed and loop mode
        /// while the blend weight climbs, and the source slot is released when it reaches 1.
        /// </summary>
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

            // The layer now references one clip fewer, so the bounds union shrinks. This is one of
            // the three moments section 5.8 names.
            boundsDirtyEnabled.ValueRW = true;

            if (wasFadingOutToNothing)
            {
                layer.flags = PlaybackFlags.None;
                layer.time = 0f;
                layer.advanceStartTime = 0f;
                layer.speed = 0f;
                layer.loop = LoopMode.UseClipDefault;
            }
        }

        /// <summary>
        /// Advances the current clip's time and applies its loop mode.
        /// </summary>
        private static void AdvanceCurrentClip(
            ref PlaybackLayer layer,
            ref ClipRegistryBlob registry,
            float deltaTime,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
        {
            ref ClipBlob clip = ref registry.clips[layer.clipIndex];
            LoopMode resolvedLoopMode = ClipSampler.ResolveLoopMode(layer.loop, clip.defaultLoop);

            layer.advanceStartTime = layer.time;
            layer.time += deltaTime * layer.speed;

            // Loop and PingPong never finish: their stored time keeps climbing and the sampler folds
            // it. Only Once has an end to reach.
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

            // A queued follow-up is promoted at the top of the NEXT advance, not here — see the
            // remarks on PromoteQueuedClip. The layer stays active and holds its final pose until
            // then, which is what a Once clip does anyway.
            if ((layer.flags & PlaybackFlags.HasQueued) != 0)
            {
                return;
            }

            layer.flags &= ~PlaybackFlags.Active;
            boundsDirtyEnabled.ValueRW = true;
        }

        /// <summary>
        /// Promotes the one-deep queue slot into the current slot, crossfading from the pose the
        /// finished clip ended on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>This runs one frame after the completion, not at it.</strong> The obvious design
        /// — promote the instant the clip ends — replaces <see cref="PlaybackLayer.clip"/>,
        /// <c>clipIndex</c>, <c>time</c> and <c>advanceStartTime</c> before
        /// <c>EventEmissionSystem</c> has run, and that system runs later in the same group. It
        /// would therefore find the follow-up where the finished clip used to be, and would emit
        /// <c>ClipFinished</c> naming the wrong clip while silently dropping every marker in the
        /// finishing clip's last segment — which is precisely where a hit frame or a footstep sits.
        /// Deferring by one advance costs a single extra frame of the final pose, on hard-cut queues
        /// only (a blended promotion enters at weight 0, so nothing is visible either way), and a
        /// <see cref="LoopMode.Once"/> clip holds that pose regardless.
        /// </para>
        /// <para>
        /// The alternative considered and rejected was a <c>finishedClip</c> field on
        /// <see cref="PlaybackLayer"/>. It fixes the wrong-clip half and not the dropped-markers
        /// half — recovering those needs the finished clip's index and window too — and section 12's
        /// R7 already flags this buffer element's size as the thing to watch.
        /// </para>
        /// <para>
        /// A queued id that no longer resolves cannot happen through the API — <c>CommandApply</c>
        /// resolves it when it is queued and the registry is immutable — but the layer is left
        /// stopped rather than left half-promoted if a hand-built one ever does.
        /// </para>
        /// </remarks>
        private static void PromoteQueuedClip(
            ref PlaybackLayer layer,
            ref ClipRegistryBlob registry,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
        {
            if (!ClipRegistryUtil.TryResolveClip(ref registry, layer.queuedClip, out int promotedClipIndex))
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

                // Same demotion order as CommandApplySystem, for the same reason: the outgoing clip
                // must fade under the mode it was playing, not under whatever the queued entry asks
                // for. Read section 5.2's note on previousLoop before touching these five lines.
                layer.previousLoop = layer.loop;

                layer.blendElapsed = 0f;
                layer.blendDuration = promotedBlend;
                layer.flags |= PlaybackFlags.Blending;
            }

            layer.clip = layer.queuedClip;
            layer.clipIndex = promotedClipIndex;
            layer.speed = layer.queuedSpeed;
            layer.loop = layer.queuedLoop;
            layer.time = layer.queuedSpeed < 0f ? promotedClip.duration : 0f;

            // Re-snapshotted so the event window for the promoted clip starts where the promoted
            // clip starts, not where the finished clip left off (amendment A27).
            layer.advanceStartTime = layer.time;

            layer.queuedClip = default;
            layer.queuedSpeed = 0f;
            layer.queuedLoop = LoopMode.UseClipDefault;
            layer.queuedBlend = 0f;
            layer.flags &= ~(PlaybackFlags.HasQueued | PlaybackFlags.Finished);

            boundsDirtyEnabled.ValueRW = true;
        }
    }
}
