// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Evaluates a <see cref="CutsceneTransformKey"/>/<see cref="CutsceneFacingKey"/> list at a raw
    /// timeline second, for the Scene-view preview (Phase G, G3). Editor-only and GameObject-facing
    /// by design — it interpolates authoring lists directly rather than through
    /// <c>ClipRegistryBlob</c>/<c>ClipSampler</c>'s baked, Burst-jobbed path, because there is no
    /// blob until G5 and nothing here runs per-frame at runtime. It reuses
    /// <see cref="ClipSampler.Ease"/> rather than re-implementing easing, so a key drawn with the
    /// same curve editor look and the same math everywhere else in this package.
    /// </summary>
    internal static class CutscenePoseSampler
    {
        /// <summary>Samples a transform key list at <paramref name="timeSeconds"/>. Holds the nearest key outside the authored range, exactly like a clip's own edge behaviour.</summary>
        public static void Sample(
            List<CutsceneTransformKey> keys, float timeSeconds,
            out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            if (keys == null || keys.Count == 0)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                scale = Vector3.one;
                return;
            }

            if (keys.Count == 1 || timeSeconds <= keys[0].time)
            {
                ToUnityTypes(keys[0], out position, out rotation, out scale);
                return;
            }

            int lastIndex = keys.Count - 1;
            if (timeSeconds >= keys[lastIndex].time)
            {
                ToUnityTypes(keys[lastIndex], out position, out rotation, out scale);
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

            CutsceneTransformKey fromKey = keys[segmentStart];
            CutsceneTransformKey toKey = keys[segmentStart + 1];
            float span = toKey.time - fromKey.time;
            float linearTime = span > 0f ? Mathf.Clamp01((timeSeconds - fromKey.time) / span) : 1f;
            float easedTime = ClipSampler.Ease(
                linearTime, fromKey.interpolation, fromKey.bezierStartHandle, fromKey.bezierEndHandle);

            position = Vector3.LerpUnclamped(
                ToVector3(fromKey.position), ToVector3(toKey.position), easedTime);
            rotation = Quaternion.SlerpUnclamped(
                Quaternion.Euler(ToVector3(fromKey.rotation)), Quaternion.Euler(ToVector3(toKey.rotation)), easedTime);
            scale = Vector3.LerpUnclamped(ToVector3(fromKey.scale), ToVector3(toKey.scale), easedTime);
        }

        /// <summary>Samples a camera key list the same way <see cref="Sample"/> does, plus field of view.</summary>
        public static void SampleCamera(
            List<CutsceneCameraKey> keys, float timeSeconds,
            out Vector3 position, out Quaternion rotation, out float fieldOfView)
        {
            if (keys == null || keys.Count == 0)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                fieldOfView = 60f;
                return;
            }

            if (keys.Count == 1 || timeSeconds <= keys[0].time)
            {
                position = ToVector3(keys[0].position);
                rotation = Quaternion.Euler(ToVector3(keys[0].rotation));
                fieldOfView = keys[0].fieldOfView;
                return;
            }

            int lastIndex = keys.Count - 1;
            if (timeSeconds >= keys[lastIndex].time)
            {
                position = ToVector3(keys[lastIndex].position);
                rotation = Quaternion.Euler(ToVector3(keys[lastIndex].rotation));
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
            float linearTime = span > 0f ? Mathf.Clamp01((timeSeconds - fromKey.time) / span) : 1f;
            float easedTime = ClipSampler.Ease(
                linearTime, fromKey.interpolation, fromKey.bezierStartHandle, fromKey.bezierEndHandle);

            position = Vector3.LerpUnclamped(ToVector3(fromKey.position), ToVector3(toKey.position), easedTime);
            rotation = Quaternion.SlerpUnclamped(
                Quaternion.Euler(ToVector3(fromKey.rotation)), Quaternion.Euler(ToVector3(toKey.rotation)), easedTime);
            fieldOfView = Mathf.LerpUnclamped(fromKey.fieldOfView, toKey.fieldOfView, easedTime);
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
            out Vector3 position, out Quaternion rotation, out float fieldOfView, out bool isCut)
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
                    if (Mathf.Abs(cutTime - timeSeconds) <= CutEpsilon)
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

            SampleCamera(windowedKeys, timeSeconds, out position, out rotation, out fieldOfView);
        }

        /// <summary>
        /// Resolves the facing angle in effect at <paramref name="timeSeconds"/> (spec §2): the
        /// last override key at or before it, or a derivation from root travel direction when none
        /// has fired yet.
        /// </summary>
        public static bool TryResolveFacingAngle(
            List<CutsceneFacingKey> facingKeys, List<CutsceneTransformKey> rootKeys,
            float timeSeconds, out float angleDegrees)
        {
            if (facingKeys != null)
            {
                int bestIndex = -1;
                for (int i = 0; i < facingKeys.Count; i++)
                {
                    if (facingKeys[i].time <= timeSeconds &&
                        (bestIndex < 0 || facingKeys[i].time > facingKeys[bestIndex].time))
                    {
                        bestIndex = i;
                    }
                }
                if (bestIndex >= 0)
                {
                    angleDegrees = facingKeys[bestIndex].angleDegrees;
                    return true;
                }
            }

            // No override yet: derive from travel direction, comparing this instant against a hair
            // earlier — the same finite-difference a live actor's movement vector would give
            // FacingResolver.FromMovement.
            const float LookBackSeconds = 0.05f;
            Vector3 positionNow;
            Quaternion rotationNow;
            Vector3 scaleNow;
            Sample(rootKeys, timeSeconds, out positionNow, out rotationNow, out scaleNow);
            Vector3 positionBefore;
            Sample(rootKeys, Mathf.Max(0f, timeSeconds - LookBackSeconds), out positionBefore, out rotationNow, out scaleNow);

            Vector3 delta = positionNow - positionBefore;
            if (delta.sqrMagnitude < 1e-8f)
            {
                angleDegrees = 0f;
                return false;
            }

            angleDegrees = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            if (angleDegrees < 0f)
            {
                angleDegrees += 360f;
            }
            return false;
        }

        private static void ToUnityTypes(
            CutsceneTransformKey key, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = ToVector3(key.position);
            rotation = Quaternion.Euler(ToVector3(key.rotation));
            scale = ToVector3(key.scale);
        }

        private static Vector3 ToVector3(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }
    }
}
