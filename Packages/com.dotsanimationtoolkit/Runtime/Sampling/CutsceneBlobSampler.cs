// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Evaluates baked <see cref="CutsceneTransformKeyBlob"/>/<see cref="CutsceneCameraKeyBlob"/>
    /// arrays at a segment-relative second, for the runtime player (Phase G §6). The Burst-jobbable
    /// twin of <c>CutsceneKeySampler</c> (Authoring) — same math, reusing <see cref="ClipSampler.Ease"/>
    /// for the same reason that one does, but written against <see cref="BlobArray{T}"/> rather than
    /// <c>List&lt;T&gt;</c> so it can run inside a per-frame job.
    /// </summary>
    [BurstCompile]
    public static class CutsceneBlobSampler
    {
        /// <summary>
        /// Samples a transform key array. Holds the nearest key outside the authored range.
        /// Rotation is per-component Euler lerp, never a quaternion slerp — the same choice
        /// <c>ClipSampler</c> makes for clip transform tracks (its own remarks: slerping would take
        /// a different path between the same two keys and quietly disagree with the curve editor).
        /// <paramref name="rotation"/> is radians, the same convention as <see cref="TargetPose.rotation"/>
        /// and <see cref="TargetRestPose.rotation"/>; a caller that needs a quaternion (a root's own
        /// <see cref="Unity.Transforms.LocalTransform.Rotation"/>) converts once, at the point it
        /// actually applies the pose.
        /// </summary>
        /// <returns>
        /// False when <paramref name="keys"/> is empty (amendment A62 defect 2) — every out
        /// parameter is the identity pose, and the caller must leave the target's current transform
        /// alone rather than writing it, or an unkeyed slot snaps to the world origin every frame.
        /// </returns>
        [BurstCompile]
        public static bool TrySampleTransform(
            ref BlobArray<CutsceneTransformKeyBlob> keys, float time,
            out float3 position, out float3 rotation, out float3 scale)
        {
            int count = keys.Length;
            if (count == 0)
            {
                position = float3.zero;
                rotation = float3.zero;
                scale = new float3(1f, 1f, 1f);
                return false;
            }

            if (count == 1 || time <= keys[0].time)
            {
                ToOut(in keys[0], out position, out rotation, out scale);
                return true;
            }

            int lastIndex = count - 1;
            if (time >= keys[lastIndex].time)
            {
                ToOut(in keys[lastIndex], out position, out rotation, out scale);
                return true;
            }

            int segmentStart = 0;
            for (int i = 0; i < lastIndex; i++)
            {
                if (keys[i].time <= time && time < keys[i + 1].time)
                {
                    segmentStart = i;
                    break;
                }
            }

            CutsceneTransformKeyBlob fromKey = keys[segmentStart];
            CutsceneTransformKeyBlob toKey = keys[segmentStart + 1];
            float span = toKey.time - fromKey.time;
            float linearTime = span > 0f ? math.saturate((time - fromKey.time) / span) : 1f;
            float easedTime = ClipSampler.Ease(
                linearTime, fromKey.interpolation, fromKey.bezierStartHandle, fromKey.bezierEndHandle);

            position = math.lerp(fromKey.position, toKey.position, easedTime);
            rotation = math.lerp(fromKey.rotation, toKey.rotation, easedTime);
            scale = math.lerp(fromKey.scale, toKey.scale, easedTime);
            return true;
        }

        /// <summary>
        /// Samples the camera lane the way it plays (decision G-D7): a cut marker splits the lane
        /// into independent interpolation windows rather than blending across it.
        /// </summary>
        [BurstCompile]
        public static void SampleCamera(
            ref BlobArray<CutsceneCameraKeyBlob> keys, ref BlobArray<float> cutTimes, float time,
            out float3 position, out quaternion rotation, out float fieldOfView, out bool isCut)
        {
            float windowStart = 0f;
            float windowEnd = float.MaxValue;
            isCut = false;
            const float CutEpsilon = 1f / 60f;
            for (int i = 0; i < cutTimes.Length; i++)
            {
                float cutTime = cutTimes[i];
                if (math.abs(cutTime - time) <= CutEpsilon)
                {
                    isCut = true;
                }
                if (cutTime <= time && cutTime > windowStart)
                {
                    windowStart = cutTime;
                }
                if (cutTime > time && cutTime < windowEnd)
                {
                    windowEnd = cutTime;
                }
            }

            int firstIndexInWindow = -1;
            int lastIndexInWindow = -1;
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].time >= windowStart && keys[i].time < windowEnd)
                {
                    if (firstIndexInWindow < 0)
                    {
                        firstIndexInWindow = i;
                    }
                    lastIndexInWindow = i;
                }
            }

            if (firstIndexInWindow < 0)
            {
                // No key of its own in this window: hold whichever key most recently applied before
                // it opened, exactly as a clip holds its last frame past its own end.
                int holdIndex = -1;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i].time <= time)
                    {
                        holdIndex = i;
                    }
                }
                if (holdIndex < 0)
                {
                    position = float3.zero;
                    rotation = quaternion.identity;
                    fieldOfView = 60f;
                    return;
                }
                position = keys[holdIndex].position;
                rotation = quaternion.Euler(keys[holdIndex].rotation);
                fieldOfView = keys[holdIndex].fieldOfView;
                return;
            }

            if (firstIndexInWindow == lastIndexInWindow || time <= keys[firstIndexInWindow].time)
            {
                position = keys[firstIndexInWindow].position;
                rotation = quaternion.Euler(keys[firstIndexInWindow].rotation);
                fieldOfView = keys[firstIndexInWindow].fieldOfView;
                return;
            }
            if (time >= keys[lastIndexInWindow].time)
            {
                position = keys[lastIndexInWindow].position;
                rotation = quaternion.Euler(keys[lastIndexInWindow].rotation);
                fieldOfView = keys[lastIndexInWindow].fieldOfView;
                return;
            }

            int segmentStart = firstIndexInWindow;
            for (int i = firstIndexInWindow; i < lastIndexInWindow; i++)
            {
                if (keys[i].time <= time && time < keys[i + 1].time)
                {
                    segmentStart = i;
                    break;
                }
            }
            CutsceneCameraKeyBlob fromKey = keys[segmentStart];
            CutsceneCameraKeyBlob toKey = keys[segmentStart + 1];
            float span = toKey.time - fromKey.time;
            float linearTime = span > 0f ? math.saturate((time - fromKey.time) / span) : 1f;
            float easedTime = ClipSampler.Ease(
                linearTime, fromKey.interpolation, fromKey.bezierStartHandle, fromKey.bezierEndHandle);

            position = math.lerp(fromKey.position, toKey.position, easedTime);
            // Per-component Euler lerp, converted once — matching SampleTransform's own choice and
            // ClipSampler's precedent, never a quaternion slerp between the two keys.
            rotation = quaternion.Euler(math.lerp(fromKey.rotation, toKey.rotation, easedTime));
            fieldOfView = math.lerp(fromKey.fieldOfView, toKey.fieldOfView, easedTime);
        }

        /// <summary>
        /// The facing angle in effect at <paramref name="time"/> (amendment A65 §3.2), in the
        /// <see cref="CutsceneFacing"/> model: the last override key at or before it, else the
        /// direction the root lane is travelling. The blob twin of
        /// <c>CutsceneKeySampler.TryResolveFacingAngle</c>.
        /// </summary>
        /// <returns>
        /// False when neither answer exists — no override key yet and a root lane that is not
        /// moving — and the caller must leave whatever facing is already in effect alone.
        /// </returns>
        [BurstCompile]
        public static bool TryResolveFacingAngle(
            ref BlobArray<CutsceneFacingKeyBlob> facingKeys,
            ref BlobArray<CutsceneTransformKeyBlob> rootKeys,
            float time,
            out float angleDegrees)
        {
            if (TryResolveFacingOverride(ref facingKeys, time, out angleDegrees))
            {
                return true;
            }
            return TryDeriveFacingFromRootTravel(ref rootKeys, time, out angleDegrees);
        }

        /// <summary>The last facing override key at or before <paramref name="time"/>.</summary>
        [BurstCompile]
        public static bool TryResolveFacingOverride(
            ref BlobArray<CutsceneFacingKeyBlob> facingKeys, float time, out float angleDegrees)
        {
            int bestIndex = -1;
            for (int keyIndex = 0; keyIndex < facingKeys.Length; keyIndex++)
            {
                if (facingKeys[keyIndex].time <= time &&
                    (bestIndex < 0 || facingKeys[keyIndex].time > facingKeys[bestIndex].time))
                {
                    bestIndex = keyIndex;
                }
            }
            if (bestIndex < 0)
            {
                angleDegrees = 0f;
                return false;
            }
            angleDegrees = math.degrees(facingKeys[bestIndex].angleRadians);
            return true;
        }

        /// <summary>
        /// The direction the root lane travels at <paramref name="time"/>, by finite difference
        /// against a hair earlier — the same movement vector a live actor would hand
        /// <c>FacingResolver.FromMovement</c>. Forward-differenced at <c>t == 0</c>, where there is
        /// no earlier sample to look back at.
        /// </summary>
        [BurstCompile]
        public static bool TryDeriveFacingFromRootTravel(
            ref BlobArray<CutsceneTransformKeyBlob> rootKeys, float time, out float angleDegrees)
        {
            const float LookBackSeconds = 1f / 60f;
            angleDegrees = 0f;
            if (rootKeys.Length == 0)
            {
                return false;
            }

            float earlierTime = time - LookBackSeconds;
            float laterTime = time;
            if (earlierTime < 0f)
            {
                earlierTime = 0f;
                laterTime = LookBackSeconds;
            }

            float3 earlierPosition;
            float3 laterPosition;
            float3 unusedRotation;
            float3 unusedScale;
            TrySampleTransform(ref rootKeys, earlierTime, out earlierPosition, out unusedRotation, out unusedScale);
            TrySampleTransform(ref rootKeys, laterTime, out laterPosition, out unusedRotation, out unusedScale);

            float3 travel = laterPosition - earlierPosition;
            if (math.lengthsq(travel) < 1e-8f)
            {
                return false;
            }

            angleDegrees = CutsceneFacingVariants.AngleDegreesFromTravel(in travel);
            return true;
        }

        private static void ToOut(
            in CutsceneTransformKeyBlob key, out float3 position, out float3 rotation, out float3 scale)
        {
            position = key.position;
            rotation = key.rotation;
            scale = key.scale;
        }
    }
}
