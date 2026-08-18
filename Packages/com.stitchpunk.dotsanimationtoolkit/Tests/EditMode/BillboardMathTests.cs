// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Amendment A44's orientation maths, exercised without a World. Every behaviour A44 promises —
    /// the facing convention, the per-mode constraint, the offset/snap/clamp order, and the blend —
    /// is pinned here, because this is the one file three callers share and the only place their
    /// agreement can be checked cheaply.
    /// </summary>
    public sealed class BillboardMathTests
    {
        private const float Tolerance = 1e-4f;

        private static BillboardSettings DefaultSettings(BillboardMode mode)
        {
            return new BillboardSettings
            {
                mode = mode,
                constraintAxis = new float3(0f, 1f, 0f),
                frozenYaw = 0f,
                angleOffsetRadians = 0f,
                blendWeight = 1f,
                enabled = true,
                snapSteps = 0,
                snapPhaseRadians = 0f,
                clampHalfArcRadians = -1f
            };
        }

        // -----------------------------------------------------------------------------------
        // Facing. The sign here is the correction A44 makes to the shipped package, so it is
        // asserted directly rather than only through a resolved rotation.
        // -----------------------------------------------------------------------------------

        [Test]
        public void ScreenAligned_TakesTheCameraForwardItself_NotItsNegation()
        {
            float3 cameraForward = math.normalize(new float3(1f, 0f, 2f));

            float3 facing;
            bool resolved = BillboardMath.TryResolveFacing(
                BillboardMode.ScreenAligned,
                float3.zero,
                new float3(0f, 0f, -10f),
                cameraForward,
                out facing);

            Assert.IsTrue(resolved);
            AssertVectorsEqual(
                cameraForward,
                math.normalize(facing),
                "The host game uses +cameraForward and A44 adopts it; the package shipped the " +
                "negation, which presents a Unity Quad's back to the viewer.");
        }

        [Test]
        public void SphericalModes_PointAwayFromTheCamera_SoAQuadShowsItsFrontFace()
        {
            float3 nodePosition = new float3(3f, 0f, 0f);
            float3 cameraPosition = new float3(0f, 0f, -10f);

            float3 facing;
            bool resolved = BillboardMath.TryResolveFacing(
                BillboardMode.Full, nodePosition, cameraPosition, float3.zero, out facing);

            Assert.IsTrue(resolved);
            AssertVectorsEqual(
                math.normalize(nodePosition - cameraPosition),
                math.normalize(facing),
                "Local +Z points away from the viewer, because Unity's Quad carries its visible " +
                "normal on -Z.");
        }

        [Test]
        public void ScreenAligned_FallsBackToSpherical_WhenTheHostWritesNoForward()
        {
            float3 nodePosition = new float3(0f, 0f, 4f);
            float3 cameraPosition = new float3(0f, 0f, -6f);

            float3 facing;
            bool resolved = BillboardMath.TryResolveFacing(
                BillboardMode.ScreenAligned, nodePosition, cameraPosition, float3.zero, out facing);

            Assert.IsTrue(resolved);
            AssertVectorsEqual(
                math.normalize(nodePosition - cameraPosition),
                math.normalize(facing),
                "A zero forward degrades to a different-but-correct look, never to a degenerate one.");
        }

        [Test]
        public void Facing_IsRefused_WhenTheNodeSitsExactlyOnTheCamera()
        {
            float3 sharedPosition = new float3(2f, 1f, 3f);

            float3 facing;
            bool resolved = BillboardMath.TryResolveFacing(
                BillboardMode.Full, sharedPosition, sharedPosition, float3.zero, out facing);

            Assert.IsFalse(resolved, "There is no direction to face, so the node is left alone.");
        }

        /// <summary>
        /// The single most important equivalence in A44: a screen-aligned root must reproduce the
        /// host game's own <c>quaternion.LookRotation(cameraForward, up)</c> exactly.
        /// </summary>
        [Test]
        public void ScreenAligned_ReproducesTheHostGamesLookRotation()
        {
            float3 cameraForward = math.normalize(new float3(0.4f, -0.3f, 1f));
            BillboardSettings settings = DefaultSettings(BillboardMode.ScreenAligned);

            quaternion result;
            bool resolved = BillboardMath.TryResolve(
                settings,
                new float3(5f, 2f, -1f),
                new float3(0f, 0f, -10f),
                cameraForward,
                quaternion.identity,
                out result);

            Assert.IsTrue(resolved);
            AssertRotationsEqual(
                quaternion.LookRotation(cameraForward, math.up()),
                result,
                "A44 preserves the host's turning behaviour; this is the line that says so.");
        }

        [Test]
        public void ScreenAligned_GivesEveryNodeTheSameRotation_WhereverItStands()
        {
            float3 cameraForward = math.normalize(new float3(1f, 0f, 1f));
            BillboardSettings settings = DefaultSettings(BillboardMode.ScreenAligned);

            quaternion nearResult;
            quaternion farResult;
            BillboardMath.TryResolve(
                settings, new float3(-8f, 0f, 0f), new float3(0f, 0f, -10f), cameraForward,
                quaternion.identity, out nearResult);
            BillboardMath.TryResolve(
                settings, new float3(9f, 3f, 5f), new float3(0f, 0f, -10f), cameraForward,
                quaternion.identity, out farResult);

            AssertRotationsEqual(
                nearResult, farResult, "Screen-aligned is the whole point of amendment A39.");
        }

        [Test]
        public void Full_TurnsEachNodeTowardTheCameraPoint_SoTwoNodesDiffer()
        {
            BillboardSettings settings = DefaultSettings(BillboardMode.Full);

            quaternion leftResult;
            quaternion rightResult;
            BillboardMath.TryResolve(
                settings, new float3(-8f, 0f, 0f), new float3(0f, 0f, -10f), float3.zero,
                quaternion.identity, out leftResult);
            BillboardMath.TryResolve(
                settings, new float3(8f, 0f, 0f), new float3(0f, 0f, -10f), float3.zero,
                quaternion.identity, out rightResult);

            Assert.Greater(
                math.degrees(AngleBetween(leftResult, rightResult)),
                10f,
                "Spherical billboards on opposite sides of the screen must not share a rotation.");
        }

        // -----------------------------------------------------------------------------------
        // Axis constraint. Local +Y lands on the reference axis for every constrained mode,
        // whatever the camera does — which is exactly what "never leans" means.
        // -----------------------------------------------------------------------------------

        [Test]
        public void Upright_KeepsItsUpAxisVertical_HoweverHighTheCameraSits()
        {
            BillboardSettings settings = DefaultSettings(BillboardMode.Upright);
            float3[] cameraPositions =
            {
                new float3(0f, 20f, -3f),
                new float3(-7f, 12f, 9f),
                new float3(4f, -8f, -6f)
            };

            for (int cameraIndex = 0; cameraIndex < cameraPositions.Length; cameraIndex++)
            {
                quaternion result;
                bool resolved = BillboardMath.TryResolve(
                    settings, float3.zero, cameraPositions[cameraIndex], float3.zero,
                    quaternion.identity, out result);

                Assert.IsTrue(resolved);
                AssertVectorsEqual(
                    math.up(),
                    math.mul(result, math.up()),
                    "An upright billboard never leans toward or away from the camera.");
            }
        }

        [Test]
        public void AxisConstrained_KeepsItsUpAxisOnTheAuthoredAxis()
        {
            float3 authoredAxis = math.normalize(new float3(1f, 0f, 0f));
            BillboardSettings settings = DefaultSettings(BillboardMode.AxisConstrained);
            settings.constraintAxis = authoredAxis;

            quaternion result;
            bool resolved = BillboardMath.TryResolve(
                settings, float3.zero, new float3(0f, 6f, -6f), float3.zero,
                quaternion.identity, out result);

            Assert.IsTrue(resolved);
            AssertVectorsEqual(
                authoredAxis,
                math.mul(result, math.up()),
                "A windmill sail turns about its hub, not about world up.");
        }

        /// <summary>A44 claims upright is exactly the axis-constrained case with world up.</summary>
        [Test]
        public void Upright_IsExactlyAxisConstrainedAboutWorldUp()
        {
            BillboardSettings uprightSettings = DefaultSettings(BillboardMode.Upright);
            BillboardSettings axisSettings = DefaultSettings(BillboardMode.AxisConstrained);
            axisSettings.constraintAxis = new float3(0f, 1f, 0f);
            float3 cameraPosition = new float3(-5f, 9f, 7f);

            quaternion uprightResult;
            quaternion axisResult;
            BillboardMath.TryResolve(
                uprightSettings, float3.zero, cameraPosition, float3.zero,
                quaternion.identity, out uprightResult);
            BillboardMath.TryResolve(
                axisSettings, float3.zero, cameraPosition, float3.zero,
                quaternion.identity, out axisResult);

            AssertRotationsEqual(
                uprightResult, axisResult, "The doc says they are the same mode; they must be.");
        }

        [Test]
        public void AxisConstrained_IsRefused_WhenTheAxisIsZeroLength()
        {
            BillboardSettings settings = DefaultSettings(BillboardMode.AxisConstrained);
            settings.constraintAxis = float3.zero;

            quaternion result;
            bool resolved = BillboardMath.TryResolve(
                settings, float3.zero, new float3(0f, 0f, -10f), float3.zero,
                quaternion.identity, out result);

            Assert.IsFalse(
                resolved,
                "Validation rule V23 reports this at authoring time; the runtime still refuses to " +
                "invent an axis.");
        }

        [Test]
        public void AxisConstrained_IsRefused_WhenTheCameraSitsOnTheAxisItself()
        {
            BillboardSettings settings = DefaultSettings(BillboardMode.Upright);

            quaternion result;
            bool resolved = BillboardMath.TryResolve(
                settings, float3.zero, new float3(0f, 10f, 0f), float3.zero,
                quaternion.identity, out result);

            Assert.IsFalse(
                resolved,
                "Every facing projects to nothing, so there is no non-arbitrary answer — the host's " +
                "system leaves the transform alone here and so does this.");
        }

        // -----------------------------------------------------------------------------------
        // Frozen yaw: the corpse case, carried over from the host verbatim.
        // -----------------------------------------------------------------------------------

        [Test]
        public void FrozenYaw_HoldsItsAuthoredYaw_WhereverTheCameraOrbitsTo()
        {
            float frozenYaw = math.radians(35f);
            BillboardSettings settings = DefaultSettings(BillboardMode.FrozenYaw);
            settings.frozenYaw = frozenYaw;
            float3[] cameraPositions =
            {
                new float3(0f, 8f, -10f),
                new float3(10f, 8f, 0f),
                new float3(-6f, 3f, 6f)
            };

            for (int cameraIndex = 0; cameraIndex < cameraPositions.Length; cameraIndex++)
            {
                quaternion result;
                bool resolved = BillboardMath.TryResolve(
                    settings, float3.zero, cameraPositions[cameraIndex], float3.zero,
                    quaternion.identity, out result);

                Assert.IsTrue(resolved);
                float3 resultForward = math.mul(result, math.forward());
                Assert.AreEqual(
                    frozenYaw,
                    math.atan2(resultForward.x, resultForward.z),
                    Tolerance,
                    "A corpse does not pirouette to follow the viewer.");
            }
        }

        [Test]
        public void FrozenYaw_StillTracksTheCamerasPitch_SoTheBodyIsNotSeenEdgeOn()
        {
            BillboardSettings settings = DefaultSettings(BillboardMode.FrozenYaw);
            settings.frozenYaw = math.radians(35f);

            quaternion result;
            BillboardMath.TryResolve(
                settings, float3.zero, new float3(0f, 12f, -4f), float3.zero,
                quaternion.identity, out result);

            float3 resultForward = math.mul(result, math.forward());
            Assert.Greater(
                math.abs(resultForward.y),
                0.1f,
                "Pitch keeps following the camera; only the yaw is held.");
        }

        // -----------------------------------------------------------------------------------
        // Angle helpers.
        // -----------------------------------------------------------------------------------

        [Test]
        public void SnapAngle_QuantisesToTheNearestStep()
        {
            Assert.AreEqual(
                math.radians(90f), BillboardMath.SnapAngle(math.radians(80f), 4, 0f), Tolerance);
            Assert.AreEqual(
                0f, BillboardMath.SnapAngle(math.radians(40f), 4, 0f), Tolerance);
            Assert.AreEqual(
                math.radians(45f), BillboardMath.SnapAngle(math.radians(50f), 8, 0f), Tolerance);
        }

        [Test]
        public void SnapAngle_IsANoOpBelowTwoSteps()
        {
            float arbitraryAngle = math.radians(37f);

            Assert.AreEqual(arbitraryAngle, BillboardMath.SnapAngle(arbitraryAngle, 1, 0f), Tolerance);
            Assert.AreEqual(arbitraryAngle, BillboardMath.SnapAngle(arbitraryAngle, 0, 0f), Tolerance);
        }

        [Test]
        public void SnapAngle_PhaseRotatesTheWholeWheel()
        {
            float phase = math.radians(45f);

            Assert.AreEqual(
                math.radians(45f),
                BillboardMath.SnapAngle(math.radians(40f), 4, phase),
                Tolerance,
                "With the wheel offset by half a step, 40 degrees lands on 45 rather than 0.");
        }

        [Test]
        public void ClampAngle_LimitsToTheArc()
        {
            Assert.AreEqual(
                math.radians(30f),
                BillboardMath.ClampAngle(math.radians(80f), math.radians(30f)),
                Tolerance);
            Assert.AreEqual(
                math.radians(-30f),
                BillboardMath.ClampAngle(math.radians(-80f), math.radians(30f)),
                Tolerance);
            Assert.AreEqual(
                math.radians(20f),
                BillboardMath.ClampAngle(math.radians(20f), math.radians(30f)),
                Tolerance,
                "An angle already inside the arc passes through untouched.");
        }

        /// <summary>
        /// The subtlety the wrap exists for: 350 degrees is 10 degrees the other way, not a value
        /// far outside a 90-degree arc.
        /// </summary>
        [Test]
        public void ClampAngle_WrapsBeforeClamping_SoANearlyFullTurnIsNotFlungToTheLimit()
        {
            Assert.AreEqual(
                math.radians(-10f),
                BillboardMath.ClampAngle(math.radians(350f), math.radians(45f)),
                Tolerance);
        }

        [Test]
        public void ClampAngle_IsANoOpWhenTheHalfArcIsNegative()
        {
            float arbitraryAngle = math.radians(140f);

            Assert.AreEqual(arbitraryAngle, BillboardMath.ClampAngle(arbitraryAngle, -1f), Tolerance);
        }

        [Test]
        public void TwistAngle_ReadsARotationAboutTheAxisAndIgnoresOneAcrossIt()
        {
            quaternion aboutUp = quaternion.RotateY(math.radians(50f));
            quaternion acrossUp = quaternion.RotateX(math.radians(50f));

            Assert.AreEqual(
                math.radians(50f), BillboardMath.TwistAngle(aboutUp, math.up()), Tolerance);
            Assert.AreEqual(
                0f, BillboardMath.TwistAngle(acrossUp, math.up()), Tolerance);
        }

        [Test]
        public void TwistAngle_IsUnchangedByQuaternionNegation()
        {
            quaternion rotation = quaternion.RotateY(math.radians(120f));
            quaternion negated = new quaternion(-rotation.value);

            Assert.AreEqual(
                BillboardMath.TwistAngle(rotation, math.up()),
                BillboardMath.TwistAngle(negated, math.up()),
                Tolerance,
                "A quaternion and its negation are the same rotation, so they must read the same " +
                "angle — otherwise the wheel and the arc disagree with themselves.");
        }

        // -----------------------------------------------------------------------------------
        // Offset, wheel, arc — and the order A44 fixes between them.
        // -----------------------------------------------------------------------------------

        [Test]
        public void AngleOffset_TurnsTheResultAboutTheReferenceAxis()
        {
            BillboardSettings baseSettings = DefaultSettings(BillboardMode.Upright);
            BillboardSettings offsetSettings = DefaultSettings(BillboardMode.Upright);
            offsetSettings.angleOffsetRadians = math.radians(90f);
            float3 cameraPosition = new float3(0f, 4f, -10f);

            quaternion baseResult;
            quaternion offsetResult;
            BillboardMath.TryResolve(
                baseSettings, float3.zero, cameraPosition, float3.zero,
                quaternion.identity, out baseResult);
            BillboardMath.TryResolve(
                offsetSettings, float3.zero, cameraPosition, float3.zero,
                quaternion.identity, out offsetResult);

            quaternion delta = math.mul(offsetResult, math.inverse(baseResult));
            Assert.AreEqual(
                math.radians(90f),
                BillboardMath.TwistAngle(delta, math.up()),
                Tolerance,
                "A keyed offset turns the rig away from the camera about its own vertical.");
        }

        [Test]
        public void Snapping_MeasuresFromTheRestOrientation_NotFromTheWorld()
        {
            quaternion restRotation = quaternion.RotateY(math.radians(20f));
            BillboardSettings settings = DefaultSettings(BillboardMode.ScreenAligned);
            settings.snapSteps = 4;

            quaternion result;
            BillboardMath.TryResolve(
                settings,
                float3.zero,
                new float3(0f, 0f, -10f),
                math.normalize(new float3(math.sin(math.radians(80f)), 0f, math.cos(math.radians(80f)))),
                restRotation,
                out result);

            float twistFromRest =
                BillboardMath.TwistAngle(math.mul(result, math.inverse(restRotation)), math.up());
            AssertIsMultipleOf(
                twistFromRest,
                math.radians(90f),
                "The wheel is anchored to the node's own rest orientation, so an animated turn " +
                "carries it along.");
        }

        /// <summary>
        /// A44 puts the offset before the wheel precisely so a keyed offset lands on a step rather
        /// than parking the rig between two.
        /// </summary>
        [Test]
        public void Snapping_IsAppliedAfterTheOffset_SoAKeyedOffsetLandsOnAStep()
        {
            BillboardSettings settings = DefaultSettings(BillboardMode.ScreenAligned);
            settings.snapSteps = 4;
            settings.angleOffsetRadians = math.radians(58f);

            quaternion result;
            BillboardMath.TryResolve(
                settings, float3.zero, new float3(0f, 0f, -10f), math.forward(),
                quaternion.identity, out result);

            // The camera alone would resolve to 0 degrees. The offset carries it to 58, which the
            // four-step wheel rounds up to 90. Snapping first would give 0 and then add the offset
            // back, landing at 58 — off every step, which is the bug this ordering avoids.
            Assert.AreEqual(
                math.radians(90f),
                BillboardMath.TwistAngle(result, math.up()),
                Tolerance,
                "An offset applied after the wheel would leave the rig 58 degrees off every step.");
        }

        [Test]
        public void Clamping_OutranksSnapping_AtTheArcBoundary()
        {
            float3 cameraForward =
                math.normalize(new float3(math.sin(math.radians(80f)), 0f, math.cos(math.radians(80f))));
            BillboardSettings settings = DefaultSettings(BillboardMode.ScreenAligned);
            settings.snapSteps = 4;
            settings.clampHalfArcRadians = math.radians(30f);

            quaternion result;
            BillboardMath.TryResolve(
                settings, float3.zero, new float3(0f, 0f, -10f), cameraForward,
                quaternion.identity, out result);

            Assert.AreEqual(
                math.radians(30f),
                BillboardMath.TwistAngle(result, math.up()),
                Tolerance,
                "The snap wants 90 degrees and the arc allows 30. The arc is a constraint and the " +
                "wheel is a look, so the result sits off-step at the boundary.");
        }

        [Test]
        public void Snapping_AloneReachesTheStepTheArcWouldHaveBlocked()
        {
            float3 cameraForward =
                math.normalize(new float3(math.sin(math.radians(80f)), 0f, math.cos(math.radians(80f))));
            BillboardSettings settings = DefaultSettings(BillboardMode.ScreenAligned);
            settings.snapSteps = 4;

            quaternion result;
            BillboardMath.TryResolve(
                settings, float3.zero, new float3(0f, 0f, -10f), cameraForward,
                quaternion.identity, out result);

            Assert.AreEqual(
                math.radians(90f),
                BillboardMath.TwistAngle(result, math.up()),
                Tolerance,
                "Without the arc the same input snaps to 90; the previous test's 30 is the arc's " +
                "doing and not the wheel's.");
        }

        [Test]
        public void Clamping_PinsTheNodeToItsRestOrientation_WhenTheArcIsZero()
        {
            quaternion restRotation = quaternion.RotateY(math.radians(20f));
            BillboardSettings settings = DefaultSettings(BillboardMode.ScreenAligned);
            settings.clampHalfArcRadians = 0f;

            quaternion result;
            BillboardMath.TryResolve(
                settings,
                float3.zero,
                new float3(0f, 0f, -10f),
                math.normalize(new float3(1f, 0f, 1f)),
                restRotation,
                out result);

            AssertRotationsEqual(
                restRotation,
                result,
                "A zero arc is a meaningful value — it pins the node — which is why the off " +
                "sentinel has to be negative rather than zero.");
        }

        /// <summary>
        /// Only the rotation about the reference axis is snapped or clamped; everything else about
        /// the pose travels through the decomposition untouched.
        /// </summary>
        [Test]
        public void TheWheelAndArcPathIsLossless_WhenNeitherLimitActuallyBites()
        {
            quaternion tiltedRest = math.mul(
                quaternion.RotateY(math.radians(25f)), quaternion.RotateX(math.radians(15f)));
            BillboardSettings untouchedSettings = DefaultSettings(BillboardMode.ScreenAligned);
            BillboardSettings wideArcSettings = DefaultSettings(BillboardMode.ScreenAligned);
            wideArcSettings.clampHalfArcRadians = math.PI;
            float3 cameraForward = math.normalize(new float3(0.3f, -0.2f, 1f));

            quaternion untouchedResult;
            quaternion wideArcResult;
            BillboardMath.TryResolve(
                untouchedSettings, float3.zero, new float3(0f, 0f, -10f), cameraForward,
                tiltedRest, out untouchedResult);
            BillboardMath.TryResolve(
                wideArcSettings, float3.zero, new float3(0f, 0f, -10f), cameraForward,
                tiltedRest, out wideArcResult);

            AssertRotationsEqual(
                untouchedResult,
                wideArcResult,
                "A tilted rest pose must survive the swing-twist round trip, or snapping would " +
                "quietly flatten every rig that is not axis-aligned at rest.");
        }

        // -----------------------------------------------------------------------------------
        // Blend and the refusals.
        // -----------------------------------------------------------------------------------

        [Test]
        public void BlendWeightZero_LeavesTheAnimatedPoseExactlyAsItWas()
        {
            quaternion restRotation = quaternion.RotateY(math.radians(70f));
            BillboardSettings settings = DefaultSettings(BillboardMode.ScreenAligned);
            settings.blendWeight = 0f;

            quaternion result;
            bool resolved = BillboardMath.TryResolve(
                settings, float3.zero, new float3(0f, 0f, -10f), math.forward(),
                restRotation, out result);

            Assert.IsTrue(resolved);
            AssertRotationsEqual(restRotation, result, "Zero weight is the animation's own pose.");
        }

        [Test]
        public void BlendWeightBetween_LandsBetweenThePoseAndTheBillboard()
        {
            quaternion restRotation = quaternion.RotateY(math.radians(90f));
            BillboardSettings fullSettings = DefaultSettings(BillboardMode.ScreenAligned);
            BillboardSettings halfSettings = DefaultSettings(BillboardMode.ScreenAligned);
            halfSettings.blendWeight = 0.5f;

            quaternion fullResult;
            quaternion halfResult;
            BillboardMath.TryResolve(
                fullSettings, float3.zero, new float3(0f, 0f, -10f), math.forward(),
                restRotation, out fullResult);
            BillboardMath.TryResolve(
                halfSettings, float3.zero, new float3(0f, 0f, -10f), math.forward(),
                restRotation, out halfResult);

            // The billboard target here is 0 degrees and the rest pose is +90, so the move is
            // negative: half way is -45 from rest, and fully billboarded is the whole -90.
            Assert.AreEqual(
                math.radians(-45f),
                BillboardMath.TwistAngle(math.mul(halfResult, math.inverse(restRotation)), math.up()),
                Tolerance,
                "Half way between a 90 degree rest and a fully billboarded 0 is 45.");
            Assert.AreEqual(
                math.radians(-90f),
                BillboardMath.TwistAngle(math.mul(fullResult, math.inverse(restRotation)), math.up()),
                Tolerance);
        }

        [Test]
        public void BlendWeight_IsSaturated_SoAKeyedOvershootCannotOverRotate()
        {
            quaternion restRotation = quaternion.RotateY(math.radians(90f));
            BillboardSettings saneSettings = DefaultSettings(BillboardMode.ScreenAligned);
            BillboardSettings overshootSettings = DefaultSettings(BillboardMode.ScreenAligned);
            overshootSettings.blendWeight = 3.5f;

            quaternion saneResult;
            quaternion overshootResult;
            BillboardMath.TryResolve(
                saneSettings, float3.zero, new float3(0f, 0f, -10f), math.forward(),
                restRotation, out saneResult);
            BillboardMath.TryResolve(
                overshootSettings, float3.zero, new float3(0f, 0f, -10f), math.forward(),
                restRotation, out overshootResult);

            AssertRotationsEqual(saneResult, overshootResult, "Weights above 1 clamp to 1.");
        }

        [Test]
        public void Off_ResolvesToNothing_AndReportsTheRestOrientation()
        {
            quaternion restRotation = quaternion.RotateY(math.radians(20f));
            BillboardSettings settings = DefaultSettings(BillboardMode.Off);

            quaternion result;
            bool resolved = BillboardMath.TryResolve(
                settings, float3.zero, new float3(0f, 0f, -10f), math.forward(),
                restRotation, out result);

            Assert.IsFalse(resolved);
            AssertRotationsEqual(restRotation, result, "A refusal still hands back a usable value.");
        }

        [Test]
        public void ADisabledRoot_ResolvesToNothing_SoAClipCanHandTheNodeBackToItsAnimation()
        {
            BillboardSettings settings = DefaultSettings(BillboardMode.ScreenAligned);
            settings.enabled = false;

            quaternion result;
            bool resolved = BillboardMath.TryResolve(
                settings, float3.zero, new float3(0f, 0f, -10f), math.forward(),
                quaternion.identity, out result);

            Assert.IsFalse(resolved);
        }

        [Test]
        public void ADefaultConstructedSettingsBlockIsInert()
        {
            BillboardSettings settings = new BillboardSettings();

            quaternion result;
            bool resolved = BillboardMath.TryResolve(
                settings, float3.zero, new float3(0f, 0f, -10f), math.forward(),
                quaternion.identity, out result);

            Assert.IsFalse(
                resolved,
                "A settings block nobody filled in must leave the pose alone rather than snap it " +
                "somewhere.");
        }

        // -----------------------------------------------------------------------------------
        // Assertion helpers.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The angle between two rotations, in radians.
        /// </summary>
        /// <remarks>
        /// <strong>Both are normalised first, and skipping that is a trap worth naming.</strong> A
        /// quaternion built from a basis carries a length of 1 only to float precision, so the raw
        /// dot product of a value with <em>itself</em> can read 0.9999998 — and <c>acos</c> near 1
        /// turns that last-bit error into a tenth of a degree. Two bit-identical rotations then
        /// appear to differ, which is a property of the measurement and not of the maths under test.
        /// </remarks>
        private static float AngleBetween(quaternion left, quaternion right)
        {
            float alignment =
                math.abs(math.dot(math.normalize(left.value), math.normalize(right.value)));
            return 2f * math.acos(math.min(alignment, 1f));
        }

        /// <summary>
        /// Asserts two rotations are the same, comparing components rather than an angle.
        /// </summary>
        /// <remarks>
        /// <c>acos</c> is ill-conditioned exactly where an equality assertion spends all its time —
        /// near zero difference — so the comparison is done on the normalised components, with the
        /// sign aligned because a quaternion and its negation are the same rotation. The threshold
        /// corresponds to roughly a quarter of a degree.
        /// </remarks>
        private static void AssertRotationsEqual(quaternion expected, quaternion actual, string because)
        {
            float4 expectedValue = math.normalize(expected.value);
            float4 actualValue = math.normalize(actual.value);
            if (math.dot(expectedValue, actualValue) < 0f)
            {
                actualValue = -actualValue;
            }

            Assert.Less(
                math.length(expectedValue - actualValue),
                2e-3f,
                because + " Expected " + expected + " but got " + actual + ".");
        }

        private static void AssertVectorsEqual(float3 expected, float3 actual, string because)
        {
            Assert.Less(
                math.length(expected - actual),
                1e-3f,
                because + " Expected " + expected + " but got " + actual + ".");
        }

        private static void AssertIsMultipleOf(float actual, float step, string because)
        {
            float stepCount = actual / step;
            Assert.Less(
                math.abs(stepCount - math.round(stepCount)),
                1e-3f,
                because + " " + math.degrees(actual) + " degrees is not a multiple of " +
                math.degrees(step) + ".");
        }
    }
}
