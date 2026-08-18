// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// The whole of billboard orientation, as pure functions (amendment A44): where a node must
    /// look, how a mode constrains that, and how an authored offset, a snap wheel, an arc limit and
    /// a blend weight fold in on top.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One implementation, three callers.</strong> <c>BillboardResolveSystem</c> calls this
    /// from a Burst job, the Clip Editor's preview calls it from managed editor code, and the
    /// billboard-frame query reads what it produced. Every one of those must agree to the last
    /// float, so none of them is allowed its own copy of the arithmetic.
    /// </para>
    /// <para>
    /// <strong>Nothing here touches a <c>Camera</c>, an entity, or a transform hierarchy.</strong>
    /// It takes world-space vectors and returns a world-space rotation. That is what makes the whole
    /// of A44's behaviour testable without a World, and it is the same split
    /// <c>EventWindowMath</c> and <c>ClipSampler</c> already draw.
    /// </para>
    /// <para>
    /// <strong>This is not <see cref="FacingResolver"/>, and the two must not be confused.</strong>
    /// That one answers "which of my eight authored clips do I play, mirrored or not" — a discrete
    /// art-selection question. This one answers "which way does this quad point" — a continuous
    /// orientation question. They both contain the word facing and they share nothing else.
    /// </para>
    /// </remarks>
    [BurstCompile]
    public static class BillboardMath
    {
        /// <summary>
        /// Below this a direction or a basis is degenerate. Matches
        /// <c>TOOLKIT_BILLBOARD_EPSILON</c> in <c>ToolkitBillboard.hlsl</c>, deliberately: the CPU
        /// and shader paths must agree on which inputs they refuse.
        /// </summary>
        public const float DirectionEpsilon = 1e-6f;

        /// <summary>World up, the reference axis for every mode that does not name its own.</summary>
        /// <remarks>
        /// A property rather than a <c>static readonly</c> field, because Burst's support for
        /// reading a static field of a non-primitive type is not something to rely on in code every
        /// job calls. The property inlines to the same two constants and cannot fail to compile.
        /// </remarks>
        private static float3 WorldUp
        {
            get { return new float3(0f, 1f, 0f); }
        }

        // -----------------------------------------------------------------------------------
        // Facing.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The direction the node's local +Z must point for the given mode.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>+Z points away from the viewer, not toward it.</strong> This is the host game's
        /// convention and A44 adopts it as normative. Unity's <c>PrimitiveType.Quad</c> — what the
        /// package's own sample builds every part from — carries its visible normal on −Z, so a quad
        /// whose +Z points at the camera presents its back. The package shipped the opposite sign
        /// and this is the correction.
        /// </para>
        /// <para>
        /// <strong>Screen-aligned takes the camera's forward; everything else takes the offset from
        /// the camera.</strong> Screen-aligned gives every root the same rotation, which is the
        /// classic 2.5D look (amendment A39); the spherical modes make each root face the camera
        /// <em>point</em>. A host that never writes a forward vector falls back to spherical rather
        /// than collapsing — degrading to a different-but-correct look beats degrading to a
        /// degenerate one.
        /// </para>
        /// <para>
        /// Neither source is the view matrix, and neither may become it: during shadow rendering the
        /// view matrix belongs to the light, and a billboard derived from it casts the shadow of a
        /// shape the camera never sees (§6.3).
        /// </para>
        /// </remarks>
        /// <param name="mode">The billboard rule being applied.</param>
        /// <param name="nodePositionWS">The billboard node's world position.</param>
        /// <param name="cameraPositionWS">The rendering camera's world position.</param>
        /// <param name="cameraForwardWS">The rendering camera's world forward; may be zero.</param>
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

            facingWS = nodePositionWS - cameraPositionWS;
            return math.lengthsq(facingWS) >= DirectionEpsilon;
        }

        /// <summary>
        /// The axis a mode turns about, and the axis its offset, snap wheel and arc are measured
        /// around. Normalised.
        /// </summary>
        /// <remarks>
        /// <see cref="BillboardMode.AxisConstrained"/> names its own; every other mode uses world up.
        /// <see cref="BillboardMode.Upright"/> is exactly the axis-constrained case with world up,
        /// which is why the two share every line below this one.
        /// </remarks>
        /// <param name="mode">The billboard rule being applied.</param>
        /// <param name="constraintAxis">The authored axis, used only by the axis-constrained mode.</param>
        /// <param name="axis">The normalised reference axis.</param>
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
            // A zero axis is validation rule V23's error, reported at authoring time. Refusing here
            // as well means a rig that reached the runtime with one still leaves its node alone
            // instead of snapping it to an arbitrary default.
            return math.lengthsq(axis) >= DirectionEpsilon;
        }

        // -----------------------------------------------------------------------------------
        // Angle helpers. Primitives in, primitives out — the easiest half of this file to
        // reason about, and the half that carries the sprite-facing behaviour.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Quantises an angle onto one of <paramref name="steps"/> evenly spaced facings.
        /// </summary>
        /// <param name="radians">The angle to quantise.</param>
        /// <param name="steps">How many facings a full turn divides into; below 2 is a no-op.</param>
        /// <param name="phaseRadians">Rotates the whole wheel, so steps can straddle the cardinals.</param>
        /// <returns>The nearest step, or the input unchanged when snapping is off.</returns>
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

        /// <summary>
        /// Limits an angle to a symmetric arc about zero.
        /// </summary>
        /// <remarks>
        /// <strong>Wrapped to (−π, π] before clamping, and that is the whole subtlety.</strong> An
        /// unwrapped 350° reads as far outside a 90° arc and would clamp to +45°, spinning the node
        /// almost the whole way round to reach a limit it was already inside. Wrapped, it reads as
        /// −10° and passes through untouched.
        /// </remarks>
        /// <param name="radians">The angle to limit.</param>
        /// <param name="halfArcRadians">Half the arc's width; negative is a no-op.</param>
        /// <returns>The limited angle.</returns>
        [BurstCompile]
        public static float ClampAngle(float radians, float halfArcRadians)
        {
            if (halfArcRadians < 0f)
            {
                return radians;
            }
            float limit = math.min(halfArcRadians, math.PI);
            return math.clamp(WrapToPi(radians), -limit, limit);
        }

        /// <summary>
        /// The signed rotation of <paramref name="rotation"/> about <paramref name="axis"/> — the
        /// twist half of a swing-twist decomposition.
        /// </summary>
        /// <remarks>
        /// The quaternion is folded to the <c>w &gt;= 0</c> hemisphere first. A quaternion and its
        /// negation are the same rotation but their raw twist angles differ by a full turn, so
        /// skipping the fold would make the snap wheel and the arc limit disagree with themselves
        /// depending on which representative a caller happened to hold.
        /// </remarks>
        /// <param name="rotation">The rotation to decompose.</param>
        /// <param name="axis">The axis to measure about; normalised internally.</param>
        /// <returns>The twist angle in radians, in (−π, π].</returns>
        [BurstCompile]
        public static float TwistAngle(in quaternion rotation, in float3 axis)
        {
            float3 normalizedAxis = math.normalizesafe(axis);
            float4 rotationValue = rotation.value;
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

        /// <summary>
        /// Resolves one billboard root's world orientation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The order is fixed by A44 and each step is here for a reason:
        /// </para>
        /// <list type="number">
        /// <item><description>Face the viewer, as the mode defines facing.</description></item>
        /// <item><description>Constrain that to the mode's axis, if it has one.</description></item>
        /// <item><description>Add the authored and keyed angle offset.</description></item>
        /// <item><description>Snap to the wheel, <em>after</em> the offset — so a keyed offset lands
        /// on a step with everything else instead of parking the rig between two.</description></item>
        /// <item><description>Clamp to the arc, last, because it is a hard guarantee. At the arc
        /// boundary the result may sit off a snap step: the clamp is a constraint, the snap is a
        /// look, and a constraint a look can violate is not a constraint.</description></item>
        /// <item><description>Blend against the animated pose.</description></item>
        /// </list>
        /// <para>
        /// <strong>Snapping and clamping are measured from the rest orientation, not from the
        /// world.</strong> Both therefore travel with an animation that turns the node — a character
        /// keyed to turn on the spot clicks through its eight facings relative to its own forward,
        /// which is what eight-direction sprite art means. Measuring from the world would make the
        /// wheel a property of where the actor stands.
        /// </para>
        /// <para>
        /// <strong>Only the rotation about the reference axis is snapped or clamped.</strong> The
        /// rest of the difference from the rest pose — the swing, in swing-twist terms — is carried
        /// through untouched, so a node whose rest pose is tilted keeps its tilt instead of being
        /// flattened onto the wheel.
        /// </para>
        /// </remarks>
        /// <param name="settings">The root's configuration and its keyed channels.</param>
        /// <param name="nodePositionWS">The node's world position.</param>
        /// <param name="cameraPositionWS">The rendering camera's world position.</param>
        /// <param name="cameraForwardWS">The rendering camera's world forward; may be zero.</param>
        /// <param name="restRotationWS">
        /// The node's world orientation <em>before</em> billboarding — its animated pose composed
        /// into its parent. This is both the blend's starting point and the reference the arc and
        /// the wheel are measured from.
        /// </param>
        /// <param name="resultWS">The resolved world orientation, or the rest orientation.</param>
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

        /// <summary>
        /// Builds the camera-facing rotation a mode wants, before any offset, wheel or arc.
        /// </summary>
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

        /// <summary>
        /// An orthonormal rotation mapping local +Z onto <paramref name="forward"/> and local +Y as
        /// near <paramref name="up"/> as that allows.
        /// </summary>
        /// <remarks>
        /// Written out rather than delegated to <c>quaternion.LookRotationSafe</c> so the degenerate
        /// case is <em>this file's</em> decision and matches <c>ToolkitBillboardBasis</c> in the
        /// shader: when forward is parallel to up, pick a perpendicular rather than emit NaNs. A
        /// billboard that flashes for one frame as the camera crosses its axis is the classic
        /// symptom of leaving that case to a library.
        /// </remarks>
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

        /// <summary>
        /// Substitutes an authored yaw while keeping the camera-derived pitch.
        /// </summary>
        /// <remarks>
        /// Lifted from the host game's own system, which had it right: split the camera-facing
        /// target into yaw and pitch, discard the yaw, and rebuild with the frozen one. Writing a
        /// yaw rotation outright instead would leave a corpse staring flat ahead however far above
        /// it the camera sat.
        /// </remarks>
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

        /// <summary>
        /// Applies the snap wheel and the arc limit to the rotation about the reference axis,
        /// leaving every other component of the pose exactly as it was.
        /// </summary>
        /// <remarks>
        /// The difference from rest is split into a twist about the axis and a swing carrying
        /// everything else. Only the twist is quantised and limited; the swing is reassembled
        /// untouched. Snapping the whole delta instead would flatten a tilted rest pose onto the
        /// wheel, which is a different and much more visible change than the one asked for.
        /// </remarks>
        private static quaternion ApplyWheelAndArc(
            in BillboardSettings settings,
            float3 referenceAxis,
            quaternion restRotationWS,
            quaternion target)
        {
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

        /// <summary>Folds an angle into (−π, π].</summary>
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
