// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// The package's single sampler (architecture section 5.11): pure, allocation-free,
    /// Burst-compatible static functions for easing, loop-mode time mapping, track sampling,
    /// bottom-up layer composition, blend pose lerping, and sample-rate quantization. Runtime
    /// jobs, PlayMode tests, EditMode tests, and the editor preview all call these same
    /// functions — sampler divergence is eliminated structurally, not by discipline.
    /// </summary>
    [BurstCompile]
    public static class ClipSampler
    {
        /// <summary>
        /// The identity atlas rect (scale 1,1 / offset 0,0 — the full texture), used for a pose
        /// until an atlas-mode sprite track writes a frame.
        /// </summary>
        public static readonly float4 IdentityAtlasRect = new float4(1f, 1f, 0f, 0f);

        /// <summary>
        /// Applies the per-key easing curve to a linear 0–1 segment position
        /// (architecture section 3.2; curves absorbed verbatim from the audited host sampler).
        /// <see cref="Interpolation.Step"/> returns 0 (full hold of the segment's left key);
        /// track sampling short-circuits Step before easing, so the 0 is a consistent fallback for
        /// direct callers.
        /// </summary>
        /// <param name="linearTime">Linear position inside the key segment, in [0, 1].</param>
        /// <param name="interpolation">The left key's easing mode.</param>
        /// <returns>The eased blend weight toward the segment's right key.</returns>
        [BurstCompile]
        public static float Ease(float linearTime, Interpolation interpolation)
        {
            switch (interpolation)
            {
                case Interpolation.Step:
                    return 0f;
                case Interpolation.EaseIn:
                    return linearTime * linearTime;
                case Interpolation.EaseOut:
                    return 1f - (1f - linearTime) * (1f - linearTime);
                case Interpolation.EaseInOut:
                    return linearTime < 0.5f
                        ? 2f * linearTime * linearTime
                        : 1f - 2f * (1f - linearTime) * (1f - linearTime);
                default:
                    return linearTime;
            }
        }

        /// <summary>
        /// Resolves the <see cref="LoopMode.UseClipDefault"/> command sentinel against a clip's
        /// authored default (architecture section 5.4).
        /// </summary>
        /// <param name="requested">The requested mode, possibly <see cref="LoopMode.UseClipDefault"/>.</param>
        /// <param name="clipDefault">The clip's authored default loop mode.</param>
        /// <returns>The resolved loop mode.</returns>
        [BurstCompile]
        public static LoopMode ResolveLoopMode(LoopMode requested, LoopMode clipDefault)
        {
            return requested == LoopMode.UseClipDefault ? clipDefault : requested;
        }

        /// <summary>
        /// Maps un-wrapped playback time onto the clip's [0, duration] sampling window per loop
        /// mode (architecture section 5.4): Once clamps; Loop wraps (negative times wrap into
        /// range, supporting reverse playback); PingPong reflects via
        /// <c>duration − |duration − mod(time, 2 × duration)|</c>. An unresolved
        /// <see cref="LoopMode.UseClipDefault"/> clamps defensively — callers resolve it first via
        /// <see cref="ResolveLoopMode"/>.
        /// </summary>
        /// <param name="rawTime">Playback time in seconds on the clip's un-wrapped timeline.</param>
        /// <param name="duration">Clip duration in seconds.</param>
        /// <param name="resolvedLoopMode">The resolved loop mode.</param>
        /// <returns>The mapped time in [0, duration]; 0 when duration is not positive.</returns>
        [BurstCompile]
        public static float MapTime(float rawTime, float duration, LoopMode resolvedLoopMode)
        {
            if (duration <= 0f)
            {
                return 0f;
            }
            switch (resolvedLoopMode)
            {
                case LoopMode.Loop:
                    return PositiveModulo(rawTime, duration);
                case LoopMode.PingPong:
                {
                    float wrappedTime = PositiveModulo(rawTime, 2f * duration);
                    return duration - math.abs(duration - wrappedTime);
                }
                default:
                    return math.clamp(rawTime, 0f, duration);
            }
        }

        /// <summary>
        /// <see cref="MapTime"/> divided by duration: the normalized sampling time in [0, 1].
        /// </summary>
        /// <param name="rawTime">Playback time in seconds on the clip's un-wrapped timeline.</param>
        /// <param name="duration">Clip duration in seconds.</param>
        /// <param name="resolvedLoopMode">The resolved loop mode.</param>
        /// <returns>The normalized time in [0, 1]; 0 when duration is not positive.</returns>
        [BurstCompile]
        public static float MapTimeNormalized(float rawTime, float duration, LoopMode resolvedLoopMode)
        {
            if (duration <= 0f)
            {
                return 0f;
            }
            return MapTime(rawTime, duration, resolvedLoopMode) / duration;
        }

        /// <summary>
        /// Samples one transform track at a normalized time (architecture section 5.6; key scan
        /// and easing absorbed verbatim from the audited host sampler). Times before the first key
        /// clamp to it, times after the last key clamp to it, and a single-key track returns that
        /// key at every time. The left key's <see cref="Interpolation"/> drives the segment;
        /// <see cref="Interpolation.Step"/> holds the left key. An empty track returns the neutral
        /// values (zero position/rotation, unit scale) — composition skips empty tracks entirely.
        /// </summary>
        /// <param name="track">The track to sample.</param>
        /// <param name="normalizedTime">Sampling time normalized to the clip's duration.</param>
        /// <param name="position">Sampled local position (z = draw-layer order).</param>
        /// <param name="rotationZ">Sampled rotation about z in radians.</param>
        /// <param name="scale">Sampled non-uniform x/y scale.</param>
        [BurstCompile]
        public static void SampleTransformTrack(
            ref TransformTrackBlob track,
            float normalizedTime,
            out float3 position,
            out float rotationZ,
            out float2 scale)
        {
            ref BlobArray<TransformKeyBlob> keys = ref track.keys;
            if (keys.Length == 0)
            {
                position = float3.zero;
                rotationZ = 0f;
                scale = new float2(1f, 1f);
                return;
            }

            FindKeySegment(ref keys, normalizedTime, out int previousIndex, out int nextIndex);
            ref TransformKeyBlob previousKey = ref keys[previousIndex];
            ref TransformKeyBlob nextKey = ref keys[nextIndex];

            if (previousIndex == nextIndex || previousKey.interpolation == Interpolation.Step)
            {
                position = previousKey.position;
                rotationZ = previousKey.rotationZ;
                scale = previousKey.scale;
                return;
            }

            float keySpan = nextKey.normalizedTime - previousKey.normalizedTime;
            float linearWeight = keySpan > 0f ? (normalizedTime - previousKey.normalizedTime) / keySpan : 0f;
            float easedWeight = Ease(linearWeight, previousKey.interpolation);

            position = math.lerp(previousKey.position, nextKey.position, easedWeight);
            rotationZ = math.lerp(previousKey.rotationZ, nextKey.rotationZ, easedWeight);
            scale = math.lerp(previousKey.scale, nextKey.scale, easedWeight);
        }

        /// <summary>
        /// Samples one sprite track at a normalized time (architecture section 5.7): nearest-key
        /// selection, with slice index −1 meaning "no change" (host convention absorbed). Slice
        /// mode writes <paramref name="sliceIndex"/>; atlas mode writes
        /// <paramref name="atlasRect"/>; an empty track writes nothing.
        /// </summary>
        /// <param name="track">The track to sample.</param>
        /// <param name="normalizedTime">Sampling time normalized to the clip's duration.</param>
        /// <param name="sliceIndex">Current slice value; overwritten when the nearest key selects a frame ≥ 0.</param>
        /// <param name="atlasRect">Current atlas rect; overwritten in atlas mode.</param>
        [BurstCompile]
        public static void SampleSpriteTrack(
            ref SpriteTrackBlob track,
            float normalizedTime,
            ref int sliceIndex,
            ref float4 atlasRect)
        {
            ref BlobArray<SpriteKeyBlob> keys = ref track.keys;
            if (keys.Length == 0)
            {
                return;
            }

            FindSpriteKeySegment(ref keys, normalizedTime, out int previousIndex, out int nextIndex);
            int chosenIndex = previousIndex;
            if (previousIndex != nextIndex)
            {
                float keySpan = keys[nextIndex].normalizedTime - keys[previousIndex].normalizedTime;
                float linearWeight = keySpan > 0f
                    ? (normalizedTime - keys[previousIndex].normalizedTime) / keySpan
                    : 0f;
                chosenIndex = linearWeight < 0.5f ? previousIndex : nextIndex;
            }

            if (track.mode == SpriteFrameMode.Slice)
            {
                if (keys[chosenIndex].sliceIndex >= 0)
                {
                    sliceIndex = keys[chosenIndex].sliceIndex;
                }
            }
            else
            {
                atlasRect = keys[chosenIndex].atlasRect;
            }
        }

        /// <summary>
        /// Initializes a sampled pose from a part's rest pose (architecture section 5.6):
        /// position/rotation/scale copied, slice index from <see cref="TargetRestPose.restSliceIndex"/>,
        /// atlas rect set to <see cref="IdentityAtlasRect"/>.
        /// </summary>
        /// <param name="restPose">The part's rest pose.</param>
        /// <param name="pose">The initialized output pose.</param>
        [BurstCompile]
        public static void RestToPose(in TargetRestPose restPose, out TargetPose pose)
        {
            pose = new TargetPose
            {
                localPosition = restPose.localPosition,
                rotationZ = restPose.rotationZ,
                scale = restPose.scale,
                sliceIndex = restPose.restSliceIndex,
                atlasRect = IdentityAtlasRect
            };
        }

        /// <summary>
        /// Applies every track of one clip bound to a target onto an existing pose, in canonical
        /// order — transform tracks before sprite tracks, all tracks applied, no first-match break
        /// (architecture sections 4.5, 5.6).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Keys are offsets from the rest pose, never absolute local values</strong>
        /// (architecture sections 3.2, 4.6; amendment A31). The two track ops differ in what they
        /// anchor to, not in whether the key is a delta:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <see cref="TrackBlendOp.Override"/> anchors to the <em>rest pose</em>, so the masked
        /// channels become <c>rest + key</c> (scale: <c>rest × key</c>) and whatever lower layers
        /// composited into those channels is replaced.
        /// </description></item>
        /// <item><description>
        /// <see cref="TrackBlendOp.Additive"/> anchors to the <em>incoming composited pose</em>, so
        /// the masked channels become <c>composited + key</c> (scale: <c>composited × key</c>).
        /// </description></item>
        /// </list>
        /// <para>
        /// Channels outside the mask are untouched by either op, so they keep whatever the layers
        /// below left there — which for the bottom layer is the rest pose the composition seeded.
        /// </para>
        /// <para>
        /// This is why <paramref name="restPose"/> is a parameter rather than something the caller
        /// could bake into the incoming pose: an Override track must reach past every lower layer's
        /// contribution to the rest value, which the incoming pose no longer carries.
        /// </para>
        /// </remarks>
        /// <param name="clip">The clip whose tracks to apply.</param>
        /// <param name="targetIndex">The part's dense target index.</param>
        /// <param name="normalizedTime">Sampling time normalized to the clip's duration.</param>
        /// <param name="restPose">The part's rest pose — the frame Override keys are offsets from.</param>
        /// <param name="pose">The pose the tracks apply onto.</param>
        [BurstCompile]
        public static void ApplyClipToPose(
            ref ClipBlob clip,
            int targetIndex,
            float normalizedTime,
            in TargetRestPose restPose,
            ref TargetPose pose)
        {
            for (int trackIndex = 0; trackIndex < clip.transformTracks.Length; trackIndex++)
            {
                ref TransformTrackBlob track = ref clip.transformTracks[trackIndex];
                if (track.targetIndex != targetIndex || track.keys.Length == 0)
                {
                    continue;
                }

                SampleTransformTrack(
                    ref track,
                    normalizedTime,
                    out float3 sampledPosition,
                    out float sampledRotationZ,
                    out float2 sampledScale);

                // The only difference between the two ops is the frame the key is added to: the
                // rest pose for Override, the composited-so-far pose for Additive. Both treat the
                // key as a delta (amendment A31).
                bool isAdditive = track.blendOp == TrackBlendOp.Additive;
                if ((track.channels & AnimatedChannels.PositionXY) != 0)
                {
                    float2 positionAnchorXY = isAdditive
                        ? pose.localPosition.xy
                        : restPose.localPosition.xy;
                    pose.localPosition.x = positionAnchorXY.x + sampledPosition.x;
                    pose.localPosition.y = positionAnchorXY.y + sampledPosition.y;
                }
                if ((track.channels & AnimatedChannels.LayerZ) != 0)
                {
                    float layerZAnchor = isAdditive ? pose.localPosition.z : restPose.localPosition.z;
                    pose.localPosition.z = layerZAnchor + sampledPosition.z;
                }
                if ((track.channels & AnimatedChannels.RotationZ) != 0)
                {
                    float rotationAnchor = isAdditive ? pose.rotationZ : restPose.rotationZ;
                    pose.rotationZ = rotationAnchor + sampledRotationZ;
                }
                if ((track.channels & AnimatedChannels.Scale) != 0)
                {
                    // Scale composes multiplicatively, so its identity is 1 and its "delta" is a
                    // factor — an unkeyed scale curve authored at 1 leaves the rest scale alone.
                    float2 scaleAnchor = isAdditive ? pose.scale : restPose.scale;
                    pose.scale = scaleAnchor * sampledScale;
                }
            }

            for (int spriteTrackIndex = 0; spriteTrackIndex < clip.spriteTracks.Length; spriteTrackIndex++)
            {
                ref SpriteTrackBlob spriteTrack = ref clip.spriteTracks[spriteTrackIndex];
                if (spriteTrack.targetIndex != targetIndex)
                {
                    continue;
                }
                SampleSpriteTrack(ref spriteTrack, normalizedTime, ref pose.sliceIndex, ref pose.atlasRect);
            }
        }

        /// <summary>
        /// Samples one clip for one target starting from the rest pose (architecture
        /// section 5.11) — the single-clip entry point shared by the editor preview and tests. An
        /// empty clip (no tracks bound to the target) returns the rest pose.
        /// </summary>
        /// <param name="clip">The clip to sample.</param>
        /// <param name="targetIndex">The part's dense target index.</param>
        /// <param name="normalizedTime">Sampling time normalized to the clip's duration.</param>
        /// <param name="rest">The part's rest pose.</param>
        /// <param name="pose">The sampled output pose.</param>
        [BurstCompile]
        public static void SamplePose(
            ref ClipBlob clip,
            int targetIndex,
            float normalizedTime,
            in TargetRestPose rest,
            out TargetPose pose)
        {
            RestToPose(in rest, out pose);
            ApplyClipToPose(ref clip, targetIndex, normalizedTime, in rest, ref pose);
        }

        /// <summary>
        /// Lerps two sampled poses by a 0–1 blend weight (architecture sections 5.4, 5.6):
        /// position, rotation, and scale interpolate linearly; sprite frames never blend — the
        /// nearest pose wins at the blend midpoint (snap, architecture section 10 answer 2).
        /// </summary>
        /// <param name="fromPose">The pose at weight 0 (the blend's "previous" side).</param>
        /// <param name="toPose">The pose at weight 1 (the blend's "current" side).</param>
        /// <param name="weight">Blend weight in [0, 1].</param>
        /// <param name="result">The blended output pose.</param>
        [BurstCompile]
        public static void LerpPose(in TargetPose fromPose, in TargetPose toPose, float weight, out TargetPose result)
        {
            result = new TargetPose
            {
                localPosition = math.lerp(fromPose.localPosition, toPose.localPosition, weight),
                rotationZ = math.lerp(fromPose.rotationZ, toPose.rotationZ, weight),
                scale = math.lerp(fromPose.scale, toPose.scale, weight),
                sliceIndex = weight < 0.5f ? fromPose.sliceIndex : toPose.sliceIndex,
                atlasRect = weight < 0.5f ? fromPose.atlasRect : toPose.atlasRect
            };
        }

        /// <summary>
        /// Composites all playback layers for one target, bottom-up — lowest layer index first,
        /// upper layers composite later and win contested channels (architecture section 5.6).
        /// Inactive layers are skipped. Per active layer: the current clip's tracks apply onto the
        /// incoming pose; while blending, the previous clip's tracks apply onto the same incoming
        /// pose independently and the two results lerp by
        /// <c>blendElapsed / blendDuration</c> before compositing continues. A fading-out layer
        /// with no current clip (<see cref="PlaybackLayer.clipIndex"/> = −1) lerps toward the
        /// incoming pose. Each clip's time maps through the loop mode it is actually playing
        /// under — the current clip through <see cref="PlaybackLayer.loop"/>, the crossfade source
        /// through <see cref="PlaybackLayer.previousLoop"/>, which is captured when the clip is
        /// demoted precisely so the outgoing side does not fade out under the incoming request's
        /// mode.
        /// </summary>
        /// <param name="registry">The actor's baked clip registry.</param>
        /// <param name="layers">The actor's playback layers (buffer index = layer index); pass a <c>DynamicBuffer</c> via <c>AsNativeArray()</c>.</param>
        /// <param name="targetIndex">The part's dense target index.</param>
        /// <param name="restPose">The part's rest pose.</param>
        /// <param name="snapBlendWeights">
        /// True to render every crossfade as a hard cut — LOD 2's behaviour (architecture section
        /// 5.10). The weight snaps; nothing here touches <see cref="PlaybackLayer.blendElapsed"/>,
        /// so a layer that changes LOD mid-blend rejoins the true weight rather than restarting.
        /// There is deliberately no overload defaulting this to false: one production caller exists
        /// and a silent default is how half the callers would stop honouring LOD.
        /// </param>
        /// <param name="pose">The composited output pose.</param>
        [BurstCompile]
        public static void CompositeLayers(
            ref ClipRegistryBlob registry,
            in NativeArray<PlaybackLayer> layers,
            int targetIndex,
            in TargetRestPose restPose,
            bool snapBlendWeights,
            out TargetPose pose)
        {
            RestToPose(in restPose, out pose);

            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                PlaybackLayer layer = layers[layerIndex];
                if ((layer.flags & PlaybackFlags.Active) == 0)
                {
                    continue;
                }

                TargetPose currentPose = pose;
                if (layer.clipIndex >= 0 && layer.clipIndex < registry.clips.Length)
                {
                    ref ClipBlob currentClip = ref registry.clips[layer.clipIndex];
                    LoopMode currentLoopMode = ResolveLoopMode(layer.loop, currentClip.defaultLoop);
                    float currentNormalizedTime = MapTimeNormalized(layer.time, currentClip.duration, currentLoopMode);
                    ApplyClipToPose(ref currentClip, targetIndex, currentNormalizedTime, in restPose, ref currentPose);
                }

                bool isBlending = (layer.flags & PlaybackFlags.Blending) != 0 && layer.blendDuration > 0f;
                if (isBlending)
                {
                    TargetPose previousPose = pose;
                    if (layer.previousClipIndex >= 0 && layer.previousClipIndex < registry.clips.Length)
                    {
                        ref ClipBlob previousClip = ref registry.clips[layer.previousClipIndex];
                        // The outgoing clip keeps the loop mode it was actually playing under: a
                        // command may have overridden it away from the clip's default, and wrapping
                        // a clip that was told to hold would pop mid-crossfade.
                        LoopMode previousLoopMode = ResolveLoopMode(layer.previousLoop, previousClip.defaultLoop);
                        float previousNormalizedTime = MapTimeNormalized(
                            layer.previousTime, previousClip.duration, previousLoopMode);
                        ApplyClipToPose(
                            ref previousClip, targetIndex, previousNormalizedTime, in restPose, ref previousPose);
                    }
                    float blendWeight = math.saturate(layer.blendElapsed / layer.blendDuration);
                    if (snapBlendWeights)
                    {
                        blendWeight = AnimationLodPolicy.SnapBlendWeight(blendWeight);
                    }
                    LerpPose(in previousPose, in currentPose, blendWeight, out pose);
                }
                else
                {
                    pose = currentPose;
                }
            }
        }

        /// <summary>
        /// The phase-offset sample frame index behind sample-rate quantization
        /// (architecture section 5.6): <c>floor(elapsedTime × rateHz + phase01)</c>, algebraically
        /// identical to the documented <c>floor((elapsedTime + phase01 / rateHz) × rateHz)</c>.
        /// </summary>
        /// <param name="elapsedTime">World elapsed time in seconds.</param>
        /// <param name="rateHz">Sample rate in Hz; must be positive.</param>
        /// <param name="phase01">Per-entity phase offset in [0, 1).</param>
        /// <returns>The sample frame index at the given time.</returns>
        [BurstCompile]
        public static long SampleFrameIndex(float elapsedTime, float rateHz, float phase01)
        {
            return (long)math.floor((double)elapsedTime * rateHz + phase01);
        }

        /// <summary>
        /// Whether a quantized actor samples this frame (architecture section 5.6): true when the
        /// phase-offset sample frame index advanced between the two elapsed times, or always when
        /// <paramref name="rateHz"/> is 0 or negative (0 = sample every frame). Per-entity phase
        /// spreads crowd sampling across frames; playback time itself is never quantized.
        /// </summary>
        /// <param name="previousElapsedTime">World elapsed time at the previous frame, in seconds.</param>
        /// <param name="currentElapsedTime">World elapsed time at this frame, in seconds.</param>
        /// <param name="rateHz">Sample rate in Hz; 0 or negative = sample every frame.</param>
        /// <param name="phase01">Per-entity phase offset in [0, 1).</param>
        /// <returns>True when the actor should sample this frame.</returns>
        [BurstCompile]
        public static bool ShouldSample(float previousElapsedTime, float currentElapsedTime, float rateHz, float phase01)
        {
            if (rateHz <= 0f)
            {
                return true;
            }
            return SampleFrameIndex(currentElapsedTime, rateHz, phase01)
                != SampleFrameIndex(previousElapsedTime, rateHz, phase01);
        }

        private static float PositiveModulo(float value, float modulus)
        {
            float remainder = math.fmod(value, modulus);
            return remainder < 0f ? remainder + modulus : remainder;
        }

        private static void FindKeySegment(
            ref BlobArray<TransformKeyBlob> keys,
            float normalizedTime,
            out int previousIndex,
            out int nextIndex)
        {
            previousIndex = 0;
            nextIndex = 0;
            for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                if (keys[keyIndex].normalizedTime <= normalizedTime)
                {
                    previousIndex = keyIndex;
                }
                if (keys[keyIndex].normalizedTime >= normalizedTime)
                {
                    nextIndex = keyIndex;
                    return;
                }
                nextIndex = keyIndex;
            }
        }

        private static void FindSpriteKeySegment(
            ref BlobArray<SpriteKeyBlob> keys,
            float normalizedTime,
            out int previousIndex,
            out int nextIndex)
        {
            previousIndex = 0;
            nextIndex = 0;
            for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                if (keys[keyIndex].normalizedTime <= normalizedTime)
                {
                    previousIndex = keyIndex;
                }
                if (keys[keyIndex].normalizedTime >= normalizedTime)
                {
                    nextIndex = keyIndex;
                    return;
                }
                nextIndex = keyIndex;
            }
        }
    }
}
