// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs after <c>TransformSampleSystem</c>: publishes each visible VAT part's frame addressing
    /// to its shader properties. LOD scales publish rate but never freezes frames (a VAT mesh has
    /// no rest pose to fall back to) — the one place this diverges from the transform path's LOD 3 freeze.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(TransformSampleSystem))]
    [BurstCompile]
    public partial struct VatMaterialSystem : ISystem
    {
        private float previousElapsedTime; // other edge of the quantization interval, held per system like TransformSampleSystem's own

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VatDriven>();
            previousElapsedTime = 0f;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float currentElapsedTime = (float)SystemAPI.Time.ElapsedTime;

            float defaultSampleRateHz = 0f;
            if (SystemAPI.TryGetSingleton(out AnimationToolkitConfig toolkitConfig))
            {
                defaultSampleRateHz = toolkitConfig.defaultSampleRateHz;
            }

            WriteVatPropertiesJob writeJob = new WriteVatPropertiesJob
            {
                previousElapsedTime = previousElapsedTime,
                currentElapsedTime = currentElapsedTime,
                defaultSampleRateHz = defaultSampleRateHz,
                playbackLayerLookup = SystemAPI.GetBufferLookup<PlaybackLayer>(true),
                clipRegistryLookup = SystemAPI.GetComponentLookup<ClipRegistry>(true),
                sampleSettingsLookup = SystemAPI.GetComponentLookup<SampleSettings>(true),
                animLodLookup = SystemAPI.GetComponentLookup<AnimLod>(true)
            };
            state.Dependency = writeJob.ScheduleParallel(state.Dependency);

            previousElapsedTime = currentElapsedTime;
        }
    }

    [BurstCompile]
    [WithAll(typeof(AnimVisible))]
    internal partial struct WriteVatPropertiesJob : IJobEntity
    {
        public float previousElapsedTime;
        public float currentElapsedTime;
        public float defaultSampleRateHz;

        [ReadOnly] public BufferLookup<PlaybackLayer> playbackLayerLookup;
        [ReadOnly] public ComponentLookup<ClipRegistry> clipRegistryLookup;
        [ReadOnly] public ComponentLookup<SampleSettings> sampleSettingsLookup;

        [ReadOnly] public ComponentLookup<AnimLod> animLodLookup; // opt-in — see the identical note in SampleActorPosesJob

        private void Execute(
            in VatDriven vatDriven,
            in RigPartBinding partBinding,
            ref VatFrameAProperty vatFrameA,
            ref VatFrameBProperty vatFrameB,
            ref VatBlendProperty vatBlend)
        {
            Entity actorEntity = partBinding.actorRoot;
            if (!playbackLayerLookup.HasBuffer(actorEntity) || !clipRegistryLookup.HasComponent(actorEntity))
            {
                return;
            }

            // Rate and phase are the actor's, not the part's: every VAT part on one actor publishes
            // on the same frames, so a rig does not tear across its own pieces.
            if (sampleSettingsLookup.HasComponent(actorEntity))
            {
                SampleSettings sampleSettings = sampleSettingsLookup[actorEntity];
                byte lodLevel = animLodLookup.HasComponent(actorEntity) ? animLodLookup[actorEntity].level : (byte)0;
                float requestedRateHz = sampleSettings.rateHz > 0f ? sampleSettings.rateHz : defaultSampleRateHz;
                float effectiveRateHz = AnimationLodResolver.EffectiveSampleRateHz(lodLevel, requestedRateHz);
                if (!ClipSampler.ShouldSample(
                        previousElapsedTime, currentElapsedTime, effectiveRateHz, sampleSettings.phase01))
                {
                    return;
                }
            }

            DynamicBuffer<PlaybackLayer> layers = playbackLayerLookup[actorEntity];
            if (vatDriven.layerIndex >= layers.Length)
            {
                return;
            }

            PlaybackLayer layer = layers[vatDriven.layerIndex];
            BlobAssetReference<ClipRegistryBlob> registryReference = clipRegistryLookup[actorEntity].Value;
            ref ClipRegistryBlob registry = ref registryReference.Value;

            if ((layer.flags & PlaybackFlags.Active) == 0
                || !TryResolveGlobalFrame(
                    ref registry, layer.clipIndex, layer.time, layer.loop, partBinding.targetIndex,
                    out float currentFrame))
            {
                // The driving layer is stopped, or is playing a clip with no baked VAT range. Hold
                // the last published frame — a VAT mesh has no rest pose to fall back to, so
                // freezing is the only sane still — but force the weight to 0 so the shader cannot
                // keep interpolating toward a B frame that no longer means anything.
                vatBlend.Value = 0f;
                return;
            }

            float blendWeight = 0f;
            // Defaults to A, not 0: with blend at 0 the shader ignores B entirely, but a stray 0
            // would point at the texture's first frame, and any future shader that lerped before
            // testing the weight would snap to another clip's pose. Pointing B at A makes the blend
            // a no-op under every reading.
            float sourceFrame = currentFrame;
            if ((layer.flags & PlaybackFlags.Blending) != 0 && layer.blendDuration > 0f
                && TryResolveGlobalFrame(
                    ref registry, layer.previousClipIndex, layer.previousTime, layer.previousLoop,
                    partBinding.targetIndex, out float previousFrame))
            {
                sourceFrame = previousFrame;
                blendWeight = math.saturate(layer.blendElapsed / layer.blendDuration);
            }

            // A is the destination and B the source, so the weight runs 0 -> 1 from the outgoing clip
            // to the incoming one, matching how blendElapsed advances.
            vatFrameA.Value = currentFrame;
            vatFrameB.Value = sourceFrame;
            vatBlend.Value = blendWeight;
        }

        /// <summary>
        /// Maps a layer's playback time onto a fractional global frame index into the VAT texture,
        /// for the specific part requesting it. The part's own dense <paramref name="targetIndex"/>
        /// is checked first against <see cref="ClipBlob.vatTargetRanges"/>, falling back to the
        /// clip-wide range only when no entry names it — the same two-step rule bake time uses, and
        /// what lets a torso and a cape on one actor, both bound to the same clip, play
        /// independently baked motion.
        /// </summary>
        /// <returns>False when the clip index is unresolved or the resolved range is empty.</returns>
        private static bool TryResolveGlobalFrame(
            ref ClipRegistryBlob registry,
            int clipIndex,
            float playbackTime,
            LoopMode requestedLoopMode,
            int targetIndex,
            out float globalFrame)
        {
            globalFrame = 0f;
            if (clipIndex < 0 || clipIndex >= registry.clips.Length)
            {
                return false;
            }

            ref ClipBlob clip = ref registry.clips[clipIndex];

            int frameStart = clip.vatFrameStart;
            int frameCount = clip.vatFrameCount;
            float fps = clip.vatFps;

            for (int rangeIndex = 0; rangeIndex < clip.vatTargetRanges.Length; rangeIndex++)
            {
                ref VatTrackRangeBlob targetRange = ref clip.vatTargetRanges[rangeIndex];
                if (targetRange.targetIndex == targetIndex)
                {
                    frameStart = targetRange.frameStart;
                    frameCount = targetRange.frameCount;
                    fps = targetRange.fps;
                    break;
                }
            }

            if (frameStart < 0 || frameCount <= 0)
            {
                return false;
            }

            // Time mapping goes through the same ClipSampler function the transform path uses, so a
            // loop/pingpong/clamp clip does not drift between techniques on one actor. Loop seams
            // need no special case: loop-safe clips carry a duplicated final frame, so
            // floor(frame) + 1 never leaves the clip's row range.
            LoopMode resolvedLoopMode = ClipSampler.ResolveLoopMode(requestedLoopMode, clip.defaultLoop);
            float mappedTime = ClipSampler.MapTime(playbackTime, clip.duration, resolvedLoopMode);

            // Clamped to the resolved range's own row span: the last frame is a real texel, so the
            // index may reach frameCount - 1 but never the first row of whatever range was baked
            // after it, whether that neighbour is another clip or another target's block of the same
            // clip.
            float localFrame = math.clamp(mappedTime * fps, 0f, frameCount - 1);
            globalFrame = frameStart + localFrame;
            return true;
        }
    }
}
