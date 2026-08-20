// Copyright (c) 2026 Stitch Punk. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Which transform channel a gizmo drag edits.</summary>
    public enum GizmoMode : byte
    {
        Move = 0,
        Rotate = 1,
        Scale = 2
    }

    /// <summary>The specific handle under the cursor, or none.</summary>
    public enum GizmoHandle : byte
    {
        None = 0,
        AxisX = 1,
        AxisY = 2,
        AxisZ = 3,
        RotateX = 4,
        RotateY = 5,
        RotateZ = 6,
        ScaleUniform = 7
    }

    /// <summary>
    /// The geometry behind gizmo picking and dragging: ray against axis, ray against plane.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pure functions, separate from the drawing and from the editing, so the arithmetic that
    /// decides where a drag lands can be reasoned about and tested without a render utility or a
    /// clip. It is the part most likely to be subtly wrong and least likely to look wrong.
    /// </para>
    /// <para>
    /// All three axes are offered for move, rotate and scale, because the authored key carries all
    /// three: nothing animated in this system is assumed to be flat. A 2.5D cutout simply leaves the
    /// axes it does not use at their identity and pays nothing for them.
    /// </para>
    /// </remarks>
    public static class PreviewGizmoMath
    {
        /// <summary>How close, in world units, the ray must pass to a handle to hit it.</summary>
        public const float HandlePickRadiusFactor = 0.09f;

        /// <summary>
        /// The parameter along an infinite axis at the point closest to a ray.
        /// </summary>
        /// <remarks>
        /// Returns false for a ray parallel to the axis, where "closest point" is every point and
        /// any answer would be arbitrary — a drag in that view would jump rather than track.
        /// </remarks>
        public static bool TryGetClosestAxisParameter(
            Ray ray, Vector3 axisOrigin, Vector3 axisDirection, out float axisParameter)
        {
            axisParameter = 0f;

            Vector3 originDelta = axisOrigin - ray.origin;
            float axisDotRay = Vector3.Dot(axisDirection, ray.direction);
            float denominator = 1f - axisDotRay * axisDotRay;
            if (Mathf.Abs(denominator) < 1e-6f)
            {
                return false;
            }

            // With w = axisOrigin - rayOrigin, the closest parameter on the axis is
            // (b·e - d) / (1 - b²), where b = axis·ray, d = axis·w and e = ray·w. The negated form
            // is the easy mistake: it puts the drag the same distance the wrong side of the pivot,
            // so a handle tracks backwards and nothing further along the chain looks wrong.
            float axisDotDelta = Vector3.Dot(axisDirection, originDelta);
            float rayDotDelta = Vector3.Dot(ray.direction, originDelta);
            axisParameter = (axisDotRay * rayDotDelta - axisDotDelta) / denominator;
            return true;
        }

        /// <summary>The shortest distance between a ray and a bounded segment.</summary>
        public static float DistanceFromRayToSegment(
            Ray ray, Vector3 segmentStart, Vector3 segmentEnd)
        {
            Vector3 segmentDelta = segmentEnd - segmentStart;
            float segmentLength = segmentDelta.magnitude;
            if (segmentLength < 1e-6f)
            {
                return Vector3.Cross(ray.direction, segmentStart - ray.origin).magnitude;
            }

            Vector3 segmentDirection = segmentDelta / segmentLength;
            float axisParameter;
            if (!TryGetClosestAxisParameter(ray, segmentStart, segmentDirection, out axisParameter))
            {
                return Vector3.Cross(ray.direction, segmentStart - ray.origin).magnitude;
            }

            axisParameter = Mathf.Clamp(axisParameter, 0f, segmentLength);
            Vector3 closestOnSegment = segmentStart + segmentDirection * axisParameter;

            float rayParameter = Mathf.Max(0f, Vector3.Dot(closestOnSegment - ray.origin, ray.direction));
            Vector3 closestOnRay = ray.origin + ray.direction * rayParameter;
            return Vector3.Distance(closestOnSegment, closestOnRay);
        }

        /// <summary>
        /// Where a ray meets the plane through <paramref name="planePoint"/> facing
        /// <paramref name="planeNormal"/>.
        /// </summary>
        public static bool TryIntersectPlane(
            Ray ray, Vector3 planePoint, Vector3 planeNormal, out Vector3 intersection)
        {
            intersection = Vector3.zero;

            float directionDotNormal = Vector3.Dot(ray.direction, planeNormal);
            if (Mathf.Abs(directionDotNormal) < 1e-6f)
            {
                // Edge-on: the ray never meets the plane, or meets all of it.
                return false;
            }

            float distanceAlongRay =
                Vector3.Dot(planePoint - ray.origin, planeNormal) / directionDotNormal;
            if (distanceAlongRay < 0f)
            {
                return false;
            }

            intersection = ray.origin + ray.direction * distanceAlongRay;
            return true;
        }

        /// <summary>The plane a rotation ring lies in, as its normal.</summary>
        public static Vector3 GetRotationPlaneNormal(GizmoHandle handle)
        {
            switch (handle)
            {
                case GizmoHandle.RotateX:
                    return Vector3.right;
                case GizmoHandle.RotateY:
                    return Vector3.up;
                default:
                    return Vector3.forward;
            }
        }

        /// <summary>
        /// The angle, in degrees, of a point around a pivot within a ring's own plane.
        /// </summary>
        /// <remarks>
        /// Measured in the plane the ring lies in, so a drag of 90° on any axis writes 90 to that
        /// axis rather than something that merely looks like a right angle from this camera.
        /// </remarks>
        public static float AngleAroundPivotDegrees(Vector3 point, Vector3 pivot, GizmoHandle handle)
        {
            Vector3 offset = point - pivot;
            switch (handle)
            {
                case GizmoHandle.RotateX:
                    return Mathf.Atan2(offset.z, offset.y) * Mathf.Rad2Deg;
                case GizmoHandle.RotateY:
                    return Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
                default:
                    return Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
            }
        }

        /// <summary>
        /// Picks the handle under a ray for a mode, or <see cref="GizmoHandle.None"/>.
        /// </summary>
        /// <remarks>
        /// Axes are tested nearest-first so overlapping handles resolve to the one actually under
        /// the cursor. The uniform-scale handle is tested before the axes because it sits at the
        /// pivot, where all three axes begin — testing it last would make it unreachable.
        /// </remarks>
        public static GizmoHandle PickHandle(
            Ray ray, GizmoMode mode, Vector3 pivot, float handleLength)
        {
            float pickRadius = handleLength * HandlePickRadiusFactor;

            if (mode == GizmoMode.Rotate)
            {
                // Three rings, best-fit first. Whichever ring the ray crosses closest to its own
                // radius is the one under the cursor; testing z alone would leave a tilted rig with
                // no way to be rotated into place.
                GizmoHandle bestRing = GizmoHandle.None;
                float bestRingError = pickRadius * 1.6f;
                GizmoHandle[] ringHandles = new GizmoHandle[]
                {
                    GizmoHandle.RotateX, GizmoHandle.RotateY, GizmoHandle.RotateZ
                };
                for (int ringIndex = 0; ringIndex < ringHandles.Length; ringIndex++)
                {
                    Vector3 planeHit;
                    if (!TryIntersectPlane(
                            ray, pivot, GetRotationPlaneNormal(ringHandles[ringIndex]), out planeHit))
                    {
                        continue;
                    }
                    float ringError = Mathf.Abs(Vector3.Distance(planeHit, pivot) - handleLength);
                    if (ringError < bestRingError)
                    {
                        bestRingError = ringError;
                        bestRing = ringHandles[ringIndex];
                    }
                }
                return bestRing;
            }

            if (mode == GizmoMode.Scale
                && DistanceFromRayToSegment(ray, pivot, pivot) <= pickRadius * 1.2f)
            {
                return GizmoHandle.ScaleUniform;
            }

            GizmoHandle bestHandle = GizmoHandle.None;
            float bestDistance = pickRadius;

            float distanceX = DistanceFromRayToSegment(
                ray, pivot, pivot + Vector3.right * handleLength);
            if (distanceX < bestDistance)
            {
                bestDistance = distanceX;
                bestHandle = GizmoHandle.AxisX;
            }

            float distanceY = DistanceFromRayToSegment(
                ray, pivot, pivot + Vector3.up * handleLength);
            if (distanceY < bestDistance)
            {
                bestDistance = distanceY;
                bestHandle = GizmoHandle.AxisY;
            }

            float distanceZ = DistanceFromRayToSegment(
                ray, pivot, pivot + Vector3.forward * handleLength);
            if (distanceZ < bestDistance)
            {
                bestHandle = GizmoHandle.AxisZ;
            }

            return bestHandle;
        }

        /// <summary>The world-space direction a handle drags along.</summary>
        public static Vector3 GetHandleAxis(GizmoHandle handle)
        {
            switch (handle)
            {
                case GizmoHandle.AxisX:
                    return Vector3.right;
                case GizmoHandle.AxisY:
                    return Vector3.up;
                case GizmoHandle.AxisZ:
                    return Vector3.forward;
                default:
                    return Vector3.right;
            }
        }
    }
}
