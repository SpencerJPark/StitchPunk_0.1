// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Publishes each visible VAT part's frame addressing to its per-instance shader properties
    /// (architecture sections 5.8, 6.5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A VAT part is driven by exactly one playback layer, named per part by
    /// <see cref="VatDriven.layerIndex"/> — a torso and a cape on the same actor may follow
    /// different layers. This system reads that layer, converts its playback time into a
    /// <em>fractional global frame index</em> into the texture, and hands the shader two of them
    /// plus a blend weight: <c>_VatFrameA</c> for the current clip, <c>_VatFrameB</c> for the
    /// crossfade source, <c>_VatBlend</c> for the weight between them.
    /// </para>
    /// <para>
    /// <strong>Time mapping goes through <see cref="ClipSampler.MapTime"/>.</strong> Frames are just
    /// playback time in another unit, so the loop/pingpong/clamp behaviour of a VAT part has to be
    /// the same function the transform path uses, or the same clip drifts between techniques on one
    /// actor. That is section 5.11 doing its job in a place where re-deriving `fmod` would have been
    /// the obvious shortcut.
    /// </para>
    /// <para>
    /// <strong>Why <c>_VatFrameB</c> defaults to <c>_VatFrameA</c> rather than 0.</strong> With
    /// <c>_VatBlend = 0</c> the shader ignores B entirely, so its value is free — but a 0 there
    /// points at the first frame of the whole texture, and any future shader that lerps before
    /// testing the weight would snap the mesh to another clip's pose. Pointing B at A makes the
    /// blend a no-op under every reading.
    /// </para>
    /// <para>
    /// Loop seams need no special case: loop-safe clips carry a duplicated final frame (§4.7), so
    /// <c>floor(frame) + 1</c> never leaves the clip's row range.
    /// </para>
    /// <para>
    /// <strong>LOD scales how often frames are published, and never freezes them</strong> (§5.10).
    /// A VAT mesh holds whatever row it was last given, so a frozen frame is a frozen *mesh* — and
    /// since GPU cost does not depend on CPU LOD, there is nothing to gain by it. Level 3 therefore
    /// keeps publishing at quarter rate while the transform path stops entirely, which is the one
    /// place the two techniques deliberately diverge.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(TransformSampleSystem))]
    [BurstCompile]
    public partial struct VatMaterialSystem : ISystem
    {
        /// <summary>
        /// World elapsed time at the previous update — the other edge of the quantization interval,
        /// held per system for the same reason <c>TransformSampleSystem</c> holds it.
        /// </summary>
        private float previousElapsedTime;

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

    /// <summary>
    /// Converts one VAT part's driving layer into frame addressing.
    /// </summary>
    /// <remarks>
    /// Iterates <em>parts</em> rather than actor roots, because <see cref="VatDriven"/> is exactly
    /// the VAT archetype and the actor is reachable through <see cref="RigPartBinding.actorRoot"/>.
    /// Both lookups are read-only, so there is nothing to coordinate between workers — unlike the
    /// sampling systems, this one writes only components on the entity it is iterating.
    /// </remarks>
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

        /// <summary>Opt-in per amendment A23 — see the identical note in <c>SampleActorPosesJob</c>.</summary>
        [ReadOnly] public ComponentLookup<AnimLod> animLodLookup;

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
                float effectiveRateHz = AnimationLodPolicy.EffectiveSampleRateHz(lodLevel, requestedRateHz);
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
            float sourceFrame = currentFrame;
            if ((layer.flags & PlaybackFlags.Blending) != 0 && layer.blendDuration > 0f
                && TryResolveGlobalFrame(
                    ref registry, layer.previousClipIndex, layer.previousTime, layer.previousLoop,
                    partBinding.targetIndex, out float previousFrame))
            {
                sourceFrame = previousFrame;
                blendWeight = math.saturate(layer.blendElapsed / layer.blendDuration);
            }

            // A is the destination and B the source, so the weight runs 0 → 1 from the outgoing clip
            // to the incoming one, matching how blendElapsed advances.
            vatFrameA.Value = currentFrame;
            vatFrameB.Value = sourceFrame;
            vatBlend.Value = blendWeight;
        }

        /// <summary>
        /// Maps a layer's playback time onto a fractional global frame index into the VAT texture,
        /// for the specific part requesting it.
        /// </summary>
        /// <remarks>
        /// <strong>C10, multi-source VAT tracks.</strong> A part resolves its own dense
        /// <paramref name="targetIndex"/> first against <see cref="ClipBlob.vatTargetRanges"/> and
        /// only falls back to the clip-wide <see cref="ClipBlob.vatFrameStart"/> /
        /// <see cref="ClipBlob.vatFrameCount"/> / <see cref="ClipBlob.vatFps"/> range when no entry
        /// names its target — the same two-step rule <c>VatTextureSetAsset.TryGetTrackRange</c>
        /// performs at bake time (see its remarks for why the two must agree). This is what lets a
        /// torso and a cape on one actor, both bound to the same clip identity, play independently
        /// baked motion: the torso's part keeps resolving the shared range exactly as it always did,
        /// while the cape's part — whose <see cref="RigPartBinding.targetIndex"/> matches an entry in
        /// <see cref="ClipBlob.vatTargetRanges"/> — resolves its own.
        /// </remarks>
        /// <param name="registry">The actor's clip registry blob.</param>
        /// <param name="clipIndex">Dense index of the clip the driving layer is playing.</param>
        /// <param name="playbackTime">The layer's current playback time in seconds.</param>
        /// <param name="requestedLoopMode">The layer's requested loop mode.</param>
        /// <param name="targetIndex">
        /// Dense target index of the VAT part asking for a frame (<see cref="RigPartBinding.targetIndex"/>).
        /// </param>
        /// <param name="globalFrame">The resolved fractional global frame index on success.</param>
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
