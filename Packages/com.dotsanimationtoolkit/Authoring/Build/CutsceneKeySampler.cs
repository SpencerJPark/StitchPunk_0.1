// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Evaluates a <see cref="CutsceneTransformKey"/>/<see cref="CutsceneCameraKey"/>/
    /// <see cref="CutsceneFacingKey"/> list at a raw timeline second — the one flat-list sampler
    /// shared by <see cref="CutsceneBlobBuilder"/> (baking boundary-continuity keys, amendment A62
    /// §3.1/§3.2) and the Scene-view preview (Phase G, G3). Moved here from the Editor assembly so
    /// the builder can call it too; the Editor assembly already sees <c>Authoring</c> internals.
    /// </summary>
    /// <remarks>
    /// Outputs raw authoring-space values (position, Euler degrees, scale) rather than
    /// <c>UnityEngine.Quaternion</c> — the builder needs them as-is to write synthetic
    /// <see cref="CutsceneTransformKey"/>/<see cref="CutsceneCameraKey"/> entries, and the editor
    /// preview (the only caller that wants a <c>Quaternion</c>) converts at its own call sites.
    /// </remarks>
    internal static class CutsceneKeySampler
    {
        /// <summary>
        /// Samples a transform key list at <paramref name="timeSeconds"/>. Holds the nearest key
        /// outside the authored range, exactly like a clip's own edge behaviour. Returns false when
        /// the list is null or empty, in which case every out parameter is the identity pose —
        /// callers must not write it unconditionally (amendment A62 defect 2).
        /// </summary>
        public static bool TrySampleTransform(
            List<CutsceneTransformKey> keys, float timeSeconds,
            out float3 position, out float3 eulerDegrees, out float3 scale)
        {
            if (keys == null || keys.Count == 0)
            {
                position = float3.zero;
                eulerDegrees = float3.zero;
                scale = new float3(1f, 1f, 1f);
                return false;
            }

            if (keys.Count == 1 || timeSeconds <= keys[0].time)
            {
                ToOut(keys[0], out position, out eulerDegrees, out scale);
                return true;
            }

            int lastIndex = keys.Count - 1;
            if (timeSeconds >= keys[lastIndex].time)
            {
                ToOut(keys[lastIndex], out position, out eulerDegrees, out scale);
                return true;
            }

            int segmentStart = 0;
            for (int i = 0; i < lastIndex; i++)
            {
                if (keys[i].time <= timeSeconds && timeSeconds < keys[i + 1].time)
                {
                    segmentStart = i;
                    break;
                }
            }

            CutsceneTransformKey fromKey = keys[segmentStart];
            CutsceneTransformKey toKey = keys[segmentStart + 1];
            float span = toKey.time - fromKey.time;
            float linearTime = span > 0f ? math.saturate((timeSeconds - fromKey.time) / span) : 1f;
            float easedTime = ClipSampler.Ease(
                linearTime, fromKey.interpolation, fromKey.bezierStartHandle, fromKey.bezierEndHandle);

            position = math.lerp(fromKey.position, toKey.position, easedTime);
            // Per-component Euler lerp, never a quaternion slerp — matching ClipSampler's own
            // rotation interpolation exactly (its remarks: slerping would take a different path
            // between the same two keys and quietly disagree with the curve editor).
            eulerDegrees = math.lerp(fromKey.rotation, toKey.rotation, easedTime);
            scale = math.lerp(fromKey.scale, toKey.scale, easedTime);
            return true;
        }

        /// <summary>Samples a camera key list the same way <see cref="TrySampleTransform"/> does, plus field of view.</summary>
        public static void SampleCamera(
            List<CutsceneCameraKey> keys, float timeSeconds,
            out float3 position, out float3 eulerDegrees, out float fieldOfView)
        {
            if (keys == null || keys.Count == 0)
            {
                position = float3.zero;
                eulerDegrees = float3.zero;
                fieldOfView = 60f;
                return;
            }

            if (keys.Count == 1 || timeSeconds <= keys[0].time)
            {
                position = keys[0].position;
                eulerDegrees = keys[0].rotation;
                fieldOfView = keys[0].fieldOfView;
                return;
            }

            int lastIndex = keys.Count - 1;
            if (timeSeconds >= keys[lastIndex].time)
            {
                position = keys[lastIndex].position;
                eulerDegrees = keys[lastIndex].rotation;
                fieldOfView = keys[lastIndex].fieldOfView;
                return;
            }

            int segmentStart = 0;
            for (int i = 0; i < lastIndex; i++)
            {
                if (keys[i].time <= timeSeconds && timeSeconds < keys[i + 1].time)
                {
                    segmentStart = i;
                    break;
                }
            }

            CutsceneCameraKey fromKey = keys[segmentStart];
            CutsceneCameraKey toKey = keys[segmentStart + 1];
            float span = toKey.time - fromKey.time;
            float linearTime = span > 0f ? math.saturate((timeSeconds - fromKey.time) / span) : 1f;
            float easedTime = ClipSampler.Ease(
                linearTime, fromKey.interpolation, fromKey.bezierStartHandle, fromKey.bezierEndHandle);

            position = math.lerp(fromKey.position, toKey.position, easedTime);
            eulerDegrees = math.lerp(fromKey.rotation, toKey.rotation, easedTime);
            fieldOfView = math.lerp(fromKey.fieldOfView, toKey.fieldOfView, easedTime);
        }

        /// <summary>
        /// Samples the camera lane the way it plays at runtime (decision G-D7): a cut marker splits
        /// the lane into independent interpolation windows, so a shot never blends across the cut it
        /// names as the exception to "one camera just moving around the scene" (spec §2). Windowing
        /// happens here rather than by pre-slicing the key list once, so the same list serves every
        /// call regardless of where the playhead currently sits.
        /// </summary>
        /// <param name="isCut">True when <paramref name="timeSeconds"/> sits inside one frame's width of a cut marker — informational, mirrored by the runtime player's <c>CutsceneCameraPose.isCut</c> (spec §6).</param>
        public static void SampleCameraWithCuts(
            List<CutsceneCameraKey> keys, List<CutsceneCameraCutMarker> cutMarkers, float timeSeconds,
            out float3 position, out float3 eulerDegrees, out float fieldOfView, out bool isCut)
        {
            float windowStart = 0f;
            float windowEnd = float.MaxValue;
            isCut = false;
            if (cutMarkers != null)
            {
                const float CutEpsilon = 1f / 60f;
                for (int i = 0; i < cutMarkers.Count; i++)
                {
                    float cutTime = cutMarkers[i].time;
                    if (math.abs(cutTime - timeSeconds) <= CutEpsilon)
                    {
                        isCut = true;
                    }
                    if (cutTime <= timeSeconds && cutTime > windowStart)
                    {
                        windowStart = cutTime;
                    }
                    if (cutTime > timeSeconds && cutTime < windowEnd)
                    {
                        windowEnd = cutTime;
                    }
                }
            }

            List<CutsceneCameraKey> windowedKeys = new List<CutsceneCameraKey>();
            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    if (keys[i].time >= windowStart && keys[i].time < windowEnd)
                    {
                        windowedKeys.Add(keys[i]);
                    }
                }
            }
            // A window with no key of its own (every key sits outside it) still needs a pose: hold
            // whichever key most recently applied before this window opened, exactly as a clip holds
            // its last frame past its own end.
            if (windowedKeys.Count == 0 && keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    if (keys[i].time <= timeSeconds)
                    {
                        windowedKeys.Add(keys[i]);
                    }
                }
            }

            SampleCamera(windowedKeys, timeSeconds, out position, out eulerDegrees, out fieldOfView);
        }

        /// <summary>
        /// Resolves the facing angle in effect at <paramref name="timeSeconds"/> (spec §2): the
        /// last override key at or before it, or the direction the root lane is travelling when
        /// none has fired yet. The list twin of <c>CutsceneBlobSampler.TryResolveFacingAngle</c>,
        /// down to the return value — false means "no answer at all", never "derived rather than
        /// authored". Ask <see cref="TryResolveFacingOverride"/> for that distinction.
        /// </summary>
        public static bool TryResolveFacingAngle(
            List<CutsceneFacingKey> facingKeys, List<CutsceneTransformKey> rootKeys,
            float timeSeconds, out float angleDegrees)
        {
            if (TryResolveFacingOverride(facingKeys, timeSeconds, out angleDegrees))
            {
                return true;
            }
            return TryDeriveFacingFromRootTravel(rootKeys, timeSeconds, out angleDegrees);
        }

        /// <summary>The last facing override key at or before <paramref name="timeSeconds"/>.</summary>
        public static bool TryResolveFacingOverride(
            List<CutsceneFacingKey> facingKeys, float timeSeconds, out float angleDegrees)
        {
            angleDegrees = 0f;
            if (facingKeys == null)
            {
                return false;
            }

            int bestIndex = -1;
            for (int i = 0; i < facingKeys.Count; i++)
            {
                if (facingKeys[i].time <= timeSeconds &&
                    (bestIndex < 0 || facingKeys[i].time > facingKeys[bestIndex].time))
                {
                    bestIndex = i;
                }
            }
            if (bestIndex < 0)
            {
                return false;
            }
            angleDegrees = facingKeys[bestIndex].angleDegrees;
            return true;
        }

        /// <summary>
        /// The direction the root lane travels at <paramref name="timeSeconds"/>, by finite
        /// difference against a hair earlier — the movement vector a live actor would hand
        /// <c>FacingResolver.FromMovement</c>. Forward-differenced at 0, which has nothing behind it.
        /// </summary>
        public static bool TryDeriveFacingFromRootTravel(
            List<CutsceneTransformKey> rootKeys, float timeSeconds, out float angleDegrees)
        {
            const float LookBackSeconds = 1f / 60f;
            angleDegrees = 0f;
            if (rootKeys == null || rootKeys.Count == 0)
            {
                return false;
            }

            float earlierTime = timeSeconds - LookBackSeconds;
            float laterTime = timeSeconds;
            if (earlierTime < 0f)
            {
                earlierTime = 0f;
                laterTime = LookBackSeconds;
            }

            float3 earlierPosition;
            float3 laterPosition;
            float3 unusedRotation;
            float3 unusedScale;
            TrySampleTransform(rootKeys, earlierTime, out earlierPosition, out unusedRotation, out unusedScale);
            TrySampleTransform(rootKeys, laterTime, out laterPosition, out unusedRotation, out unusedScale);

            float3 travel = laterPosition - earlierPosition;
            if (math.lengthsq(travel) < 1e-8f)
            {
                return false;
            }

            angleDegrees = CutsceneFacingVariants.AngleDegreesFromTravel(in travel);
            return true;
        }

        private static void ToOut(
            CutsceneTransformKey key, out float3 position, out float3 eulerDegrees, out float3 scale)
        {
            position = key.position;
            eulerDegrees = key.rotation;
            scale = key.scale;
        }
    }
}
