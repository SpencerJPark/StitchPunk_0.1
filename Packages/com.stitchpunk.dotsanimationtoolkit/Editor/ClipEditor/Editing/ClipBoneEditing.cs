// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Reads and writes authored bone tracks at a point in time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sampling goes through <c>BoneTrackPoser</c>, the same function the preview skeleton and the
    /// VAT bake use, so the numbers in the inspector are the pose that will be baked rather than a
    /// second reading of the same keys.
    /// </para>
    /// <para>
    /// <strong>Angles are exchanged as signed Euler degrees, stored as a quaternion.</strong> The
    /// quaternion is the authored form — it is what the bake samples and what has no gimbal order
    /// to agree on — but it is not a thing anyone types, so the editor converts at the boundary.
    /// Signed rather than <c>eulerAngles</c>' [0, 360) because a joint at −30° must not read as
    /// +330° in a field the user is about to nudge.
    /// </para>
    /// </remarks>
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

            track.keys.Add(new BoneKey
            {
                normalizedTime = normalizedTime,
                localPosition = position,
                localRotation = rotation,
                localScale = scale,
                interpolation = InheritInterpolationAt(track, normalizedTime)
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

        private static float SignedDegrees(float degrees)
        {
            return degrees > 180f ? degrees - 360f : degrees;
        }

        private static Interpolation InheritInterpolationAt(BoneTrack track, float normalizedTime)
        {
            Interpolation inherited = Interpolation.Linear;
            for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
            {
                if (track.keys[keyIndex].normalizedTime <= normalizedTime)
                {
                    inherited = track.keys[keyIndex].interpolation;
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
