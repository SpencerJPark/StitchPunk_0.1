// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Turns a part's authored transform into the <see cref="TargetRestPose"/> clips compose against
    /// (architecture section 5.11: position and rotation additive, scale multiplicative).
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="RigTargetBaker"/> and the Cutscene Editor's Scene-view preview
    /// (amendment A58): a preview whose rest pose is derived differently from the bake's shows every
    /// authored offset in the wrong place, and nothing about the clip data would look wrong.
    /// </remarks>
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

        /// <summary>
        /// Signed ZXY Euler angles, in radians, from a rotation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ZXY because that is the order <c>quaternion.Euler</c> and <c>Transform.eulerAngles</c>
        /// both use, and <c>TransformApplySystem</c> rebuilds the pose with the former. Extracting
        /// in a different order than the one used to rebuild would give a rest pose that is correct
        /// only while two of the three angles are zero — that is, correct on every flat rig and
        /// wrong on the first tilted one.
        /// </para>
        /// <para>
        /// Signed, in (−π, π], rather than <c>localEulerAngles</c>'s [0, 360): a part authored at
        /// −30° must not come back as +330°, because the two compose differently once a clip adds
        /// a delta to them.
        /// </para>
        /// </remarks>
        public static float3 ExtractZxyEulerRadians(Quaternion rotation)
        {
            float x = rotation.x;
            float y = rotation.y;
            float z = rotation.z;
            float w = rotation.w;

            // sin(pitch) for the ZXY order; clamped because a value a hair outside [-1, 1] from
            // accumulated float error would make asin return NaN at exactly the poles.
            float sinPitch = math.clamp(2f * (w * x + y * z), -1f, 1f);
            float pitch = math.asin(sinPitch);

            float cosPitch = math.sqrt(math.max(0f, 1f - sinPitch * sinPitch));
            if (cosPitch < 1e-6f)
            {
                // Gimbal lock: yaw and roll describe the same turn, so the split between them is
                // arbitrary. Putting all of it in yaw is the conventional choice and keeps the
                // rebuilt rotation identical.
                return new float3(pitch, 2f * math.atan2(y, w), 0f);
            }

            float yaw = math.atan2(2f * (w * y - z * x), 1f - 2f * (x * x + y * y));
            float roll = math.atan2(2f * (w * z - x * y), 1f - 2f * (x * x + z * z));
            return new float3(pitch, yaw, roll);
        }
    }
}
