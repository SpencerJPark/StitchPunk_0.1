// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The package's single sampler: pure, allocation-free, Burst-compatible functions for easing,
    /// loop-mode time mapping, track sampling, layer composition, and sample-rate quantization.
    /// Runtime jobs, tests, and the editor preview all call these same functions.
    /// </summary>
    [BurstCompile]
    public static class ClipSampler
    {
        public static readonly float4 IdentityAtlasRect = new float4(1f, 1f, 0f, 0f); // full texture; used until an atlas-mode sprite track writes a frame

        /// <returns>The eased blend weight toward the segment's right key; <see cref="Interpolation.Step"/> returns 0.</returns>
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

        /// <summary>Overload for <see cref="Interpolation.Bezier"/>, which needs the key's handles.</summary>
        [BurstCompile]
        public static float Ease(
            float linearTime, Interpolation interpolation,
            in float2 bezierStartHandle, in float2 bezierEndHandle)
        {
            if (interpolation != Interpolation.Bezier)
            {
                return Ease(linearTime, interpolation);
            }
            return EaseBezier(linearTime, in bezierStartHandle, in bezierEndHandle);
        }

        /// <summary>Cubic Bézier ease with endpoints pinned at (0,0) and (1,1).</summary>
        [BurstCompile]
        public static float EaseBezier(
            float linearTime, in float2 bezierStartHandle, in float2 bezierEndHandle)
        {
            // Two zero handles mean these fields predate this asset's schema, not a curve
            // collapsing to the origin; treating that as linear stops an old or uninitialized key
            // from freezing at the segment's left value.
            if (math.all(bezierStartHandle == float2.zero) && math.all(bezierEndHandle == float2.zero))
            {
                return linearTime;
            }

            if (linearTime <= 0f)
            {
                return 0f;
            }
            if (linearTime >= 1f)
            {
                return 1f;
            }

            float parameter = SolveBezierParameterForTime(
                linearTime, bezierStartHandle.x, bezierEndHandle.x);
            return CubicBezierComponent(parameter, bezierStartHandle.y, bezierEndHandle.y);
        }

        [BurstCompile]
        private static float CubicBezierComponent(float parameter, float firstHandle, float secondHandle)
        {
            float inverse = 1f - parameter;
            return 3f * inverse * inverse * parameter * firstHandle
                + 3f * inverse * parameter * parameter * secondHandle
                + parameter * parameter * parameter;
        }

        [BurstCompile]
        private static float CubicBezierDerivative(
            float parameter, float firstHandle, float secondHandle)
        {
            float inverse = 1f - parameter;
            return 3f * inverse * inverse * firstHandle
                + 6f * inverse * parameter * (secondHandle - firstHandle)
                + 3f * parameter * parameter * (1f - secondHandle);
        }

        [BurstCompile]
        private static float SolveBezierParameterForTime(
            float targetTime, float firstHandleX, float secondHandleX)
        {
            const float SolveTolerance = 1e-5f;
            const int NewtonIterations = 8;
            const int BisectionIterations = 24;

            float parameter = targetTime;
            for (int iteration = 0; iteration < NewtonIterations; iteration++)
            {
                float error = CubicBezierComponent(parameter, firstHandleX, secondHandleX) - targetTime;
                if (math.abs(error) < SolveTolerance)
                {
                    return parameter;
                }

                float derivative = CubicBezierDerivative(parameter, firstHandleX, secondHandleX);
                if (math.abs(derivative) < 1e-6f)
                {
                    break;
                }
                parameter -= error / derivative;
            }

            // Newton stalled on a near-flat stretch. Bisection cannot stall, so the solve is bounded
            // rather than merely usually fast.
            float lowerBound = 0f;
            float upperBound = 1f;
            parameter = targetTime;
            for (int iteration = 0; iteration < BisectionIterations; iteration++)
            {
                float sampledTime = CubicBezierComponent(parameter, firstHandleX, secondHandleX);
                if (math.abs(sampledTime - targetTime) < SolveTolerance)
                {
                    break;
                }
                if (sampledTime < targetTime)
                {
                    lowerBound = parameter;
                }
                else
                {
                    upperBound = parameter;
                }
                parameter = (lowerBound + upperBound) * 0.5f;
            }
            return parameter;
        }

        [BurstCompile]
        public static LoopMode ResolveLoopMode(LoopMode requested, LoopMode clipDefault)
        {
            return requested == LoopMode.UseClipDefault ? clipDefault : requested;
        }

        /// <summary>
        /// Maps un-wrapped playback time onto [0, duration] per loop mode: Once clamps; Loop wraps
        /// (negative times wrap too, supporting reverse playback); PingPong reflects. An unresolved
        /// <see cref="LoopMode.UseClipDefault"/> clamps defensively — callers resolve it first via <see cref="ResolveLoopMode"/>.
        /// </summary>
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

        /// <returns><see cref="MapTime"/> divided by duration, in [0, 1]; 0 when duration is not positive.</returns>
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
        /// Samples one transform track at a normalized time. Times before the first key or after
        /// the last clamp to it; a single-key track returns that key always. The left key's
        /// <see cref="Interpolation"/> drives the segment; an empty track returns the neutral pose.
        /// </summary>
        [BurstCompile]
        public static void SampleTransformTrack(
            ref TransformTrackBlob track,
            float normalizedTime,
            out float3 position,
            out float3 rotation,
            out float3 scale)
        {
            ref BlobArray<TransformKeyBlob> keys = ref track.keys;
            if (keys.Length == 0)
            {
                position = float3.zero;
                rotation = float3.zero;
                scale = new float3(1f, 1f, 1f);
                return;
            }

            FindKeySegment(ref keys, normalizedTime, out int previousIndex, out int nextIndex);
            ref TransformKeyBlob previousKey = ref keys[previousIndex];
            ref TransformKeyBlob nextKey = ref keys[nextIndex];

            if (previousIndex == nextIndex || previousKey.interpolation == Interpolation.Step)
            {
                position = previousKey.position;
                rotation = previousKey.rotation;
                scale = previousKey.scale;
                return;
            }

            float keySpan = nextKey.normalizedTime - previousKey.normalizedTime;
            float linearWeight = keySpan > 0f ? (normalizedTime - previousKey.normalizedTime) / keySpan : 0f;
            float easedWeight = Ease(
                linearWeight, previousKey.interpolation,
                in previousKey.bezierStartHandle, in previousKey.bezierEndHandle);

            position = math.lerp(previousKey.position, nextKey.position, easedWeight);

            // Euler angles are lerped per component, which is how a keyed rotation curve behaves
            // everywhere an author has met one. Slerping a quaternion built from them would take a
            // different path between the same two keys and quietly disagree with the curve editor.
            rotation = math.lerp(previousKey.rotation, nextKey.rotation, easedWeight);
            scale = math.lerp(previousKey.scale, nextKey.scale, easedWeight);
        }

        /// <summary>
        /// Applies the facing term to an already-composed slice and keeps the result inside the
        /// character's own variant block.
        /// </summary>
        /// <param name="composedSlice">The slice after clip composition.</param>
        /// <param name="restSliceIndex">The part's rest slice — which variant this character has.</param>
        /// <param name="viewOffset">Frames to step for the direction the part faces.</param>
        /// <param name="framesPerVariant">Frames one variant owns; 1 or less means no blocks.</param>
        /// <returns>The final, non-negative slice index.</returns>
        [BurstCompile]
        public static int ResolveViewSlice(
            int composedSlice,
            int restSliceIndex,
            int viewOffset,
            int framesPerVariant)
        {
            if (framesPerVariant <= 1)
            {
                return math.max(0, composedSlice + viewOffset);
            }

            // The block is derived from restSliceIndex, not the composed slice: the composed slice
            // may already have moved via a relative key, and flooring that would let a large
            // animation offset silently redefine which block the part belongs to.
            int blockBase = (restSliceIndex / framesPerVariant) * framesPerVariant;
            int frameInBlock = composedSlice - blockBase + viewOffset;

            // Positive modulo: C#'s % keeps the sign of the dividend, so a part facing "one back"
            // from the first frame of its block would land outside it.
            int wrapped = frameInBlock % framesPerVariant;
            if (wrapped < 0)
            {
                wrapped += framesPerVariant;
            }
            return math.max(0, blockBase + wrapped);
        }

        /// <summary>
        /// Samples one sprite track at a normalized time: the key at or before the time holds until
        /// the next key's time, with slice index -1 meaning "no change". Slice mode writes
        /// <paramref name="sliceIndex"/>; atlas mode writes <paramref name="atlasRect"/>; an empty track writes nothing.
        /// </summary>
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

            // The key at or before the time wins outright and holds until the next key's own time
            // is reached — a frame index cannot be part-way between two values, so a midpoint
            // crossover would read as the whole animation running offset from its own timeline.
            int chosenIndex = FindHoldingSpriteKey(ref keys, normalizedTime);

            if (track.mode == SpriteFrameMode.Slice)
            {
                // Two independent bases compose here, in order: the key's own mode resolves it
                // against the track's authored baseIndex first, then sliceSpace decides whether that
                // value replaces the pose's slice or is added to the rest slice the variant chose.
                int trackValue = SpriteIndexResolver.Resolve(
                    keys[chosenIndex].sliceIndex, keys[chosenIndex].indexMode, track.baseIndex);

                if (track.sliceSpace == SpriteSliceSpace.RelativeToRest)
                {
                    // No -1 sentinel here: 0 is the no-op, and a negative offset is a legitimate step
                    // backwards through the variant's frames.
                    sliceIndex += trackValue;
                }
                else if (trackValue >= 0
                    || keys[chosenIndex].indexMode == SpriteIndexMode.RelativeToBase)
                {
                    // -1 belongs to absolute keys only. A relative key resolving below zero is a
                    // base/offset disagreement, not a "hold the current frame" request; clamped to
                    // stay on a renderable slice.
                    sliceIndex = math.max(0, trackValue);
                }
            }
            else
            {
                atlasRect = keys[chosenIndex].atlasRect;
            }
        }

        [BurstCompile]
        public static void RestToPose(in TargetRestPose restPose, out TargetPose pose)
        {
            pose = new TargetPose
            {
                localPosition = restPose.localPosition,
                rotation = restPose.rotation,
                scale = restPose.scale,
                sliceIndex = restPose.restSliceIndex,
                atlasRect = IdentityAtlasRect
            };
        }

        /// <summary>
        /// Applies every track of one clip bound to a target onto an existing pose, in canonical
        /// order. Keys are always deltas, never absolute values: <see cref="TrackBlendOp.Override"/>
        /// anchors to <paramref name="restPose"/> (masked channels become rest + key), while
        /// <see cref="TrackBlendOp.Additive"/> anchors to the incoming composited pose. Channels
        /// outside a track's mask are left exactly as the layers below composited them.
        /// </summary>
        /// <param name="restPose">The frame Override keys are offsets from — needed because an Override must reach past every lower layer's contribution.</param>
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
                    out float3 sampledRotation,
                    out float3 sampledScale);

                // The only difference between the two ops is the frame the key is added to: the
                // rest pose for Override, the composited-so-far pose for Additive. Both treat the
                // key as a delta.
                bool isAdditive = track.blendOp == TrackBlendOp.Additive;
                if ((track.channels & AnimatedChannels.PositionXY) != 0)
                {
                    float2 positionAnchorXY = isAdditive
                        ? pose.localPosition.xy
                        : restPose.localPosition.xy;
                    pose.localPosition.x = positionAnchorXY.x + sampledPosition.x;
                    pose.localPosition.y = positionAnchorXY.y + sampledPosition.y;
                }
                if ((track.channels & AnimatedChannels.PositionZ) != 0)
                {
                    float layerZAnchor = isAdditive ? pose.localPosition.z : restPose.localPosition.z;
                    pose.localPosition.z = layerZAnchor + sampledPosition.z;
                }
                if ((track.channels & AnimatedChannels.Rotation) != 0)
                {
                    float3 rotationAnchor = isAdditive ? pose.rotation : restPose.rotation;
                    pose.rotation = rotationAnchor + sampledRotation;
                }
                if ((track.channels & AnimatedChannels.Scale) != 0)
                {
                    // Scale composes multiplicatively, so its identity is 1 and its "delta" is a
                    // factor — an unkeyed scale curve authored at 1 leaves the rest scale alone.
                    float3 scaleAnchor = isAdditive ? pose.scale : restPose.scale;
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

        /// <summary>Samples one clip for one target starting from the rest pose — the single-clip entry point shared by the editor preview and tests.</summary>
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

        /// <summary>Lerps two sampled poses by a blend weight: position/rotation/scale interpolate linearly; sprite frames never blend — the nearest pose wins at the midpoint.</summary>
        /// <param name="fromPose">The pose at weight 0 (the blend's "previous" side).</param>
        /// <param name="toPose">The pose at weight 1 (the blend's "current" side).</param>
        [BurstCompile]
        public static void LerpPose(in TargetPose fromPose, in TargetPose toPose, float weight, out TargetPose result)
        {
            result = new TargetPose
            {
                localPosition = math.lerp(fromPose.localPosition, toPose.localPosition, weight),
                rotation = math.lerp(fromPose.rotation, toPose.rotation, weight),
                scale = math.lerp(fromPose.scale, toPose.scale, weight),
                sliceIndex = weight < 0.5f ? fromPose.sliceIndex : toPose.sliceIndex,
                atlasRect = weight < 0.5f ? fromPose.atlasRect : toPose.atlasRect
            };
        }

        /// <summary>
        /// Composites all playback layers for one target, bottom-up: lowest layer index first,
        /// upper layers win contested channels. Inactive layers are skipped. While blending, the
        /// previous clip's tracks sample independently and lerp against the current clip's by
        /// <c>blendElapsed / blendDuration</c>. The crossfade source maps time through
        /// <see cref="PlaybackLayer.previousLoop"/> rather than the current request's loop mode,
        /// since it was captured when the clip was demoted — otherwise the outgoing side could pop
        /// mid-crossfade if a command had overridden its loop away from its own default.
        /// </summary>
        /// <param name="layers">The actor's playback layers (buffer index = layer index); pass a <c>DynamicBuffer</c> via <c>AsNativeArray()</c>.</param>
        /// <param name="snapBlendWeights">
        /// True to render every crossfade as a hard cut (LOD 2). Only the weight snaps —
        /// <see cref="PlaybackLayer.blendElapsed"/> is untouched, so a layer that changes LOD
        /// mid-blend rejoins the true weight. No overload defaults this to false, deliberately.
        /// </param>
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
                        blendWeight = AnimationLodResolver.SnapBlendWeight(blendWeight);
                    }
                    LerpPose(in previousPose, in currentPose, blendWeight, out pose);
                }
                else
                {
                    pose = currentPose;
                }
            }
        }

        [BurstCompile]
        public static long SampleFrameIndex(float elapsedTime, float rateHz, float phase01)
        {
            return (long)math.floor((double)elapsedTime * rateHz + phase01);
        }

        /// <summary>
        /// Whether a quantized actor samples this frame: true when the phase-offset sample frame
        /// index advanced between the two elapsed times, or always when <paramref name="rateHz"/>
        /// is 0 or negative. Playback time itself is never quantized, only sampling frequency.
        /// </summary>
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

        /// <summary>
        /// Samples one billboard track's three channels at a time. The angle offset and blend
        /// weight interpolate; the enable flag is a discrete instruction and is held from the last
        /// key at or before the time, never eased. An empty track resolves to the neutral values a
        /// root with no track has, so adding one is a no-op rather than a silent disable.
        /// </summary>
        [BurstCompile]
        public static void SampleBillboardTrack(
            ref BillboardTrackBlob track,
            float normalizedTime,
            out float angleOffsetRadians,
            out float blendWeight,
            out bool enabled)
        {
            ref BlobArray<BillboardKeyBlob> keys = ref track.keys;
            if (keys.Length == 0)
            {
                angleOffsetRadians = 0f;
                blendWeight = 1f;
                enabled = true;
                return;
            }

            FindBillboardKeySegment(ref keys, normalizedTime, out int previousIndex, out int nextIndex);
            ref BillboardKeyBlob previousKey = ref keys[previousIndex];
            ref BillboardKeyBlob nextKey = ref keys[nextIndex];

            // Held from its own key in every case, easing or not — the flag never blends.
            enabled = previousKey.enabled;

            if (previousIndex == nextIndex || previousKey.interpolation == Interpolation.Step)
            {
                angleOffsetRadians = previousKey.angleOffsetRadians;
                blendWeight = previousKey.blendWeight;
                return;
            }

            float keySpan = nextKey.normalizedTime - previousKey.normalizedTime;
            float linearWeight = keySpan > 0f
                ? (normalizedTime - previousKey.normalizedTime) / keySpan
                : 0f;
            float easedWeight = Ease(
                linearWeight, previousKey.interpolation,
                in previousKey.bezierStartHandle, in previousKey.bezierEndHandle);

            angleOffsetRadians =
                math.lerp(previousKey.angleOffsetRadians, nextKey.angleOffsetRadians, easedWeight);
            blendWeight = math.lerp(previousKey.blendWeight, nextKey.blendWeight, easedWeight);
        }

        // Mirrors FindKeySegment; the two cannot share code because a BlobArray of one key type is
        // a different type from a BlobArray of another and neither is generic.
        private static void FindBillboardKeySegment(
            ref BlobArray<BillboardKeyBlob> keys,
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

        // A single index, not a surrounding pair: a flipbook has nothing to interpolate, so the
        // most recently fired key is the whole answer. Before the first key, that key still holds
        // (frame 0's value), rather than showing nothing.
        private static int FindHoldingSpriteKey(
            ref BlobArray<SpriteKeyBlob> keys, float normalizedTime)
        {
            int holdingIndex = 0;
            for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                if (keys[keyIndex].normalizedTime > normalizedTime)
                {
                    break;
                }
                holdingIndex = keyIndex;
            }
            return holdingIndex;
        }
    }
}
