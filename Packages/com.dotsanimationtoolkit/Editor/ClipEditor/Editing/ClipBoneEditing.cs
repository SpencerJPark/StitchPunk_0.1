// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Reads and writes authored bone tracks at a point in time. Sampling goes through
    /// <c>BoneTrackPoser</c>, the same function the preview skeleton and the VAT bake use. Angles
    /// are exchanged as signed Euler degrees but stored as a quaternion, the authored form.
    /// </summary>
    public static class ClipBoneEditing
    {
        /// <summary>The index of the key at a time, or −1 when none is close enough.</summary>
        public static int FindKeyIndexAt(BoneTrack track, float normalizedTime)
        {
            if (track == null || track.keys == null)
            {
                return -1;
            }
            for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
            {
                float keyTime = track.keys[keyIndex].normalizedTime;
                if (Mathf.Abs(keyTime - normalizedTime) <= ClipTransformEditing.KeyTimeTolerance)
                {
                    return keyIndex;
                }
            }
            return -1;
        }

        /// <summary>
        /// Samples a bone track at a time.
        /// </summary>
        /// <returns>False when the track has no keys, leaving the rest pose in the outputs.</returns>
        public static bool TryEvaluate(
            BoneTrack track, float normalizedTime,
            out float3 position, out float3 rotationDegrees, out float3 scale)
        {
            position = float3.zero;
            rotationDegrees = float3.zero;
            scale = new float3(1f, 1f, 1f);

            if (track == null || track.keys == null || track.keys.Count == 0)
            {
                return false;
            }

            float3 sampledPosition;
            quaternion sampledRotation;
            float3 sampledScale;
            BoneTrackPoser.Sample(
                track.keys, normalizedTime,
                out sampledPosition, out sampledRotation, out sampledScale);

            position = sampledPosition;
            rotationDegrees = ToSignedEulerDegrees(sampledRotation);
            scale = sampledScale;
            return true;
        }

        /// <summary>
        /// Writes a bone value into the key at a time, creating that key if there is none.
        /// </summary>
        public static int SetKeyValues(
            BoneTrack track, float normalizedTime,
            float3 position, float3 rotationDegrees, float3 scale)
        {
            if (track == null)
            {
                return -1;
            }
            if (track.keys == null)
            {
                track.keys = new List<BoneKey>();
            }

            quaternion rotation = quaternion.Euler(math.radians(rotationDegrees));

            int existingIndex = FindKeyIndexAt(track, normalizedTime);
            if (existingIndex >= 0)
            {
                BoneKey existingKey = track.keys[existingIndex];
                existingKey.localPosition = position;
                existingKey.localRotation = rotation;
                existingKey.localScale = scale;
                track.keys[existingIndex] = existingKey;
                return existingIndex;
            }

            float2 inheritedStartHandle;
            float2 inheritedEndHandle;
            Interpolation inheritedInterpolation = InheritInterpolationAt(
                track, normalizedTime, out inheritedStartHandle, out inheritedEndHandle);
            track.keys.Add(new BoneKey
            {
                normalizedTime = normalizedTime,
                localPosition = position,
                localRotation = rotation,
                localScale = scale,
                interpolation = inheritedInterpolation,
                bezierStartHandle = inheritedStartHandle,
                bezierEndHandle = inheritedEndHandle
            });
            track.keys.Sort(CompareKeyTimes);
            return FindKeyIndexAt(track, normalizedTime);
        }

        /// <summary>Euler degrees in (−180, 180] per axis, in the order the authored value uses.</summary>
        public static float3 ToSignedEulerDegrees(quaternion rotation)
        {
            Vector3 euler = ((Quaternion)rotation).eulerAngles;
            return new float3(
                SignedDegrees(euler.x), SignedDegrees(euler.y), SignedDegrees(euler.z));
        }

        // Signed rather than eulerAngles' [0, 360): a joint at −30° must not read as +330° in a
        // field the user is about to nudge.
        private static float SignedDegrees(float degrees)
        {
            return degrees > 180f ? degrees - 360f : degrees;
        }

        // The easing of the key a new one lands after — mode and handles both, since a Bézier
        // without handles reads as linear and would flatten a hand-shaped segment.
        private static Interpolation InheritInterpolationAt(
            BoneTrack track, float normalizedTime,
            out float2 bezierStartHandle, out float2 bezierEndHandle)
        {
            Interpolation inherited = Interpolation.Linear;
            bezierStartHandle = float2.zero;
            bezierEndHandle = float2.zero;
            for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
            {
                if (track.keys[keyIndex].normalizedTime <= normalizedTime)
                {
                    inherited = track.keys[keyIndex].interpolation;
                    bezierStartHandle = track.keys[keyIndex].bezierStartHandle;
                    bezierEndHandle = track.keys[keyIndex].bezierEndHandle;
                }
            }
            return inherited;
        }

        private static int CompareKeyTimes(BoneKey first, BoneKey second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }
    }
}
