// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Builds minimal, otherwise-inert <see cref="RagdollBodyParams"/> / <see cref="RagdollBodyState"/>
    /// / <see cref="RagdollSolverSettings"/> values for the ragdoll solver's EditMode fixtures
    /// (<see cref="RagdollSolverTests"/>, <see cref="RagdollPlanarConstraintTests"/>,
    /// <see cref="RagdollSolverDeterminismTests"/>). Not part of the shipped package surface —
    /// <c>internal</c>, shared only within this assembly's tests — but kept in one place so the
    /// three fixtures agree on what "a default body" means instead of drifting apart in small,
    /// hard-to-notice ways.
    /// </summary>
    internal static class RagdollTestFactory
    {
        internal static RagdollSolverSettings DefaultSettings(RagdollSpace space = RagdollSpace.Spatial3D)
        {
            return new RagdollSolverSettings
            {
                space = space,
                worldGravity = float3.zero,
                gravityScale = 1f,
                frameRotation = quaternion.identity,
                planeOrigin = float3.zero,
                solverIterations = 6,
                substepDeltaTime = 1f / 120f,
                jointStiffness = 1f,
                jointDamping = 0.1f,
                sleepLinearSpeed = 0.01f,
                sleepAngularSpeed = 0.01f
            };
        }

        internal static RagdollBodyParams DefaultBodyParams(int parentBodyIndex)
        {
            return DefaultBodyParams(parentBodyIndex, 1f, new float3(0.25f, 0.5f, 0.25f));
        }

        internal static RagdollBodyParams DefaultBodyParams(int parentBodyIndex, float mass, float3 boxHalfExtents)
        {
            RagdollSolver.ComputeBoxInverseInertia(mass, boxHalfExtents, out float invMass, out float3 invInertiaDiagonal);
            RagdollBodyFlags flags = RagdollBodyFlags.CollidesWithWorld;
            if (parentBodyIndex < 0)
            {
                flags |= RagdollBodyFlags.IsRoot;
            }

            return new RagdollBodyParams
            {
                boxCenter = float3.zero,
                boxHalfExtents = boxHalfExtents,
                boxRotation = quaternion.identity,
                invMass = invMass,
                invInertiaDiagonal = invInertiaDiagonal,
                linearDamping = 0f,
                angularDamping = 0f,
                restitution = 0f,
                friction = 0.5f,
                limitMin = -math.PI,
                limitMax = math.PI,
                swingLimit = math.PI,
                twistLimit = math.PI,
                restRelativeRotation = quaternion.identity,
                parentAnchorOffset = float3.zero,
                parentBodyIndex = parentBodyIndex,
                selfGroup = 0,
                selfCollidesWith = 0xFF,
                flags = flags
            };
        }

        internal static RagdollBodyState DefaultBodyState(float3 position)
        {
            return new RagdollBodyState
            {
                position = position,
                orientation = quaternion.identity,
                linearVelocity = float3.zero,
                angularVelocity = float3.zero
            };
        }
    }

    /// <summary>
    /// <see cref="RagdollSolver"/>'s XPBD pipeline, exercised without a World (§10): joint pinning,
    /// limit clamping at both ends, damping decay, box-vs-box SAT, and the two ways a self-contact
    /// pair is excluded — parent/child (always) and self-collision masks (mutual admission only).
    /// </summary>
    public sealed class RagdollSolverTests
    {
        private const float PositionTolerance = 1e-3f;

        private static void AssertVectorsApproximatelyEqual(float3 expected, float3 actual, float tolerance, string because)
        {
            Assert.AreEqual(expected.x, actual.x, tolerance, because);
            Assert.AreEqual(expected.y, actual.y, tolerance, because);
            Assert.AreEqual(expected.z, actual.z, tolerance, because);
        }

        // -----------------------------------------------------------------------------------
        // Joint.
        // -----------------------------------------------------------------------------------

        [Test]
        public void JointConstraint_PullsTheChildsCenterToTheParentsAnchorPoint()
        {
            RagdollSolverSettings settings = RagdollTestFactory.DefaultSettings(RagdollSpace.Spatial3D);

            RagdollBodyParams rootParams = RagdollTestFactory.DefaultBodyParams(-1);
            rootParams.invMass = 0f;
            rootParams.invInertiaDiagonal = float3.zero;

            RagdollBodyParams childParams = RagdollTestFactory.DefaultBodyParams(0);
            childParams.parentAnchorOffset = new float3(0f, -1f, 0f);

            NativeArray<RagdollBodyParams> bodyParams = new NativeArray<RagdollBodyParams>(2, Allocator.Temp);
            bodyParams[0] = rootParams;
            bodyParams[1] = childParams;

            NativeArray<RagdollBodyState> bodyStates = new NativeArray<RagdollBodyState>(2, Allocator.Temp);
            bodyStates[0] = RagdollTestFactory.DefaultBodyState(float3.zero);
            bodyStates[1] = RagdollTestFactory.DefaultBodyState(new float3(2f, 3f, 0f));

            NativeArray<RagdollContact> worldContacts = new NativeArray<RagdollContact>(0, Allocator.Temp);

            for (int step = 0; step < 8; step++)
            {
                RagdollSolver.Step(settings, bodyParams, ref bodyStates, worldContacts, out bool _);
            }

            AssertVectorsApproximatelyEqual(
                float3.zero, bodyStates[0].position, 1e-5f,
                "A fully static parent (zero invMass and zero invInertia) must never move.");
            AssertVectorsApproximatelyEqual(
                new float3(0f, -1f, 0f), bodyStates[1].position, PositionTolerance,
                "The joint pins the child's own center of mass to the parent's anchor point.");

            bodyParams.Dispose();
            bodyStates.Dispose();
            worldContacts.Dispose();
        }

        // -----------------------------------------------------------------------------------
        // Limit.
        // -----------------------------------------------------------------------------------

        [TestCase(1f)]
        [TestCase(-1f)]
        public void LimitConstraintPlanar_ClampsTheHingeAngleAtBothEnds(float angularDirection)
        {
            RagdollSolverSettings settings = RagdollTestFactory.DefaultSettings(RagdollSpace.Planar2D);
            settings.worldGravity = float3.zero;

            RagdollBodyParams rootParams = RagdollTestFactory.DefaultBodyParams(-1);
            rootParams.invMass = 0f;
            rootParams.invInertiaDiagonal = float3.zero;

            RagdollBodyParams childParams = RagdollTestFactory.DefaultBodyParams(0);
            childParams.limitMin = -0.5f;
            childParams.limitMax = 0.5f;

            NativeArray<RagdollBodyParams> bodyParams = new NativeArray<RagdollBodyParams>(2, Allocator.Temp);
            bodyParams[0] = rootParams;
            bodyParams[1] = childParams;

            NativeArray<RagdollBodyState> bodyStates = new NativeArray<RagdollBodyState>(2, Allocator.Temp);
            bodyStates[0] = RagdollTestFactory.DefaultBodyState(float3.zero);
            RagdollBodyState childState = RagdollTestFactory.DefaultBodyState(float3.zero);
            childState.angularVelocity = new float3(0f, 0f, angularDirection * 20f);
            bodyStates[1] = childState;

            NativeArray<RagdollContact> worldContacts = new NativeArray<RagdollContact>(0, Allocator.Temp);

            for (int step = 0; step < 60; step++)
            {
                RagdollSolver.Step(settings, bodyParams, ref bodyStates, worldContacts, out bool _);
            }

            quaternion relative = math.mul(math.inverse(bodyStates[0].orientation), bodyStates[1].orientation);
            quaternion departure = math.mul(math.inverse(childParams.restRelativeRotation), relative);
            float finalAngle = BillboardMath.TwistAngle(departure, new float3(0f, 0f, 1f));

            Assert.LessOrEqual(
                finalAngle, childParams.limitMax + 0.05f,
                "A hinge spun hard toward the positive limit must settle at or inside limitMax, not fly past it.");
            Assert.GreaterOrEqual(
                finalAngle, childParams.limitMin - 0.05f,
                "A hinge spun hard toward the negative limit must settle at or inside limitMin, not fly past it.");
        }

        // -----------------------------------------------------------------------------------
        // Damping.
        // -----------------------------------------------------------------------------------

        [Test]
        public void Predict_DecaysLinearAndAngularVelocityByDampingEachSubstep()
        {
            RagdollSolverSettings settings = RagdollTestFactory.DefaultSettings(RagdollSpace.Spatial3D);
            settings.worldGravity = float3.zero;

            RagdollBodyParams bodyParams = RagdollTestFactory.DefaultBodyParams(-1);
            bodyParams.linearDamping = 0.5f;
            bodyParams.angularDamping = 0.3f;

            NativeArray<RagdollBodyParams> paramsArray = new NativeArray<RagdollBodyParams>(1, Allocator.Temp);
            paramsArray[0] = bodyParams;

            float3 initialLinearVelocity = new float3(2f, 0f, 0f);
            float3 initialAngularVelocity = new float3(0f, 1.5f, 0f);
            RagdollBodyState state = RagdollTestFactory.DefaultBodyState(float3.zero);
            state.linearVelocity = initialLinearVelocity;
            state.angularVelocity = initialAngularVelocity;

            NativeArray<RagdollBodyState> statesArray = new NativeArray<RagdollBodyState>(1, Allocator.Temp);
            statesArray[0] = state;

            NativeArray<RagdollContact> worldContacts = new NativeArray<RagdollContact>(0, Allocator.Temp);

            RagdollSolver.Step(settings, paramsArray, ref statesArray, worldContacts, out bool _);

            float expectedLinearSpeed =
                math.length(initialLinearVelocity) * (1f - bodyParams.linearDamping * settings.substepDeltaTime);
            float expectedAngularSpeed =
                math.length(initialAngularVelocity) * (1f - bodyParams.angularDamping * settings.substepDeltaTime);

            Assert.AreEqual(
                expectedLinearSpeed, math.length(statesArray[0].linearVelocity), 1e-4f,
                "A single free body with no other constraints should decay its speed by exactly (1 - damping*dt) per substep.");
            Assert.AreEqual(
                expectedAngularSpeed, math.length(statesArray[0].angularVelocity), 1e-3f,
                "Angular damping follows the same rule; the wider tolerance accounts for the quaternion integration and extraction not being perfectly inverse for a finite step.");

            paramsArray.Dispose();
            statesArray.Dispose();
            worldContacts.Dispose();
        }

        // -----------------------------------------------------------------------------------
        // Box-vs-box SAT.
        // -----------------------------------------------------------------------------------

        [Test]
        public void SelfContact_SeparatesTwoOverlappingUnrelatedBoxes()
        {
            RagdollSolverSettings settings = RagdollTestFactory.DefaultSettings(RagdollSpace.Spatial3D);
            settings.worldGravity = float3.zero;

            RagdollBodyParams bodyAParams = RagdollTestFactory.DefaultBodyParams(-1, 1f, new float3(0.5f, 0.5f, 0.5f));
            RagdollBodyParams bodyBParams = RagdollTestFactory.DefaultBodyParams(-1, 1f, new float3(0.5f, 0.5f, 0.5f));

            NativeArray<RagdollBodyParams> paramsArray = new NativeArray<RagdollBodyParams>(2, Allocator.Temp);
            paramsArray[0] = bodyAParams;
            paramsArray[1] = bodyBParams;

            NativeArray<RagdollBodyState> statesArray = new NativeArray<RagdollBodyState>(2, Allocator.Temp);
            statesArray[0] = RagdollTestFactory.DefaultBodyState(new float3(-0.3f, 0f, 0f));
            statesArray[1] = RagdollTestFactory.DefaultBodyState(new float3(0.3f, 0f, 0f));

            NativeArray<RagdollContact> worldContacts = new NativeArray<RagdollContact>(0, Allocator.Temp);

            for (int step = 0; step < 10; step++)
            {
                RagdollSolver.Step(settings, paramsArray, ref statesArray, worldContacts, out bool _);
            }

            float finalDistance = math.distance(statesArray[0].position, statesArray[1].position);
            Assert.GreaterOrEqual(
                finalDistance, 1f - 1e-3f,
                "Two 1x1x1 boxes overlapping along X (0.6 apart, 0.5+0.5 half-extents) must separate to at least the sum of their half-extents.");

            paramsArray.Dispose();
            statesArray.Dispose();
            worldContacts.Dispose();
        }

        // -----------------------------------------------------------------------------------
        // Self-collision exclusion: parent/child (unconditional) and masks (mutual).
        // -----------------------------------------------------------------------------------

        [Test]
        public void ShouldSelfCollide_AlwaysSkipsParentChildPairs_RegardlessOfMasks()
        {
            RagdollBodyParams parent = RagdollTestFactory.DefaultBodyParams(-1);
            parent.selfGroup = 0;
            parent.selfCollidesWith = 0xFF;

            RagdollBodyParams child = RagdollTestFactory.DefaultBodyParams(0);
            child.selfGroup = 0;
            child.selfCollidesWith = 0xFF;

            Assert.IsFalse(
                RagdollSolver.ShouldSelfCollide(parent, 0, child, 1),
                "Two boxes sharing a joint overlap by construction (§3.3); the pair must be skipped even though both masks admit every group.");
        }

        [Test]
        public void ShouldSelfCollide_RequiresBothMasksToAdmitTheOthersGroup()
        {
            RagdollBodyParams bodyA = RagdollTestFactory.DefaultBodyParams(-1);
            bodyA.selfGroup = 1;
            bodyA.selfCollidesWith = 1 << 2;

            RagdollBodyParams bodyB = RagdollTestFactory.DefaultBodyParams(-1);
            bodyB.selfGroup = 2;
            bodyB.selfCollidesWith = 1 << 2;

            Assert.IsFalse(
                RagdollSolver.ShouldSelfCollide(bodyA, 0, bodyB, 1),
                "A admits B's group (2), but B does not admit A's group (1); mutual agreement is required, so the pair must not collide.");

            bodyB.selfCollidesWith = (1 << 1) | (1 << 2);

            Assert.IsTrue(
                RagdollSolver.ShouldSelfCollide(bodyA, 0, bodyB, 1),
                "Once both masks admit the other's group, an otherwise-unrelated pair must collide.");
        }

        [Test]
        public void Step_NeverSeparatesOverlappingParentChildBoxes()
        {
            RagdollSolverSettings settings = RagdollTestFactory.DefaultSettings(RagdollSpace.Spatial3D);
            settings.worldGravity = float3.zero;

            RagdollBodyParams rootParams = RagdollTestFactory.DefaultBodyParams(-1, 1f, new float3(1f, 1f, 1f));
            rootParams.invMass = 0f;
            rootParams.invInertiaDiagonal = float3.zero;

            RagdollBodyParams childParams = RagdollTestFactory.DefaultBodyParams(0, 1f, new float3(1f, 1f, 1f));

            NativeArray<RagdollBodyParams> paramsArray = new NativeArray<RagdollBodyParams>(2, Allocator.Temp);
            paramsArray[0] = rootParams;
            paramsArray[1] = childParams;

            NativeArray<RagdollBodyState> statesArray = new NativeArray<RagdollBodyState>(2, Allocator.Temp);
            statesArray[0] = RagdollTestFactory.DefaultBodyState(float3.zero);
            statesArray[1] = RagdollTestFactory.DefaultBodyState(float3.zero);

            NativeArray<RagdollContact> worldContacts = new NativeArray<RagdollContact>(0, Allocator.Temp);

            for (int step = 0; step < 10; step++)
            {
                RagdollSolver.Step(settings, paramsArray, ref statesArray, worldContacts, out bool _);
            }

            Assert.AreEqual(
                0f, math.distance(statesArray[0].position, statesArray[1].position), 1e-4f,
                "Parent and child 1x1x1 boxes overlap completely by construction; self-contact must never push them apart, only the joint (already satisfied here) governs their relative position.");

            paramsArray.Dispose();
            statesArray.Dispose();
            worldContacts.Dispose();
        }
    }
}
