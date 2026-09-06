// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Pure static XPBD ragdoll solver, Burst-compiled, no ECS types in the math. One substep per
    /// <see cref="Step"/> call; bodies must be parent-before-child ordered, and state tracks each
    /// box's own center of mass and orientation, not the authored node's origin.
    /// </summary>
    [BurstCompile]
    public static class RagdollSolver
    {
        private const float PositionEpsilon = 1e-6f; // below this, a position-level correction is treated as already satisfied

        private const float AngularEpsilon = 1e-6f; // below this, an angular correction or decomposed axis is treated as zero/degenerate

        private const float MassOrInertiaEpsilon = 1e-8f; // below this, a mass or moment of inertia is treated as zero

        private const float AxisDegenerateEpsilonSq = 1e-10f; // below this squared length, a SAT candidate axis carries no separating information

        private const float ContactTouchSlop = 1e-3f; // world units; how close still counts as "touching" for the restitution/friction pass, since the position solve leaves contacts near-zero rather than still penetrating

        // -----------------------------------------------------------------------------------
        // Bake-time helpers. Not called by Step itself, but shared by whichever bakers and
        // preview builders populate RagdollBodyParams, so neither invents its own arithmetic.
        // -----------------------------------------------------------------------------------

        /// <summary>The closed-form box inertia tensor, inverted. Diagonal in the box's own local axes by construction, which is why <see cref="RagdollBodyParams.invInertiaDiagonal"/> stores three floats instead of a matrix.</summary>
        /// <param name="invMass">Zero when <paramref name="mass"/> is not positive.</param>
        /// <param name="invInertiaDiagonal">Zero on any axis whose moment of inertia is degenerate.</param>
        [BurstCompile]
        public static void ComputeBoxInverseInertia(
            float mass, in float3 boxHalfExtents, out float invMass, out float3 invInertiaDiagonal)
        {
            invMass = mass > MassOrInertiaEpsilon ? 1f / mass : 0f;

            float3 halfExtentsSquared = boxHalfExtents * boxHalfExtents;
            float3 momentOfInertia = (mass / 3f) * new float3(
                halfExtentsSquared.y + halfExtentsSquared.z,
                halfExtentsSquared.x + halfExtentsSquared.z,
                halfExtentsSquared.x + halfExtentsSquared.y);

            invInertiaDiagonal = new float3(
                momentOfInertia.x > MassOrInertiaEpsilon ? 1f / momentOfInertia.x : 0f,
                momentOfInertia.y > MassOrInertiaEpsilon ? 1f / momentOfInertia.y : 0f,
                momentOfInertia.z > MassOrInertiaEpsilon ? 1f / momentOfInertia.z : 0f);
        }

        /// <param name="authoredDamping">The body's own authored value; negative means inherit.</param>
        [BurstCompile]
        public static void ResolveDampingSentinel(
            float authoredDamping, float rigDefaultDamping, out float resolvedDamping)
        {
            resolvedDamping = authoredDamping < 0f ? rigDefaultDamping : authoredDamping;
        }

        /// <summary>Applies a launch impulse to one body's velocity — the death blow a host writes as <c>RagdollLaunch</c> before enabling a ragdoll.</summary>
        /// <param name="state">Its velocities are modified in place, never position or orientation — those change through the ordinary predict/solve steps that follow.</param>
        /// <param name="worldPoint">World-space point the impulse is applied at, for the torque its lever arm from the body's own center contributes.</param>
        /// <param name="worldTorque">World-space torque applied directly, independent of <paramref name="worldPoint"/>.</param>
        [BurstCompile]
        public static void ApplyLaunchImpulse(
            in RagdollBodyParams bodyParams, ref RagdollBodyState state,
            in float3 worldImpulse, in float3 worldPoint, in float3 worldTorque)
        {
            if (bodyParams.invMass <= 0f)
            {
                return;
            }

            state.linearVelocity += worldImpulse * bodyParams.invMass;

            quaternion inertialOrientation = math.mul(state.orientation, bodyParams.boxRotation);
            float3 leverArm = worldPoint - state.position;
            float3 totalTorque = math.cross(leverArm, worldImpulse) + worldTorque;
            state.angularVelocity += ApplyInverseInertiaWorld(
                in inertialOrientation, in bodyParams.invInertiaDiagonal, in totalTorque);
        }

        // A provider (e.g. the ground-plane fallback) must report a contact's distance as the gap
        // between the box's actual face and the surface, not center-to-surface, or every body sinks
        // in by its own half-height before the solver pushes back.
        /// <summary>An oriented box's projected half-width onto an arbitrary world-space axis.</summary>
        /// <param name="axis">The world-space axis to project onto — normalized.</param>
        [BurstCompile]
        public static void ComputeBoxProjectedRadius(
            in float3 halfExtents, in quaternion orientation, in float3 axis, out float radius)
        {
            radius = ProjectedRadius(in halfExtents, in orientation, in axis);
        }

        /// <param name="frameRotation">A frame resolved by <c>BillboardApi.TryGetFrame</c>, or identity for the documented fallback.</param>
        [BurstCompile]
        public static void ComputePlaneNormal(in quaternion frameRotation, out float3 planeNormal)
        {
            planeNormal = math.mul(frameRotation, new float3(0f, 0f, 1f));
        }

        /// <summary>
        /// Whether two bodies are allowed to self-collide at all: never a parent/child pair,
        /// regardless of masks, and otherwise only when each body's <see cref="RagdollBodyParams.selfCollidesWith"/>
        /// admits the other's <see cref="RagdollBodyParams.selfGroup"/> — both directions must agree.
        /// </summary>
        [BurstCompile]
        public static bool ShouldSelfCollide(
            in RagdollBodyParams bodyA, int indexA, in RagdollBodyParams bodyB, int indexB)
        {
            if (bodyA.parentBodyIndex == indexB || bodyB.parentBodyIndex == indexA)
            {
                return false;
            }
            bool bAdmitsA = ((bodyB.selfCollidesWith >> bodyA.selfGroup) & 1) != 0;
            bool aAdmitsB = ((bodyA.selfCollidesWith >> bodyB.selfGroup) & 1) != 0;
            return aAdmitsB && bAdmitsA;
        }

        // -----------------------------------------------------------------------------------
        // The solver itself.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Advances every body by exactly one fixed substep: predict, project constraints
        /// <see cref="RagdollSolverSettings.solverIterations"/> times, derive velocities, apply
        /// restitution and friction, and report whether the actor is quiet enough to be a sleep
        /// candidate. One substep, not one frame — owning the fixed-step accumulator is the
        /// caller's job. Every loop is index-ascending and nothing reads wall-clock time or
        /// randomness, so identical inputs produce bit-identical outputs.
        /// </summary>
        /// <param name="bodyParams">Per-body constant configuration, one entry per body, parent-before-child ordered.</param>
        /// <param name="bodyStates">Same ordering and length as <paramref name="bodyParams"/>; written in place.</param>
        /// <param name="belowSleepThreshold">True when every body's speed after this substep is below both sleep thresholds.</param>
        [BurstCompile]
        public static void Step(
            in RagdollSolverSettings settings,
            in NativeArray<RagdollBodyParams> bodyParams,
            ref NativeArray<RagdollBodyState> bodyStates,
            in NativeArray<RagdollContact> worldContacts,
            out bool belowSleepThreshold)
        {
            belowSleepThreshold = true;
            int bodyCount = bodyStates.Length;
            if (bodyCount == 0)
            {
                return;
            }

            NativeArray<float3> previousPositions = new NativeArray<float3>(bodyCount, Allocator.Temp);
            NativeArray<quaternion> previousOrientations = new NativeArray<quaternion>(bodyCount, Allocator.Temp);

            float3 planeNormal = float3.zero;
            if (settings.space == RagdollSpace.Planar2D)
            {
                ComputePlaneNormal(in settings.frameRotation, out planeNormal);
            }
            ResolveStepGravity(in settings, out float3 gravity);

            for (int bodyIndex = 0; bodyIndex < bodyCount; bodyIndex++)
            {
                RagdollBodyState state = bodyStates[bodyIndex];
                previousPositions[bodyIndex] = state.position;
                previousOrientations[bodyIndex] = state.orientation;

                RagdollBodyParams currentParams = bodyParams[bodyIndex];
                PredictBody(in currentParams, in gravity, settings.substepDeltaTime, ref state);
                bodyStates[bodyIndex] = state;
            }

            int iterationCount = math.max((int)settings.solverIterations, 1);
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                // Joint + limit, parent-before-child ascending.
                for (int bodyIndex = 0; bodyIndex < bodyCount; bodyIndex++)
                {
                    RagdollBodyParams childParams = bodyParams[bodyIndex];
                    if (childParams.parentBodyIndex < 0)
                    {
                        continue;
                    }

                    int parentIndex = childParams.parentBodyIndex;
                    RagdollBodyParams parentParams = bodyParams[parentIndex];
                    RagdollBodyState parentState = bodyStates[parentIndex];
                    RagdollBodyState childState = bodyStates[bodyIndex];

                    SolveJointConstraint(in parentParams, ref parentState, in childParams, ref childState);
                    if (settings.space == RagdollSpace.Planar2D)
                    {
                        SolveLimitConstraintPlanar(
                            in parentParams, ref parentState, in childParams, ref childState,
                            in planeNormal, settings.jointStiffness, settings.jointDamping);
                    }
                    else
                    {
                        SolveLimitConstraintSpatial(
                            in parentParams, ref parentState, in childParams, ref childState,
                            settings.jointStiffness, settings.jointDamping);
                    }

                    bodyStates[parentIndex] = parentState;
                    bodyStates[bodyIndex] = childState;
                }

                // Self-contact, ascending pair order i < j (determinism).
                for (int indexA = 0; indexA < bodyCount; indexA++)
                {
                    RagdollBodyParams paramsA = bodyParams[indexA];
                    for (int indexB = indexA + 1; indexB < bodyCount; indexB++)
                    {
                        RagdollBodyParams paramsB = bodyParams[indexB];
                        if (!ShouldSelfCollide(in paramsA, indexA, in paramsB, indexB))
                        {
                            continue;
                        }

                        RagdollBodyState stateA = bodyStates[indexA];
                        RagdollBodyState stateB = bodyStates[indexB];
                        CorrectContactPosition(in paramsA, ref stateA, in paramsB, ref stateB);
                        bodyStates[indexA] = stateA;
                        bodyStates[indexB] = stateB;
                    }
                }

                // World contact, ascending contact index (determinism).
                for (int contactIndex = 0; contactIndex < worldContacts.Length; contactIndex++)
                {
                    RagdollContact contact = worldContacts[contactIndex];
                    if (contact.bodyIndex < 0 || contact.bodyIndex >= bodyCount)
                    {
                        continue;
                    }
                    RagdollBodyParams contactBodyParams = bodyParams[contact.bodyIndex];
                    if (!contactBodyParams.CollidesWithWorld)
                    {
                        continue;
                    }

                    RagdollBodyState contactBodyState = bodyStates[contact.bodyIndex];
                    // referencePosition is where the provider measured contact.distance, never this
                    // substep's start — see RagdollContact.referencePosition for why mixing those
                    // two moments pumps energy in across substeps.
                    float3 contactReferencePosition = contact.referencePosition;
                    CorrectWorldContactPosition(
                        in contactBodyParams, ref contactBodyState, in contact,
                        in contactReferencePosition);
                    bodyStates[contact.bodyIndex] = contactBodyState;
                }

                // Plane solved last: SAT contact correction can push a body out of plane, and only
                // a following plane pass removes that component.
                if (settings.space == RagdollSpace.Planar2D)
                {
                    for (int bodyIndex = 0; bodyIndex < bodyCount; bodyIndex++)
                    {
                        RagdollBodyState state = bodyStates[bodyIndex];
                        SolvePlaneConstraint(ref state, in settings.planeOrigin, in planeNormal, in settings.frameRotation);
                        bodyStates[bodyIndex] = state;
                    }
                }
            }

            // Derive velocities from the position delta before restitution/friction touch them, so
            // the baseline is exactly what the position solve implies — fully inelastic, by construction.
            for (int bodyIndex = 0; bodyIndex < bodyCount; bodyIndex++)
            {
                RagdollBodyState state = bodyStates[bodyIndex];
                float3 previousPosition = previousPositions[bodyIndex];
                quaternion previousOrientation = previousOrientations[bodyIndex];
                DeriveVelocity(in previousPosition, in previousOrientation, settings.substepDeltaTime, ref state);
                bodyStates[bodyIndex] = state;
            }

            for (int indexA = 0; indexA < bodyCount; indexA++)
            {
                RagdollBodyParams paramsA = bodyParams[indexA];
                for (int indexB = indexA + 1; indexB < bodyCount; indexB++)
                {
                    RagdollBodyParams paramsB = bodyParams[indexB];
                    if (!ShouldSelfCollide(in paramsA, indexA, in paramsB, indexB))
                    {
                        continue;
                    }

                    RagdollBodyState stateA = bodyStates[indexA];
                    RagdollBodyState stateB = bodyStates[indexB];
                    ApplyContactVelocityResponse(in paramsA, ref stateA, in paramsB, ref stateB);
                    bodyStates[indexA] = stateA;
                    bodyStates[indexB] = stateB;
                }
            }

            for (int contactIndex = 0; contactIndex < worldContacts.Length; contactIndex++)
            {
                RagdollContact contact = worldContacts[contactIndex];
                if (contact.bodyIndex < 0 || contact.bodyIndex >= bodyCount)
                {
                    continue;
                }
                RagdollBodyParams contactBodyParams = bodyParams[contact.bodyIndex];
                if (!contactBodyParams.CollidesWithWorld)
                {
                    continue;
                }

                RagdollBodyState contactBodyState = bodyStates[contact.bodyIndex];
                ApplyWorldContactVelocityResponse(in contactBodyParams, ref contactBodyState, in contact);
                bodyStates[contact.bodyIndex] = contactBodyState;
            }

            belowSleepThreshold = true;
            for (int bodyIndex = 0; bodyIndex < bodyCount; bodyIndex++)
            {
                RagdollBodyState state = bodyStates[bodyIndex];
                float sleepLinearSpeedSq = settings.sleepLinearSpeed * settings.sleepLinearSpeed;
                float sleepAngularSpeedSq = settings.sleepAngularSpeed * settings.sleepAngularSpeed;
                if (math.lengthsq(state.linearVelocity) >= sleepLinearSpeedSq
                    || math.lengthsq(state.angularVelocity) >= sleepAngularSpeedSq)
                {
                    belowSleepThreshold = false;
                    break;
                }
            }

            previousPositions.Dispose();
            previousOrientations.Dispose();
        }

        // -----------------------------------------------------------------------------------
        // Predict.
        // -----------------------------------------------------------------------------------

        /// <summary>Integrates gravity and damping into velocity, then predicts position and orientation forward by one substep.</summary>
        private static void PredictBody(
            in RagdollBodyParams bodyParams, in float3 gravity, float deltaTime, ref RagdollBodyState state)
        {
            if (bodyParams.invMass <= 0f)
            {
                // invMass 0 = fully static: both linear and angular freeze, not just position.
                state.linearVelocity = float3.zero;
                state.angularVelocity = float3.zero;
                return;
            }

            state.linearVelocity += gravity * deltaTime;
            state.linearVelocity *= math.max(0f, 1f - bodyParams.linearDamping * deltaTime);
            state.angularVelocity *= math.max(0f, 1f - bodyParams.angularDamping * deltaTime);

            state.position += state.linearVelocity * deltaTime;
            state.orientation = IntegrateOrientationByVelocity(in state.orientation, in state.angularVelocity, deltaTime);
        }

        /// <summary>
        /// Planar2D's gravity: <c>worldGravity</c> expressed in the billboard frame with its z
        /// discarded, then rotated back to world space — the largest world-space vector lying
        /// entirely within the frame's plane. Spatial3D uses <see cref="RagdollSolverSettings.worldGravity"/> directly.
        /// </summary>
        private static void ResolveStepGravity(in RagdollSolverSettings settings, out float3 gravity)
        {
            float3 scaledGravity = settings.worldGravity * settings.gravityScale;
            if (settings.space != RagdollSpace.Planar2D)
            {
                gravity = scaledGravity;
                return;
            }

            BillboardApi.ToBillboardSpace(in settings.frameRotation, in scaledGravity, out float3 planarGravity);
            planarGravity.z = 0f;
            gravity = math.mul(settings.frameRotation, planarGravity);
        }

        // -----------------------------------------------------------------------------------
        // Joint + limit.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Pins the child body's own center of mass to the parent's anchor point, rigidly. The
        /// parent carries both a linear and angular correction (its anchor sits away from its own
        /// center, per <see cref="RagdollBodyParams.parentAnchorOffset"/>); the child carries only a
        /// linear one, because its anchor is its center.
        /// </summary>
        private static void SolveJointConstraint(
            in RagdollBodyParams parentParams, ref RagdollBodyState parentState,
            in RagdollBodyParams childParams, ref RagdollBodyState childState)
        {
            float3 leverArmParent = math.mul(parentState.orientation, childParams.parentAnchorOffset);
            float3 parentAnchorWorld = parentState.position + leverArmParent;
            float3 childAnchorWorld = childState.position;

            float3 separation = parentAnchorWorld - childAnchorWorld;
            float distance = math.length(separation);
            if (distance < PositionEpsilon)
            {
                return;
            }
            float3 normal = separation / distance;

            quaternion parentInertialOrientation = math.mul(parentState.orientation, parentParams.boxRotation);
            float angularWeightParent = AngularInverseMass(
                in parentParams.invInertiaDiagonal, in parentInertialOrientation, in leverArmParent, in normal);
            float weightParent = parentParams.invMass + angularWeightParent;
            float weightChild = childParams.invMass;

            float denom = weightParent + weightChild;
            if (denom < PositionEpsilon)
            {
                return;
            }

            // XPBD two-body derivation: impulse p = delta-lambda * n, pointing from the parent's
            // anchor toward the child's.
            float3 impulse = -normal * (distance / denom);

            parentState.position += impulse * parentParams.invMass;
            childState.position -= impulse * childParams.invMass;
            if (angularWeightParent > MassOrInertiaEpsilon)
            {
                parentState.orientation = ApplyAngularImpulse(
                    in parentInertialOrientation, in parentParams.invInertiaDiagonal,
                    in leverArmParent, in impulse, in parentState.orientation);
            }
        }

        /// <summary>Clamps the Planar2D hinge angle into [limitMin, limitMax], measured as the twist of the departure from <see cref="RagdollBodyParams.restRelativeRotation"/> about the frame normal.</summary>
        private static void SolveLimitConstraintPlanar(
            in RagdollBodyParams parentParams, ref RagdollBodyState parentState,
            in RagdollBodyParams childParams, ref RagdollBodyState childState,
            in float3 planeNormal, float jointStiffness, float jointDamping)
        {
            quaternion relative = math.mul(math.inverse(parentState.orientation), childState.orientation);
            quaternion departure = math.mul(math.inverse(childParams.restRelativeRotation), relative);

            float currentAngle = BillboardMath.TwistAngle(in departure, in planeNormal);
            float clampedAngle = math.clamp(currentAngle, childParams.limitMin, childParams.limitMax);
            float overshoot = clampedAngle - currentAngle;
            if (math.abs(overshoot) < AngularEpsilon)
            {
                return;
            }

            ApplyAngularOnlyCorrection(
                in parentParams, ref parentState, in childParams, ref childState,
                in planeNormal, overshoot, jointStiffness, jointDamping);
        }

        /// <summary>Clamps a Spatial3D joint's swing and twist independently. Twist is about the child's rest-local +Y; swing is whatever rotation remains, clamped to a cone half-angle.</summary>
        private static void SolveLimitConstraintSpatial(
            in RagdollBodyParams parentParams, ref RagdollBodyState parentState,
            in RagdollBodyParams childParams, ref RagdollBodyState childState,
            float jointStiffness, float jointDamping)
        {
            quaternion relative = math.mul(math.inverse(parentState.orientation), childState.orientation);
            quaternion departure = math.mul(math.inverse(childParams.restRelativeRotation), relative);

            float3 twistAxisLocal = new float3(0f, 1f, 0f);
            float twistAngle = BillboardMath.TwistAngle(in departure, in twistAxisLocal);
            quaternion twistOnly = quaternion.AxisAngle(twistAxisLocal, twistAngle);
            quaternion swingOnly = math.mul(departure, math.inverse(twistOnly));

            float4 swingValue = swingOnly.value;
            if (swingValue.w < 0f)
            {
                swingValue = -swingValue;
            }
            float swingAngle = 2f * math.atan2(math.length(swingValue.xyz), swingValue.w);
            float swingAxisLengthSq = math.lengthsq(swingValue.xyz);
            float3 swingAxisLocal = swingAxisLengthSq > AngularEpsilon * AngularEpsilon
                ? swingValue.xyz * math.rsqrt(swingAxisLengthSq)
                : new float3(1f, 0f, 0f);

            float clampedTwistAngle = math.clamp(twistAngle, -childParams.twistLimit, childParams.twistLimit);
            float twistOvershoot = clampedTwistAngle - twistAngle;

            float clampedSwingAngle = math.clamp(swingAngle, 0f, childParams.swingLimit);
            float swingOvershoot = clampedSwingAngle - swingAngle;

            // Both axes are in the joint's own rest-relative frame; reaching world space goes
            // through the parent's current orientation, the same frame restRelativeRotation is
            // itself defined against.
            if (math.abs(twistOvershoot) >= AngularEpsilon)
            {
                float3 worldTwistAxis = math.mul(parentState.orientation, twistAxisLocal);
                ApplyAngularOnlyCorrection(
                    in parentParams, ref parentState, in childParams, ref childState,
                    in worldTwistAxis, twistOvershoot, jointStiffness, jointDamping);
            }
            if (math.abs(swingOvershoot) >= AngularEpsilon)
            {
                float3 worldSwingAxis = math.mul(parentState.orientation, swingAxisLocal);
                ApplyAngularOnlyCorrection(
                    in parentParams, ref parentState, in childParams, ref childState,
                    in worldSwingAxis, swingOvershoot, jointStiffness, jointDamping);
            }
        }

        /// <summary>
        /// Rotates parent and child apart (or together) about a shared world axis by
        /// <paramref name="correctionAngle"/> total, split by their angular inverse mass about that
        /// axis and scaled by <paramref name="jointStiffness"/>. <paramref name="jointDamping"/>
        /// additionally bleeds off the axis-aligned angular velocity of both bodies while a limit is
        /// actively correcting, so a joint settles at its stop rather than ringing against it.
        /// </summary>
        private static void ApplyAngularOnlyCorrection(
            in RagdollBodyParams parentParams, ref RagdollBodyState parentState,
            in RagdollBodyParams childParams, ref RagdollBodyState childState,
            in float3 axis, float correctionAngle, float jointStiffness, float jointDamping)
        {
            quaternion parentInertialOrientation = math.mul(parentState.orientation, parentParams.boxRotation);
            quaternion childInertialOrientation = math.mul(childState.orientation, childParams.boxRotation);

            float parentAngularWeight = AngularSpin(in parentParams.invInertiaDiagonal, in parentInertialOrientation, in axis);
            float childAngularWeight = AngularSpin(in childParams.invInertiaDiagonal, in childInertialOrientation, in axis);
            float denom = parentAngularWeight + childAngularWeight;
            if (denom < AngularEpsilon)
            {
                return;
            }

            float softenedAngle = correctionAngle * math.saturate(jointStiffness);
            float parentAngle = -softenedAngle * (parentAngularWeight / denom);
            float childAngle = softenedAngle * (childAngularWeight / denom);

            parentState.orientation = RotateOrientationByAxisAngle(in parentState.orientation, in axis, parentAngle);
            childState.orientation = RotateOrientationByAxisAngle(in childState.orientation, in axis, childAngle);

            float damping = math.saturate(jointDamping);
            if (damping > 0f)
            {
                parentState.angularVelocity -= axis * (math.dot(parentState.angularVelocity, axis) * damping);
                childState.angularVelocity -= axis * (math.dot(childState.angularVelocity, axis) * damping);
            }
        }

        // -----------------------------------------------------------------------------------
        // Plane (Planar2D only).
        // -----------------------------------------------------------------------------------

        /// <summary>Projects a body's position onto the frame's XY plane and its orientation onto a pure rotation about the frame normal — exactly, not iteratively.</summary>
        private static void SolvePlaneConstraint(
            ref RagdollBodyState state, in float3 planeOrigin, in float3 planeNormal, in quaternion frameRotation)
        {
            float distanceFromPlane = math.dot(state.position - planeOrigin, planeNormal);
            state.position -= planeNormal * distanceFromPlane;

            quaternion remainder = math.mul(math.inverse(frameRotation), state.orientation);
            float3 localZAxis = new float3(0f, 0f, 1f);
            float twistAngle = BillboardMath.TwistAngle(in remainder, in localZAxis);
            state.orientation = math.normalize(math.mul(frameRotation, quaternion.AxisAngle(localZAxis, twistAngle)));
        }

        // -----------------------------------------------------------------------------------
        // Self- and world-contact: position correction (every iteration) and velocity response
        // (once, after derivation). Linear-only: a penetrating body is pushed along the contact
        // normal with no torque from the contact point's offset from center.
        // -----------------------------------------------------------------------------------

        /// <summary>Separates two overlapping boxes along the SAT minimum-translation axis, weighted by inverse mass. No-ops when the boxes do not overlap.</summary>
        private static void CorrectContactPosition(
            in RagdollBodyParams paramsA, ref RagdollBodyState stateA,
            in RagdollBodyParams paramsB, ref RagdollBodyState stateB)
        {
            quaternion orientationA = math.mul(stateA.orientation, paramsA.boxRotation);
            quaternion orientationB = math.mul(stateB.orientation, paramsB.boxRotation);
            if (!TestBoxOverlap(
                    in stateA.position, in paramsA.boxHalfExtents, in orientationA,
                    in stateB.position, in paramsB.boxHalfExtents, in orientationB,
                    0f, out float3 normal, out float depth))
            {
                return;
            }

            float denom = paramsA.invMass + paramsB.invMass;
            if (denom < PositionEpsilon)
            {
                return;
            }
            float3 correction = normal * (depth / denom);
            stateA.position -= correction * paramsA.invMass;
            stateB.position += correction * paramsB.invMass;
        }

        /// <summary>Pushes a body out of a penetrating world contact along its normal. No-ops when the contact is not penetrating (any longer) or the body is static.</summary>
        /// <param name="referencePosition">
        /// The body's position when the probe measured <paramref name="contact"/>'s distance, held
        /// fixed across every substep of the frame. This call runs once per solver iteration, but
        /// the contact is only measured once per frame: reusing the raw <c>contact.distance</c>
        /// every iteration would reapply the full original correction each time instead of
        /// converging, pumping energy into the body. Re-deriving current penetration from how far
        /// the body has moved since <paramref name="referencePosition"/> avoids that.
        /// </param>
        private static void CorrectWorldContactPosition(
            in RagdollBodyParams bodyParams, ref RagdollBodyState state, in RagdollContact contact,
            in float3 referencePosition)
        {
            if (bodyParams.invMass <= 0f)
            {
                return;
            }
            float movedAlongNormal = math.dot(state.position - referencePosition, contact.normal);
            float currentDistance = contact.distance + movedAlongNormal;
            if (currentDistance >= 0f)
            {
                return;
            }
            state.position -= contact.normal * currentDistance;
        }

        /// <summary>
        /// Restitution and friction between two self-colliding bodies still touching (within
        /// <see cref="ContactTouchSlop"/>) and still closing along the contact normal. Restitution
        /// combines by the larger of the two bodies' values; friction by their geometric mean.
        /// </summary>
        private static void ApplyContactVelocityResponse(
            in RagdollBodyParams paramsA, ref RagdollBodyState stateA,
            in RagdollBodyParams paramsB, ref RagdollBodyState stateB)
        {
            quaternion orientationA = math.mul(stateA.orientation, paramsA.boxRotation);
            quaternion orientationB = math.mul(stateB.orientation, paramsB.boxRotation);
            if (!TestBoxOverlap(
                    in stateA.position, in paramsA.boxHalfExtents, in orientationA,
                    in stateB.position, in paramsB.boxHalfExtents, in orientationB,
                    ContactTouchSlop, out float3 normal, out float _))
            {
                return;
            }

            float3 relativeVelocity = stateA.linearVelocity - stateB.linearVelocity;
            float normalSpeed = math.dot(relativeVelocity, normal);
            if (normalSpeed >= 0f)
            {
                return;
            }

            float massSum = paramsA.invMass + paramsB.invMass;
            if (massSum < PositionEpsilon)
            {
                return;
            }

            float restitution = math.max(paramsA.restitution, paramsB.restitution);
            float friction = math.sqrt(math.max(paramsA.friction, 0f) * math.max(paramsB.friction, 0f));
            float3 tangentVelocity = relativeVelocity - normal * normalSpeed;
            float3 targetRelativeVelocity =
                normal * (-normalSpeed * restitution) + tangentVelocity * (1f - math.saturate(friction));
            float3 deltaVelocity = targetRelativeVelocity - relativeVelocity;

            stateA.linearVelocity += deltaVelocity * (paramsA.invMass / massSum);
            stateB.linearVelocity -= deltaVelocity * (paramsB.invMass / massSum);
        }

        /// <summary>Restitution and friction between a body and a still-touching, still-closing world contact.</summary>
        private static void ApplyWorldContactVelocityResponse(
            in RagdollBodyParams bodyParams, ref RagdollBodyState state, in RagdollContact contact)
        {
            if (contact.distance > ContactTouchSlop)
            {
                return;
            }
            float normalSpeed = math.dot(state.linearVelocity, contact.normal);
            if (normalSpeed >= 0f)
            {
                return;
            }

            float restitution = math.max(bodyParams.restitution, contact.restitution);
            float friction = math.sqrt(math.max(bodyParams.friction, 0f) * math.max(contact.friction, 0f));
            float3 tangentVelocity = state.linearVelocity - contact.normal * normalSpeed;
            state.linearVelocity =
                contact.normal * (-normalSpeed * restitution) + tangentVelocity * (1f - math.saturate(friction));
        }

        /// <summary>15-axis SAT between two oriented boxes (3 face normals of each, plus 9 pairwise edge-cross products), reporting the minimum-penetration axis and depth when they overlap.</summary>
        /// <param name="slop">Subtracted from the true penetration depth before the overlap decision — 0 for an actual position correction, a small positive tolerance to also catch bodies left just touching.</param>
        /// <param name="normal">Oriented from <paramref name="centerA"/> toward <paramref name="centerB"/>.</param>
        /// <param name="depth">Meaningless when this method returns false.</param>
        private static bool TestBoxOverlap(
            in float3 centerA, in float3 halfExtentsA, in quaternion orientationA,
            in float3 centerB, in float3 halfExtentsB, in quaternion orientationB,
            float slop,
            out float3 normal, out float depth)
        {
            float3 axisA0 = math.mul(orientationA, new float3(1f, 0f, 0f));
            float3 axisA1 = math.mul(orientationA, new float3(0f, 1f, 0f));
            float3 axisA2 = math.mul(orientationA, new float3(0f, 0f, 1f));
            float3 axisB0 = math.mul(orientationB, new float3(1f, 0f, 0f));
            float3 axisB1 = math.mul(orientationB, new float3(0f, 1f, 0f));
            float3 axisB2 = math.mul(orientationB, new float3(0f, 0f, 1f));
            float3 centerDelta = centerB - centerA;

            normal = axisA0;
            depth = float.MaxValue;
            bool foundUsableAxis = false;

            if (!TestSeparatingAxis(in axisA0, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in axisA1, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in axisA2, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in axisB0, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in axisB1, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in axisB2, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }

            float3 crossA0B0 = math.cross(axisA0, axisB0);
            float3 crossA0B1 = math.cross(axisA0, axisB1);
            float3 crossA0B2 = math.cross(axisA0, axisB2);
            float3 crossA1B0 = math.cross(axisA1, axisB0);
            float3 crossA1B1 = math.cross(axisA1, axisB1);
            float3 crossA1B2 = math.cross(axisA1, axisB2);
            float3 crossA2B0 = math.cross(axisA2, axisB0);
            float3 crossA2B1 = math.cross(axisA2, axisB1);
            float3 crossA2B2 = math.cross(axisA2, axisB2);

            if (!TestSeparatingAxis(in crossA0B0, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in crossA0B1, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in crossA0B2, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in crossA1B0, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in crossA1B1, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in crossA1B2, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in crossA2B0, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in crossA2B1, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }
            if (!TestSeparatingAxis(in crossA2B2, in centerDelta, in halfExtentsA, in orientationA, in halfExtentsB, in orientationB, ref normal, ref depth, ref foundUsableAxis)) { return false; }

            if (!foundUsableAxis)
            {
                return false;
            }
            depth -= slop;
            if (depth <= 0f)
            {
                return false;
            }
            if (math.dot(normal, centerDelta) < 0f)
            {
                normal = -normal;
            }
            return true;
        }

        /// <summary>Tests one SAT candidate axis, tracking the least-overlapping usable axis seen so far. A near-zero-length axis carries no separating information and is skipped rather than treated as a separation or overlap.</summary>
        /// <returns>False only when this axis proves the boxes are definitely separated.</returns>
        private static bool TestSeparatingAxis(
            in float3 axis, in float3 centerDelta,
            in float3 halfExtentsA, in quaternion orientationA,
            in float3 halfExtentsB, in quaternion orientationB,
            ref float3 bestNormal, ref float bestDepth, ref bool foundUsableAxis)
        {
            float axisLengthSq = math.lengthsq(axis);
            if (axisLengthSq < AxisDegenerateEpsilonSq)
            {
                return true;
            }

            float3 normalizedAxis = axis * math.rsqrt(axisLengthSq);
            float radiusA = ProjectedRadius(in halfExtentsA, in orientationA, in normalizedAxis);
            float radiusB = ProjectedRadius(in halfExtentsB, in orientationB, in normalizedAxis);
            float centerDistance = math.dot(centerDelta, normalizedAxis);
            float overlap = radiusA + radiusB - math.abs(centerDistance);
            if (overlap < 0f)
            {
                return false;
            }

            foundUsableAxis = true;
            if (overlap < bestDepth)
            {
                bestDepth = overlap;
                bestNormal = normalizedAxis;
            }
            return true;
        }

        private static float ProjectedRadius(in float3 halfExtents, in quaternion orientation, in float3 axis)
        {
            float3 localAxis = math.mul(math.inverse(orientation), axis);
            return halfExtents.x * math.abs(localAxis.x)
                + halfExtents.y * math.abs(localAxis.y)
                + halfExtents.z * math.abs(localAxis.z);
        }

        // -----------------------------------------------------------------------------------
        // Shared rigid-body math. Every use of RagdollBodyParams.invInertiaDiagonal goes through
        // these, because the diagonal is only valid in the box's own local axes
        // (orientation * boxRotation), never in the body's orientation alone.
        // -----------------------------------------------------------------------------------

        /// <summary>First-order quaternion integration by an angular velocity over a timestep, renormalized. Accurate to first order in <paramref name="deltaTime"/>, which is why substeps exist.</summary>
        private static quaternion IntegrateOrientationByVelocity(
            in quaternion orientation, in float3 angularVelocity, float deltaTime)
        {
            float3 halfDelta = angularVelocity * (deltaTime * 0.5f);
            quaternion spin = new quaternion(halfDelta.x, halfDelta.y, halfDelta.z, 0f);
            float4 integrated = orientation.value + math.mul(spin, orientation).value;
            return math.normalize(new quaternion(integrated));
        }

        private static quaternion RotateOrientationByAxisAngle(in quaternion orientation, in float3 axis, float angle)
        {
            if (math.abs(angle) < AngularEpsilon)
            {
                return orientation;
            }
            quaternion rotation = quaternion.AxisAngle(axis, angle);
            return math.normalize(math.mul(rotation, orientation));
        }

        // Folded to the w >= 0 hemisphere first, same as BillboardMath.TwistAngle and for the same
        // reason: a quaternion and its negation are the same rotation but would otherwise disagree
        // by a full turn's worth of angular speed.
        /// <summary>Recovers angular velocity from the orientation delta between two states, exactly (via the delta's own axis-angle), not the small-angle approximation <see cref="IntegrateOrientationByVelocity"/> uses going the other way.</summary>
        private static float3 ComputeAngularVelocity(
            in quaternion previousOrientation, in quaternion currentOrientation, float deltaTime)
        {
            quaternion delta = math.mul(currentOrientation, math.inverse(previousOrientation));
            float4 deltaValue = delta.value;
            if (deltaValue.w < 0f)
            {
                deltaValue = -deltaValue;
            }

            float3 axisTimesSinHalfAngle = deltaValue.xyz;
            float sinHalfAngleMagnitude = math.length(axisTimesSinHalfAngle);
            if (sinHalfAngleMagnitude < AngularEpsilon || deltaTime <= 0f)
            {
                return float3.zero;
            }

            float cosHalfAngle = math.clamp(deltaValue.w, -1f, 1f);
            float halfAngle = math.atan2(sinHalfAngleMagnitude, cosHalfAngle);
            float3 axis = axisTimesSinHalfAngle / sinHalfAngleMagnitude;
            return axis * (2f * halfAngle / deltaTime);
        }

        private static void DeriveVelocity(
            in float3 previousPosition, in quaternion previousOrientation, float deltaTime, ref RagdollBodyState state)
        {
            if (deltaTime <= 0f)
            {
                return;
            }
            state.linearVelocity = (state.position - previousPosition) / deltaTime;
            state.angularVelocity = ComputeAngularVelocity(in previousOrientation, in state.orientation, deltaTime);
        }

        private static float3 ApplyInverseInertiaWorld(
            in quaternion inertialOrientation, in float3 invInertiaDiagonal, in float3 worldVector)
        {
            float3 localVector = math.mul(math.inverse(inertialOrientation), worldVector);
            float3 scaledLocal = localVector * invInertiaDiagonal;
            return math.mul(inertialOrientation, scaledLocal);
        }

        /// <summary>The angular contribution to a point constraint's generalized inverse mass: (r x n)^T * I^-1 * (r x n).</summary>
        private static float AngularInverseMass(
            in float3 invInertiaDiagonal, in quaternion inertialOrientation, in float3 leverArm, in float3 axis)
        {
            float3 crossTerm = math.cross(leverArm, axis);
            float3 applied = ApplyInverseInertiaWorld(in inertialOrientation, in invInertiaDiagonal, in crossTerm);
            return math.dot(crossTerm, applied);
        }

        /// <summary>A body's angular inverse mass about a pure rotation axis (no lever arm): axis^T * I^-1 * axis.</summary>
        private static float AngularSpin(in float3 invInertiaDiagonal, in quaternion inertialOrientation, in float3 axis)
        {
            float3 applied = ApplyInverseInertiaWorld(in inertialOrientation, in invInertiaDiagonal, in axis);
            return math.dot(axis, applied);
        }

        /// <summary>Applies a point constraint's angular correction: q += 0.5 * [I^-1(r x p), 0] (x) q, renormalized.</summary>
        private static quaternion ApplyAngularImpulse(
            in quaternion inertialOrientation, in float3 invInertiaDiagonal,
            in float3 leverArm, in float3 impulse, in quaternion orientation)
        {
            float3 torque = math.cross(leverArm, impulse);
            float3 angularDelta = ApplyInverseInertiaWorld(in inertialOrientation, in invInertiaDiagonal, in torque);
            quaternion spin = new quaternion(angularDelta.x * 0.5f, angularDelta.y * 0.5f, angularDelta.z * 0.5f, 0f);
            float4 integrated = orientation.value + math.mul(spin, orientation).value;
            return math.normalize(new quaternion(integrated));
        }
    }
}
