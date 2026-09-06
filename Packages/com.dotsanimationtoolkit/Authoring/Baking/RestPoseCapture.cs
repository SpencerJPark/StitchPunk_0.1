// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Turns a part's authored transform into the <see cref="TargetRestPose"/> clips compose against
    /// (position and rotation additive, scale multiplicative). Shared by <see cref="RigTargetBaker"/>
    /// and the Cutscene Editor's Scene-view preview, so both agree on where a part's rest pose sits.
    /// </summary>
    public static class RestPoseCapture
    {
        /// <summary>The rest pose a part authored at <paramref name="partTransform"/> composes from.</summary>
        public static TargetRestPose FromTransform(Transform partTransform, int restSliceIndex)
        {
            Vector3 localPosition = partTransform.localPosition;
            Vector3 localScale = partTransform.localScale;

            return new TargetRestPose
            {
                localPosition = new float3(localPosition.x, localPosition.y, localPosition.z),
                rotation = ExtractZxyEulerRadians(partTransform.localRotation),
                scale = new float3(localScale.x, localScale.y, localScale.z),
                restSliceIndex = math.max(0, restSliceIndex)
            };
        }

        /// <summary>Signed ZXY Euler angles in radians, as (−π, π] rather than [0, 360) — a delta added later composes differently otherwise.</summary>
        public static float3 ExtractZxyEulerRadians(Quaternion rotation)
        {
            float x = rotation.x;
            float y = rotation.y;
            float z = rotation.z;
            float w = rotation.w;

            // ZXY order to match quaternion.Euler/TransformApplySystem's rebuild — extracting in a
            // different order gives a rest pose correct only while two of the three angles are zero.
            // sin(pitch) for that order; clamped since float error can push it a hair past [-1, 1],
            // which would make asin return NaN at the poles.
            float sinPitch = math.clamp(2f * (w * x + y * z), -1f, 1f);
            float pitch = math.asin(sinPitch);

            float cosPitch = math.sqrt(math.max(0f, 1f - sinPitch * sinPitch));
            if (cosPitch < 1e-6f)
            {
                // Gimbal lock: yaw and roll describe the same turn, so the split is arbitrary.
                // Putting it all in yaw is the conventional choice and rebuilds identically.
                return new float3(pitch, 2f * math.atan2(y, w), 0f);
            }

            float yaw = math.atan2(2f * (w * y - z * x), 1f - 2f * (x * x + y * y));
            float roll = math.atan2(2f * (w * z - x * y), 1f - 2f * (x * x + z * z));
            return new float3(pitch, yaw, roll);
        }
    }
}
