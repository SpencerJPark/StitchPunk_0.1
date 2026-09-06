// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Billboard orientation as pure functions: where a node must look, how the mode constrains
    /// it, and how offset, snap, arc and blend fold in. Takes world-space vectors only — no
    /// <c>Camera</c>, entity, or transform hierarchy — so it is testable without a World.
    /// </summary>
    [BurstCompile]
    public static class BillboardMath
    {
        public const float DirectionEpsilon = 1e-6f; // matches TOOLKIT_BILLBOARD_EPSILON in ToolkitBillboard.hlsl; CPU and shader must agree

        // A property, not a static readonly field: Burst's support for reading a static field of a
        // non-primitive type isn't reliable in code every job calls; this inlines to two constants.
        private static float3 WorldUp
        {
            get { return new float3(0f, 1f, 0f); }
        }

        // -----------------------------------------------------------------------------------
        // Facing.
        // -----------------------------------------------------------------------------------

        /// <param name="facingWS">The resolved direction, not normalised.</param>
        /// <returns>False when the inputs give no usable direction, leaving the node alone.</returns>
        [BurstCompile]
        public static bool TryResolveFacing(
            BillboardMode mode,
            in float3 nodePositionWS,
            in float3 cameraPositionWS,
            in float3 cameraForwardWS,
            out float3 facingWS)
        {
            if (mode == BillboardMode.ScreenAligned
                && math.lengthsq(cameraForwardWS) >= DirectionEpsilon)
            {
                facingWS = cameraForwardWS;
                return true;
            }

            // +Z points away from the viewer, not toward it (the host's convention): camera-to-node,
            // not node-to-camera.
            facingWS = nodePositionWS - cameraPositionWS;
            return math.lengthsq(facingWS) >= DirectionEpsilon;
        }

        /// <summary>The axis a mode turns about, and the axis its offset, snap wheel and arc are measured around. Normalised.</summary>
        /// <param name="constraintAxis">The authored axis, used only by the axis-constrained mode.</param>
        /// <returns>False when an axis-constrained root was given no usable axis.</returns>
        [BurstCompile]
        public static bool TryResolveReferenceAxis(
            BillboardMode mode, in float3 constraintAxis, out float3 axis)
        {
            if (mode != BillboardMode.AxisConstrained)
            {
                axis = WorldUp;
                return true;
            }

            axis = math.normalizesafe(constraintAxis);
            // A zero axis is an authoring-time validation error; refusing here too means a rig that
            // reached the runtime with one leaves its node alone rather than snapping to a default.
            return math.lengthsq(axis) >= DirectionEpsilon;
        }

        // -----------------------------------------------------------------------------------
        // Angle helpers. Primitives in, primitives out.
        // -----------------------------------------------------------------------------------

        /// <summary>Quantises an angle onto one of <paramref name="steps"/> evenly spaced facings.</summary>
        /// <param name="steps">How many facings a full turn divides into; below 2 is a no-op.</param>
        /// <param name="phaseRadians">Rotates the whole wheel, so steps can straddle the cardinals.</param>
        [BurstCompile]
        public static float SnapAngle(float radians, int steps, float phaseRadians)
        {
            if (steps < 2)
            {
                return radians;
            }
            float stepSize = (2f * math.PI) / steps;
            return math.round((radians - phaseRadians) / stepSize) * stepSize + phaseRadians;
        }

        /// <param name="halfArcRadians">Half the arc's width; negative is a no-op.</param>
        [BurstCompile]
        public static float ClampAngle(float radians, float halfArcRadians)
        {
            if (halfArcRadians < 0f)
            {
                return radians;
            }
            float limit = math.min(halfArcRadians, math.PI);
            // Wrapped to (-pi, pi] before clamping: an unwrapped 350 degrees reads as far outside a
            // 90 degree arc and would clamp to +45, instead of passing through as -10 untouched.
            return math.clamp(WrapToPi(radians), -limit, limit);
        }

        /// <summary>The signed rotation of <paramref name="rotation"/> about <paramref name="axis"/> — the twist half of a swing-twist decomposition.</summary>
        /// <returns>The twist angle in radians, in (-pi, pi].</returns>
        [BurstCompile]
        public static float TwistAngle(in quaternion rotation, in float3 axis)
        {
            float3 normalizedAxis = math.normalizesafe(axis);
            float4 rotationValue = rotation.value;
            // Folded to the w >= 0 hemisphere first: a quaternion and its negation are the same
            // rotation but their raw twist angles differ by a full turn.
            if (rotationValue.w < 0f)
            {
                rotationValue = -rotationValue;
            }
            float axisComponent = math.dot(rotationValue.xyz, normalizedAxis);
            return 2f * math.atan2(axisComponent, rotationValue.w);
        }

        // -----------------------------------------------------------------------------------
        // The whole pipeline.
        // -----------------------------------------------------------------------------------

        /// <summary>Resolves one billboard root's world orientation.</summary>
        /// <param name="restRotationWS">
        /// The node's world orientation before billboarding — both the blend's starting point and
        /// the reference the arc and wheel are measured from.
        /// </param>
        /// <returns>
        /// False when there is nothing to apply — the mode is off, the root is disabled, or the
        /// inputs are degenerate — in which case the caller must leave the node's transform alone.
        /// </returns>
        [BurstCompile]
        public static bool TryResolve(
            in BillboardSettings settings,
            in float3 nodePositionWS,
            in float3 cameraPositionWS,
            in float3 cameraForwardWS,
            in quaternion restRotationWS,
            out quaternion resultWS)
        {
            resultWS = restRotationWS;

            if (settings.mode == BillboardMode.Off || !settings.enabled)
            {
                return false;
            }

            float3 facingWS;
            if (!TryResolveFacing(
                    settings.mode, nodePositionWS, cameraPositionWS, cameraForwardWS, out facingWS))
            {
                return false;
            }

            float3 referenceAxis;
            if (!TryResolveReferenceAxis(settings.mode, settings.constraintAxis, out referenceAxis))
            {
                return false;
            }

            quaternion target;
            if (!TryBuildTarget(settings, facingWS, referenceAxis, out target))
            {
                return false;
            }

            if (settings.angleOffsetRadians != 0f)
            {
                // Added before the snap wheel, so a keyed offset lands on a step along with
                // everything else instead of parking the rig between two.
                target = math.mul(quaternion.AxisAngle(referenceAxis, settings.angleOffsetRadians), target);
            }

            if (settings.SnapEnabled || settings.ClampEnabled)
            {
                target = ApplyWheelAndArc(settings, referenceAxis, restRotationWS, target);
            }

            resultWS = math.slerp(restRotationWS, target, math.saturate(settings.blendWeight));
            return true;
        }

        // -----------------------------------------------------------------------------------
        // Implementation.
        // -----------------------------------------------------------------------------------

        private static bool TryBuildTarget(
            in BillboardSettings settings, float3 facingWS, float3 referenceAxis, out quaternion target)
        {
            bool isAxisConstrained = settings.mode == BillboardMode.Upright
                || settings.mode == BillboardMode.AxisConstrained;

            if (isAxisConstrained)
            {
                // Flattening the facing onto the plane perpendicular to the axis is what restricts
                // the turn to that axis — an upright character faces the camera without ever leaning
                // toward or away from it.
                facingWS -= referenceAxis * math.dot(facingWS, referenceAxis);
                if (math.lengthsq(facingWS) < DirectionEpsilon)
                {
                    // The camera sits on the axis itself: every facing projects to nothing, and
                    // there is no correct answer to give. Leaving the node alone is the only
                    // non-arbitrary one, and it is what the host's system does.
                    target = quaternion.identity;
                    return false;
                }
            }

            if (!TryBuildBasis(facingWS, referenceAxis, out target))
            {
                return false;
            }

            if (settings.mode == BillboardMode.FrozenYaw)
            {
                target = ApplyFrozenYaw(target, settings.frozenYaw);
            }
            return true;
        }

        /// <summary>An orthonormal rotation mapping local +Z onto <paramref name="forward"/> and local +Y as near <paramref name="up"/> as that allows.</summary>
        // Written out rather than delegated to quaternion.LookRotationSafe so the degenerate case
        // matches ToolkitBillboardBasis in the shader: pick a perpendicular rather than emit NaNs.
        private static bool TryBuildBasis(float3 forward, float3 up, out quaternion rotation)
        {
            float3 zAxis = math.normalizesafe(forward);
            if (math.lengthsq(zAxis) < DirectionEpsilon)
            {
                rotation = quaternion.identity;
                return false;
            }

            float3 xAxis = math.cross(up, zAxis);
            float xAxisLength = math.length(xAxis);
            if (xAxisLength < DirectionEpsilon)
            {
                // Forward parallel to up — the camera looking straight down an upright billboard.
                // Any perpendicular will do; it must simply be the same one every frame.
                xAxis = math.normalizesafe(math.cross(new float3(0f, 0f, 1f), zAxis));
                if (math.lengthsq(xAxis) < DirectionEpsilon)
                {
                    xAxis = new float3(1f, 0f, 0f);
                }
            }
            else
            {
                xAxis /= xAxisLength;
            }

            float3 yAxis = math.cross(zAxis, xAxis);
            // Columns, so the matrix maps local axes onto the world ones just built.
            rotation = new quaternion(new float3x3(xAxis, yAxis, zAxis));
            return true;
        }

        /// <summary>Substitutes an authored yaw while keeping the camera-derived pitch.</summary>
        private static quaternion ApplyFrozenYaw(quaternion cameraFacing, float frozenYaw)
        {
            float3 targetForward = math.mul(cameraFacing, math.forward());
            float3 flatForward = new float3(targetForward.x, 0f, targetForward.z);

            quaternion frozen = quaternion.RotateY(frozenYaw);
            if (math.lengthsq(flatForward) < DirectionEpsilon)
            {
                // Camera directly overhead: there is no yaw to strip, so the frozen yaw is the whole
                // answer rather than a component of it.
                return frozen;
            }

            quaternion cameraYaw = quaternion.LookRotationSafe(math.normalize(flatForward), WorldUp);
            quaternion pitchOnly = math.mul(math.inverse(cameraYaw), cameraFacing);
            return math.mul(frozen, pitchOnly);
        }

        /// <summary>Applies the snap wheel and arc limit to the rotation about the reference axis, leaving every other component of the pose as it was.</summary>
        private static quaternion ApplyWheelAndArc(
            in BillboardSettings settings,
            float3 referenceAxis,
            quaternion restRotationWS,
            quaternion target)
        {
            // Measured from the rest orientation, not the world, so both travel with an animation
            // that turns the node. Only the twist about the axis is snapped/clamped; the swing
            // (everything else) is carried through untouched.
            quaternion delta = math.mul(target, math.inverse(restRotationWS));
            float twistAngle = TwistAngle(delta, referenceAxis);
            quaternion twist = quaternion.AxisAngle(referenceAxis, twistAngle);
            quaternion swing = math.mul(delta, math.inverse(twist));

            float adjustedAngle = twistAngle;
            if (settings.SnapEnabled)
            {
                adjustedAngle = SnapAngle(adjustedAngle, settings.snapSteps, settings.snapPhaseRadians);
            }
            if (settings.ClampEnabled)
            {
                adjustedAngle = ClampAngle(adjustedAngle, settings.clampHalfArcRadians);
            }

            quaternion adjustedDelta =
                math.mul(swing, quaternion.AxisAngle(referenceAxis, adjustedAngle));
            return math.mul(adjustedDelta, restRotationWS);
        }

        /// <summary>Folds an angle into (-pi, pi].</summary>
        private static float WrapToPi(float radians)
        {
            float wrapped = math.fmod(radians + math.PI, 2f * math.PI);
            if (wrapped < 0f)
            {
                wrapped += 2f * math.PI;
            }
            return wrapped - math.PI;
        }
    }
}
