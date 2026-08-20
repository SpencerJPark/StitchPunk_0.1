// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
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
        /// Applies the per-key easing curve, including <see cref="Interpolation.Bezier"/>, which
        /// needs the key's handles and so cannot be served by the parameterless overload.
        /// </summary>
        /// <remarks>
        /// Kept as an overload rather than replacing <see cref="Ease(float, Interpolation)"/>,
        /// because that signature is the one the four fixed curves need and callers that never
        /// author Bézier should not have to carry handles they do not use.
        /// </remarks>
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

        /// <summary>
        /// Evaluates a cubic Bézier ease with endpoints pinned at (0,0) and (1,1).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The curve is parametric, so finding the weight for a time means solving x(s) = t for the
        /// parameter s first and then reading y(s). Newton's method converges in a handful of
        /// iterations for the monotonic-x curves validation rule V17 permits; the bisection fallback
        /// covers the flat-derivative case Newton cannot make progress on, so the loop always
        /// terminates with a bounded error rather than sometimes not terminating.
        /// </para>
        /// <para>
        /// Two zero handles mean "these fields did not exist when this asset was written", not "a
        /// curve that collapses to the origin". Reading that as linear is what stops an old clip,
        /// or a key switched to Bézier by something that did not initialise it, from freezing at
        /// the segment's left key.
        /// </para>
        /// </remarks>
        [BurstCompile]
        public static float EaseBezier(
            float linearTime, in float2 bezierStartHandle, in float2 bezierEndHandle)
        {
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

        /// <summary>One axis of a cubic Bézier with endpoints pinned at 0 and 1.</summary>
        [BurstCompile]
        private static float CubicBezierComponent(float parameter, float firstHandle, float secondHandle)
        {
            float inverse = 1f - parameter;
            return 3f * inverse * inverse * parameter * firstHandle
                + 3f * inverse * parameter * parameter * secondHandle
                + parameter * parameter * parameter;
        }

        /// <summary>Derivative of <see cref="CubicBezierComponent"/> with respect to the parameter.</summary>
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
        /// <param name="rotation">Sampled Euler rotation in radians.</param>
        /// <param name="scale">Sampled non-uniform x/y/z scale.</param>
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
        /// Samples one sprite track at a normalized time (architecture section 5.7): the key at or
        /// before the time holds until the next key's time is reached, with slice index −1 meaning
        /// "no change" (host convention absorbed). Slice mode writes <paramref name="sliceIndex"/>;
        /// atlas mode writes <paramref name="atlasRect"/>; an empty track writes nothing.
        /// </summary>
        /// <param name="track">The track to sample.</param>
        /// <param name="normalizedTime">Sampling time normalized to the clip's duration.</param>
        /// <param name="sliceIndex">Current slice value; overwritten when the holding key selects a frame ≥ 0.</param>
        /// <param name="atlasRect">Current atlas rect; overwritten in atlas mode.</param>
        /// <summary>
        /// Applies the facing term to an already-composed slice and keeps the result inside the
        /// character's own variant block (architecture section 5.7, amendment A37).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The block is derived from <paramref name="restSliceIndex"/> rather than from the composed
        /// slice, because the rest slice is the one value that reliably names the character's
        /// variant — the composed slice may already have been moved by a relative key, and flooring
        /// *that* would let a large animation offset silently redefine which block the part belongs
        /// to.
        /// </para>
        /// <para>
        /// With <paramref name="framesPerVariant"/> at or below 1 there are no blocks, so the offset
        /// is a plain addition clamped at 0 — the lower clamp is what stops a negative relative key
        /// producing a negative slice, which is the fifth route to a negative index that A37 opened
        /// and section 5.7's original "no guard needed" argument did not anticipate.
        /// </para>
        /// </remarks>
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

            // The key at or before the time wins outright, and holds until the next key's own time
            // is reached. A frame index is not a quantity that can be part-way between two values,
            // so the change has to land on the key the author placed — a midpoint crossover put it
            // half a segment early or late, and on an evenly spaced flipbook that reads as the whole
            // animation running offset from its own timeline.
            int chosenIndex = FindHoldingSpriteKey(ref keys, normalizedTime);

            if (track.mode == SpriteFrameMode.Slice)
            {
                // Two independent bases compose here, and the order matters. The key's own mode
                // resolves it against the track's authored baseIndex first, producing the track's
                // value; sliceSpace then decides whether that value replaces the pose's slice or is
                // added to the rest slice the character's variant chose. Collapsing them would cost
                // one of the two retargeting behaviours: an authored base that moves a whole track
                // onto another span of the array, and a runtime base that follows the character.
                int trackValue = SpriteIndexResolver.Resolve(
                    keys[chosenIndex].sliceIndex, keys[chosenIndex].indexMode, track.baseIndex);

                if (track.sliceSpace == SpriteSliceSpace.RelativeToRest)
                {
                    // Amendment A37: the key is an offset from whatever the seed carries, which is
                    // the rest slice the host's design system chose for this character. There is no
                    // -1 sentinel here — 0 is the no-op, and a negative offset is a legitimate step
                    // backwards through the variant's frames.
                    sliceIndex += trackValue;
                }
                else if (trackValue >= 0
                    || keys[chosenIndex].indexMode == SpriteIndexMode.RelativeToBase)
                {
                    // The −1 sentinel belongs to absolute keys only. A relative key that resolves
                    // below zero is a base and offset that disagree, not a request to hold the
                    // current frame — validation rule V18 reports it, and clamping keeps the
                    // material on a renderable slice meanwhile.
                    sliceIndex = math.max(0, trackValue);
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
                rotation = restPose.rotation,
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
                    out float3 sampledRotation,
                    out float3 sampledScale);

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
                rotation = math.lerp(fromPose.rotation, toPose.rotation, weight),
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

        /// <summary>
        /// The key holding a sprite track at a time: the last one at or before it.
        /// </summary>
        /// <remarks>
        /// A single index rather than a surrounding pair, because a flipbook has nothing to
        /// interpolate — the key that has most recently fired is the whole answer. Before the first
        /// key that is the first key, which holds frame 0's value rather than showing nothing.
        /// </remarks>
        /// <summary>
        /// Samples one billboard track's three channels at a time (amendment A44).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Two channels interpolate and one does not.</strong> The angle offset and the
        /// blend weight are continuous values being approximated between keys, so they ease. The
        /// enable flag is a discrete instruction that fires at a moment, so it is <em>held</em> from
        /// the last key at or before the time — amendment A43's rule for flipbook indices, applied
        /// to the other channel in this package that is an instruction rather than an approximation.
        /// </para>
        /// <para>
        /// An empty track resolves to the neutral values a root with no track has: no extra offset,
        /// full blend, enabled. That is what makes adding an empty track a no-op rather than a
        /// silent disabling.
        /// </para>
        /// </remarks>
        /// <param name="track">The track to sample.</param>
        /// <param name="normalizedTime">Sampling time normalized to the clip's duration.</param>
        /// <param name="angleOffsetRadians">Sampled rotation off the resolved facing.</param>
        /// <param name="blendWeight">Sampled blend against the animated pose.</param>
        /// <param name="enabled">Whether the root billboards at this time.</param>
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

        /// <summary>
        /// The keys surrounding a time on a billboard track. Mirrors <c>FindKeySegment</c>; the two
        /// cannot share code because a <c>BlobArray</c> of one key type is a different type from a
        /// <c>BlobArray</c> of another and neither is generic.
        /// </summary>
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
