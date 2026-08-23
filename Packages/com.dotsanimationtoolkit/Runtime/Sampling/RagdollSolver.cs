// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Phase D's ragdoll physics (amendment A50): pure static functions, Burst-compiled, no ECS
    /// types anywhere in the math — exactly the shape <see cref="ClipSampler"/> and
    /// <see cref="BillboardMath"/> already establish for this package. One substep of an extended
    /// position-based dynamics (XPBD) solver per <see cref="Step"/> call, over plain
    /// <see cref="RagdollBodyParams"/> / <see cref="RagdollBodyState"/> arrays a caller owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One implementation, at least three callers.</strong> The runtime's
    /// <c>RagdollSolveSystem</c> job (D4), the editor's <c>RagdollPreviewSimulation</c> ticking
    /// <c>PreviewSkeletonMirror</c> transforms every repaint (D6), and this file's own EditMode
    /// tests all call exactly this entry point. A preview that re-derived any of this arithmetic
    /// would drift from the runtime the moment either side changed — the failure
    /// <c>SocketPreviewParityTests</c> and <c>BillboardPreviewParityTests</c> already exist to
    /// prevent for their own features, and <c>RagdollPreviewParityTests</c> (D6) exists for this
    /// one.
    /// </para>
    /// <para>
    /// <strong>Where a "body" lives.</strong> <see cref="RagdollBodyState.position"/> and
    /// <see cref="RagdollBodyState.orientation"/> track the collision box's own center of mass, not
    /// the authored node's origin. See <see cref="RagdollBodyParams"/>'s remarks for why: it is
    /// what lets <see cref="RagdollBodyParams.invInertiaDiagonal"/> stay three floats instead of a
    /// general symmetric tensor. Every function below inherits that convention without restating it.
    /// </para>
    /// <para>
    /// <strong>Judgement calls this file makes that §6 did not fully specify</strong> — flagged
    /// here because four later phases (D3, D4, D6, D8) build directly on this contract:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// §6.1 describes "pin the child body's anchor point to the parent's anchor point" but §5.2's
    /// field list has no anchor. <see cref="RagdollBodyParams.parentAnchorOffset"/> supplies it —
    /// see that field's remarks for why the child side needed no matching field of its own.
    /// </description></item>
    /// <item><description>
    /// §5.6's <c>RagdollWorldContact</c> field list has no way to say which body a contact belongs
    /// to. <see cref="RagdollContact.bodyIndex"/> supplies it; D3's eventual buffer element needs
    /// the same field or this solver cannot consume it.
    /// </description></item>
    /// <item><description>
    /// §6.1 lists constraint categories in the order Joint, Limit, Plane, Self-contact, World
    /// contact. <see cref="Step"/> solves Plane <em>last</em> within each iteration instead,
    /// because SAT-driven contact correction can introduce an out-of-plane component that only a
    /// following plane pass removes — solving Plane first would leave the final iteration's
    /// contacts unprojected, breaking the exact on-plane invariant
    /// <c>RagdollPlanarConstraintTests</c> checks. The categories are unchanged; only their order
    /// within one iteration is.
    /// </description></item>
    /// <item><description>
    /// Self- and world-contact resolution here is <strong>linear-only</strong>: a penetrating body
    /// is pushed along the contact normal with no torque from the contact point's offset from the
    /// body's center. §10 requires SAT detection and parent/mask exclusion, not manifold-accurate
    /// angular response, and no build phase after this one depends on contact-induced spin. If
    /// playtesting in D6 or D8 finds ragdolls sliding unnaturally through corners, this is the
    /// first place to add it — <see cref="RagdollContact.point"/> is already carried for exactly
    /// that future pass.
    /// </description></item>
    /// <item><description>
    /// Restitution and friction are applied once, after every iteration's purely positional
    /// constraint solve and after velocities are derived from the resulting position delta — a
    /// second, read-mostly contact pass rather than folding a velocity nudge into the position
    /// iterations, so the derived (fully inelastic) velocity is a clean baseline the response
    /// adjusts rather than something it must contend with mid-iteration.
    /// </description></item>
    /// <item><description>
    /// §3.3's <c>selfCollidesWith</c> mask is required to agree in <em>both</em> directions before
    /// a pair collides (<see cref="ShouldSelfCollide"/>) — the conservative reading of two
    /// independent per-body masks that could otherwise disagree.
    /// </description></item>
    /// <item><description>
    /// <see cref="RagdollBodyParams.twistLimit"/>'s axis is the child's rest-local <c>+Y</c>,
    /// matching <see cref="BillboardMath"/>'s "up unless a mode names its own axis" convention.
    /// Nothing in §3.3 names one; a different choice changes what "twist" means for every
    /// Spatial3D rig, so it is called out here rather than left implicit in the code.
    /// </description></item>
    /// <item><description>
    /// A body with <see cref="RagdollBodyParams.invMass"/> zero is read as fully static — position
    /// <em>and</em> orientation frozen, gravity and damping skipped entirely — because §5.2 offers
    /// no separate "pin linear, leave angular free" concept and "a static body" reads as the plain
    /// meaning of the phrase.
    /// </description></item>
    /// </list>
    /// </remarks>
    [BurstCompile]
    public static class RagdollSolver
    {
        /// <summary>Below this, a position-level correction is treated as already satisfied.</summary>
        private const float PositionEpsilon = 1e-6f;

        /// <summary>Below this, an angular correction or a decomposed rotation axis is treated as zero/degenerate.</summary>
        private const float AngularEpsilon = 1e-6f;

        /// <summary>Below this, a mass or a moment of inertia is treated as zero (pinned / axis carries no rotational inertia).</summary>
        private const float MassOrInertiaEpsilon = 1e-8f;

        /// <summary>Below this squared length, a SAT candidate axis (almost always an edge-cross product of two nearly parallel edges) carries no separating information and is skipped.</summary>
        private const float AxisDegenerateEpsilonSq = 1e-10f;

        /// <summary>
        /// How far apart (world units) two shapes may sit and still count as "touching" for the
        /// restitution/friction pass. The position-solve iterations above generally leave a
        /// resolved contact at very close to zero separation, not still penetrating, so the
        /// velocity-response pass needs a small positive tolerance rather than requiring the
        /// literal overlap the position pass required.
        /// </summary>
        private const float ContactTouchSlop = 1e-3f;

        // -----------------------------------------------------------------------------------
        // Bake-time helpers. Not called by Step itself, but shared by whichever bakers (D3) and
        // preview builders (D6) populate RagdollBodyParams, so neither invents its own copy of
        // arithmetic the other must agree with to the last float.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The closed-form box inertia tensor, inverted (§3.3: "the inertia tensor is derived from
        /// mass and boxSize at bake — a box has a closed form").
        /// </summary>
        /// <remarks>
        /// The tensor is diagonal in the box's own local axes by construction — a box's principal
        /// axes are its own edges — which is exactly what lets
        /// <see cref="RagdollBodyParams.invInertiaDiagonal"/> store three floats instead of a
        /// general symmetric matrix.
        /// </remarks>
        /// <param name="mass">Body mass; must be positive to produce a non-zero result.</param>
        /// <param name="boxHalfExtents">The collider's half-extents.</param>
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

        /// <summary>
        /// Resolves the −1 "inherit the rig default" sentinel on a per-body damping value (§3.3).
        /// </summary>
        /// <remarks>
        /// A tiny function, but a shared one: the resolution must happen exactly once, at bake
        /// (§5.2 — "the −1 sentinel is resolved at bake, never at runtime"), and both D3's baker and
        /// D6's preview builder need to agree on the rule without each restating it.
        /// </remarks>
        /// <param name="authoredDamping">The body's own authored value; negative means inherit.</param>
        /// <param name="rigDefaultDamping">The rig-wide default to fall back to.</param>
        /// <param name="resolvedDamping">The value to bake into <see cref="RagdollBodyParams"/>.</param>
        [BurstCompile]
        public static void ResolveDampingSentinel(
            float authoredDamping, float rigDefaultDamping, out float resolvedDamping)
        {
            resolvedDamping = authoredDamping < 0f ? rigDefaultDamping : authoredDamping;
        }

        /// <summary>
        /// Applies a launch impulse to one body's velocity (spec §5.5, §7.1) — the death blow a host
        /// writes as <c>RagdollLaunch</c> before enabling a ragdoll, converted into exactly the
        /// linear and angular velocity change the body carries into its very first predicted
        /// substep.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>D4 addition, not in §6.1's numbered steps.</strong> §7.1 says <c>RagdollCaptureSystem</c>
        /// "applies <c>RagdollLaunch</c> if present" but §6 never specifies the arithmetic. This
        /// reuses the same generalized-impulse shape <see cref="SolveJointConstraint"/> and
        /// <see cref="ApplyAngularImpulse"/> already establish — a linear term scaled by
        /// <see cref="RagdollBodyParams.invMass"/>, and an angular term that folds
        /// <paramref name="worldPoint"/>'s lever arm and <paramref name="worldTorque"/> together
        /// through the same inverse-inertia rotation those functions use — so a launch is not a
        /// second, disagreeing way to turn a force into a velocity change.
        /// </para>
        /// <para>
        /// A body with zero <see cref="RagdollBodyParams.invMass"/> is left untouched, matching the
        /// class remarks' "fully static" reading — a launch cannot move what nothing else can.
        /// </para>
        /// </remarks>
        /// <param name="bodyParams">The struck body's constant configuration.</param>
        /// <param name="state">The struck body's state; its velocities are modified in place, never its position or orientation — those change through the ordinary predict/solve steps that follow.</param>
        /// <param name="worldImpulse">World-space linear impulse.</param>
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

        /// <summary>
        /// An oriented box's projected half-width onto an arbitrary world-space axis (spec §5.6,
        /// §7.5) — the public door onto <see cref="ProjectedRadius"/>, D4's fallback ground-plane
        /// provider being the first caller outside this file.
        /// </summary>
        /// <remarks>
        /// <c>RagdollProbeFallbackSystem</c> needs this to report a <see cref="RagdollContact"/>'s
        /// <c>distance</c> as the gap between a box's actual face and the ground plane, not between
        /// the box's center and the plane — <see cref="CorrectWorldContactPosition"/> treats
        /// <c>distance</c> as already accounting for the body's own extent, so a provider that
        /// skipped this and reported the center-to-plane distance would let every body sink in by
        /// its own half-height before the solver ever pushed back.
        /// </remarks>
        /// <param name="halfExtents">The box's half-extents, in its own local axes.</param>
        /// <param name="orientation">The box's world orientation.</param>
        /// <param name="axis">The world-space axis to project onto — normalized.</param>
        /// <param name="radius">The box's half-width as measured along <paramref name="axis"/>.</param>
        [BurstCompile]
        public static void ComputeBoxProjectedRadius(
            in float3 halfExtents, in quaternion orientation, in float3 axis, out float radius)
        {
            radius = ProjectedRadius(in halfExtents, in orientation, in axis);
        }

        /// <summary>
        /// The billboard frame's own +Z, in world space — Planar2D's plane normal (§6.2, §5.4).
        /// </summary>
        /// <param name="frameRotation">A frame resolved by <c>BillboardQuery.TryGetFrame</c>, or identity for the documented fallback.</param>
        /// <param name="planeNormal">The world-space plane normal.</param>
        [BurstCompile]
        public static void ComputePlaneNormal(in quaternion frameRotation, out float3 planeNormal)
        {
            planeNormal = math.mul(frameRotation, new float3(0f, 0f, 1f));
        }

        /// <summary>
        /// Whether two bodies are allowed to self-collide at all: never a parent/child pair
        /// (§3.3, unconditionally, regardless of masks — "the single most common way a hand-built
        /// ragdoll explodes on frame one"), and otherwise only when each body's
        /// <see cref="RagdollBodyParams.selfCollidesWith"/> mask admits the other's
        /// <see cref="RagdollBodyParams.selfGroup"/>. Requiring <em>both</em> directions to agree
        /// is the conservative reading of two independently-authored masks that could otherwise
        /// disagree — see the class remarks.
        /// </summary>
        /// <param name="bodyA">The first body's parameters.</param>
        /// <param name="indexA">The first body's index in the same step's arrays.</param>
        /// <param name="bodyB">The second body's parameters.</param>
        /// <param name="indexB">The second body's index in the same step's arrays.</param>
        /// <returns>True when the pair should be tested for overlap at all.</returns>
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
        /// Advances every body by exactly one fixed substep of §6.1's XPBD pipeline: predict,
        /// project constraints <see cref="RagdollSolverSettings.solverIterations"/> times, derive
        /// velocities, apply restitution and friction, and report whether the whole actor is quiet
        /// enough to be a sleep candidate.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>One substep, not one frame.</strong> §6.4 requires a fixed substep rate with no
        /// reliance on frame delta; owning that accumulator is the caller's job (D4's
        /// <c>RagdollSolveSystem</c>, or D6's preview tick), not this function's. A caller with
        /// leftover time from last frame calls <see cref="Step"/> as many times as
        /// <see cref="RagdollSolverSettings.substepDeltaTime"/> divides out of it, capped by
        /// <c>RagdollConfig.maxSubstepsPerFrame</c> (D3/D4), and carries the remainder forward.
        /// </para>
        /// <para>
        /// <strong>Bodies must be ordered parent-before-child</strong> — buffer index ascending,
        /// exactly as §5.2 requires of the eventual <c>RagdollBody</c> buffer ("a body's parent has
        /// already been integrated by the time the child reads through it"). The joint and limit
        /// pass below relies on this without re-checking it: reordering the arrays reorders the
        /// solve and, per that same section, chains solve backwards.
        /// </para>
        /// <para>
        /// <strong>Determinism (§6.4).</strong> Every loop here is index-ascending — bodies by
        /// buffer position, self-contact pairs by <c>i &lt; j</c>, world contacts by buffer
        /// position — and nothing reads wall-clock time or a random source. Two calls with
        /// identical inputs produce bit-identical outputs, which is exactly what
        /// <c>RagdollSolverDeterminismTests</c> checks.
        /// </para>
        /// </remarks>
        /// <param name="settings">Rig-wide tuning and this step's resolved frame (§3.2, §5.4, §5.7's relevant slice).</param>
        /// <param name="bodyParams">Per-body constant configuration, one entry per body, parent-before-child ordered.</param>
        /// <param name="bodyStates">Per-body integrated state, same ordering and length as <paramref name="bodyParams"/>; written in place.</param>
        /// <param name="worldContacts">This step's world contacts from whichever probe provider filled them (§5.6, §7.5); may be empty.</param>
        /// <param name="belowSleepThreshold">True when every body's speed, after this substep, is below both of <see cref="RagdollSolverSettings"/>'s sleep thresholds — the caller accumulates this into its own sleep timer (§5.4, §6.1 step 4).</param>
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
                // Joint + limit, parent-before-child, ascending (§5.2's buffer ordering guarantee).
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

                // Self-contact, ascending pair order i < j (§6.4).
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

                // World contact, ascending contact index (§6.4).
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
                    // The position the provider measured contact.distance at, never this
                    // substep's start — see RagdollContact.referencePosition for why pairing
                    // those two moments pumps energy in across substeps.
                    float3 contactReferencePosition = contact.referencePosition;
                    CorrectWorldContactPosition(
                        in contactBodyParams, ref contactBodyState, in contact,
                        in contactReferencePosition);
                    bodyStates[contact.bodyIndex] = contactBodyState;
                }

                // Plane last within the iteration — see the class remarks for why this reorders
                // §6.1's listed sequence.
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

            // Derive velocities from the position delta (§6.1 step 3) before any restitution or
            // friction touches them, so the baseline is exactly what the position solve implies —
            // fully inelastic, by construction.
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

        /// <summary>
        /// Integrates gravity and damping into velocity, then predicts position and orientation
        /// forward by one substep (§6.1 step 1). A body with zero <see cref="RagdollBodyParams.invMass"/>
        /// is treated as fully static — see the class remarks for why both degrees of freedom, not
        /// only linear, freeze together.
        /// </summary>
        private static void PredictBody(
            in RagdollBodyParams bodyParams, in float3 gravity, float deltaTime, ref RagdollBodyState state)
        {
            if (bodyParams.invMass <= 0f)
            {
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
        /// Planar2D's gravity: <c>worldGravity</c> expressed in the billboard frame with its
        /// <c>z</c> discarded, then rotated back to world space, so the result is the largest
        /// world-space vector that lies entirely within the frame's own plane (§6.2). Spatial3D
        /// uses <see cref="RagdollSolverSettings.worldGravity"/> directly — no billboard frame is
        /// read, so a 3D ragdoll on a rig with no billboard roots costs nothing extra (§6.3).
        /// </summary>
        /// <remarks>
        /// Calls <c>BillboardQuery.ToBillboardSpace</c> rather than re-deriving the projection —
        /// §1 names this exact call as the ragdoll's gravity call and asks that facing math never
        /// be recomputed a second way.
        /// </remarks>
        private static void ResolveStepGravity(in RagdollSolverSettings settings, out float3 gravity)
        {
            float3 scaledGravity = settings.worldGravity * settings.gravityScale;
            if (settings.space != RagdollSpace.Planar2D)
            {
                gravity = scaledGravity;
                return;
            }

            BillboardQuery.ToBillboardSpace(in settings.frameRotation, in scaledGravity, out float3 planarGravity);
            planarGravity.z = 0f;
            gravity = math.mul(settings.frameRotation, planarGravity);
        }

        // -----------------------------------------------------------------------------------
        // Joint + limit.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Pins the child body's own center of mass to the parent's anchor point (§6.1's "joint"
        /// constraint), rigidly. A generalized XPBD two-body positional constraint: the parent
        /// carries both a linear and an angular correction (its anchor sits away from its own
        /// center, per <see cref="RagdollBodyParams.parentAnchorOffset"/>); the child carries only
        /// a linear one, because its anchor <em>is</em> its center — see the type remarks on
        /// <see cref="RagdollBodyParams.parentAnchorOffset"/> for why that is not a simplification
        /// but the geometry of a joint authored by hierarchy (§3.3).
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

            // "impulse" here is the XPBD two-body derivation's p = Δλ·n, pointing from the parent's
            // anchor toward the child's — see the class remarks index for the derivation this
            // mirrors.
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

        /// <summary>
        /// Clamps the Planar2D hinge angle into <c>[limitMin, limitMax]</c>, measured as the twist
        /// of the departure from <see cref="RagdollBodyParams.restRelativeRotation"/> about the
        /// frame normal (§6.1's "limit" constraint, §6.2). Reuses <see cref="BillboardMath.TwistAngle"/>
        /// rather than re-deriving a twist extraction the package already owns one implementation
        /// of.
        /// </summary>
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

        /// <summary>
        /// Clamps a Spatial3D joint's swing and twist independently (§6.1, §6.3). Twist is measured
        /// about the child's rest-local <c>+Y</c> (see the class remarks on that convention); swing
        /// is whatever rotation remains once twist is factored out, clamped to a cone half-angle.
        /// The same swing-twist split <see cref="BillboardMath.TwistAngle"/>'s own callers already
        /// use, applied here to the joint's departure from rest instead of to a billboard's facing.
        /// </summary>
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

            // Both axes are expressed in the joint's own rest-relative frame; reaching world space
            // goes through the parent's current orientation, the same frame restRelativeRotation
            // is itself defined against (§3.3).
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
        /// <paramref name="correctionAngle"/> total, split between them by their angular inverse
        /// mass about that axis, scaled by <see cref="RagdollSolverSettings.jointStiffness"/>
        /// (§3.2's "limit-constraint softness"). <see cref="RagdollSolverSettings.jointDamping"/>
        /// additionally bleeds off the axis-aligned component of both bodies' angular velocity
        /// while a limit is actively correcting, so a joint settles at its stop rather than
        /// ringing against it — a specific reading of "damping" the spec leaves as a tuning
        /// concept without a formula.
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

        /// <summary>
        /// Projects a body's position onto the frame's XY plane and its orientation onto a pure
        /// rotation about the frame normal (§6.2) — exactly, not iteratively: the result's local
        /// <c>+Z</c> maps to <paramref name="planeNormal"/> to floating-point precision every call,
        /// which is what lets <see cref="Step"/> apply this once per iteration and still guarantee
        /// the invariant <c>RagdollPlanarConstraintTests</c> checks rather than only converging
        /// toward it.
        /// </summary>
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
        // (once, after derivation). Linear-only — see the class remarks.
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

        /// <summary>
        /// Pushes a body out of a penetrating world contact along its normal. No-ops when the
        /// contact is not penetrating (any longer) or the body is static.
        /// </summary>
        /// <remarks>
        /// <strong>D4 fix.</strong> <paramref name="contact"/> is filled once per frame by whichever
        /// probe provider is active (§7.5) and is never mutated during a <see cref="Step"/> call, but
        /// this correction runs once per position-solve <em>iteration</em>
        /// (<see cref="RagdollSolverSettings.solverIterations"/>, default 6) within that call. Reusing
        /// the raw, unmutated <see cref="RagdollContact.distance"/> on every iteration reapplies the
        /// <em>entire original</em> penetration correction each time — 6 full corrections instead of
        /// one converging toward zero — which pumps energy into the body every substep it stays in
        /// contact and never lets it settle. Self-contact does not have this problem because it
        /// re-derives live overlap from the bodies' current positions via SAT every iteration
        /// (<see cref="CorrectContactPosition"/>); this mirrors that by re-deriving the current
        /// penetration from how far the body has already moved along the contact normal since this
        /// <see cref="Step"/> call started (<paramref name="referencePosition"/>, the same
        /// <c>previousPositions</c> snapshot <see cref="DeriveVelocity"/> reads), rather than trusting
        /// the stale scalar.
        /// </remarks>
        /// <param name="referencePosition">
        /// The body's position when the probe measured <paramref name="contact"/>'s
        /// <c>distance</c> — held fixed across every <see cref="Step"/> substep of the frame, never
        /// re-snapshotted at a substep's own start (see <see cref="RagdollContact.referencePosition"/>
        /// for why pairing those two moments pumps energy in), so the amount already corrected since
        /// then can be subtracted back out.
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
        /// combines by the larger of the two bodies' values; friction by their geometric mean —
        /// both ordinary, if not uniquely specified, physics-engine conventions.
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

        /// <summary>
        /// 15-axis separating-axis test between two oriented boxes (the 3 face normals of each box,
        /// plus the 9 pairwise cross products of their edges), reporting the minimum-penetration
        /// axis and depth when they overlap (§6.1, §10's "box-vs-box SAT").
        /// </summary>
        /// <param name="slop">
        /// Subtracted from the true penetration depth before the overlap decision — 0 for an actual
        /// position correction, a small positive tolerance to also catch bodies left just touching
        /// by a previous correction (see <see cref="ContactTouchSlop"/>).
        /// </param>
        /// <param name="normal">Oriented from <paramref name="centerA"/> toward <paramref name="centerB"/>.</param>
        /// <param name="depth">The penetration depth along <paramref name="normal"/>, after <paramref name="slop"/> is applied; meaningless when this method returns false.</param>
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

        /// <summary>
        /// Tests one SAT candidate axis, tracking the least-overlapping usable axis seen so far. A
        /// near-zero-length axis (almost always an edge-cross product of two nearly parallel box
        /// edges) carries no separating information and is skipped rather than treated as either a
        /// separation or an overlap.
        /// </summary>
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

        /// <summary>An oriented box's projected half-width onto an arbitrary world-space axis.</summary>
        private static float ProjectedRadius(in float3 halfExtents, in quaternion orientation, in float3 axis)
        {
            float3 localAxis = math.mul(math.inverse(orientation), axis);
            return halfExtents.x * math.abs(localAxis.x)
                + halfExtents.y * math.abs(localAxis.y)
                + halfExtents.z * math.abs(localAxis.z);
        }

        // -----------------------------------------------------------------------------------
        // Shared rigid-body math: orientation integration, generalized inverse mass, angular
        // impulses. Every use of RagdollBodyParams.invInertiaDiagonal goes through these, because
        // the diagonal is only valid in the box's own local axes (orientation * boxRotation), never
        // in the body's orientation alone — see RagdollBodyParams.boxRotation's remarks.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// First-order quaternion integration by an angular velocity over a timestep — the
        /// standard <c>q += 0.5·Δt·[ω, 0]⊗q</c> update, renormalized. Accurate to first order in
        /// <paramref name="deltaTime"/>, which is the entire reason substeps exist: the error per
        /// substep shrinks with the fixed substep rate rather than with a variable frame time.
        /// </summary>
        private static quaternion IntegrateOrientationByVelocity(
            in quaternion orientation, in float3 angularVelocity, float deltaTime)
        {
            float3 halfDelta = angularVelocity * (deltaTime * 0.5f);
            quaternion spin = new quaternion(halfDelta.x, halfDelta.y, halfDelta.z, 0f);
            float4 integrated = orientation.value + math.mul(spin, orientation).value;
            return math.normalize(new quaternion(integrated));
        }

        /// <summary>Composes an exact finite rotation of <paramref name="angle"/> radians about a world <paramref name="axis"/> onto <paramref name="orientation"/>.</summary>
        private static quaternion RotateOrientationByAxisAngle(in quaternion orientation, in float3 axis, float angle)
        {
            if (math.abs(angle) < AngularEpsilon)
            {
                return orientation;
            }
            quaternion rotation = quaternion.AxisAngle(axis, angle);
            return math.normalize(math.mul(rotation, orientation));
        }

        /// <summary>
        /// Recovers angular velocity from the orientation delta between two states, exactly (via
        /// the delta's own axis-angle), not the small-angle approximation
        /// <see cref="IntegrateOrientationByVelocity"/> uses going the other way. Folds the delta to
        /// the <c>w ≥ 0</c> hemisphere first, the same fix <see cref="BillboardMath.TwistAngle"/>
        /// documents and for the same reason: a quaternion and its negation are the same rotation
        /// but would otherwise disagree by a full turn's worth of angular speed.
        /// </summary>
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

        /// <summary>Writes linear and angular velocity from the position and orientation delta since the substep started (§6.1 step 3).</summary>
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

        /// <summary>Rotates a world-space vector by a diagonal inverse inertia tensor: into the tensor's own local axes, scaled, and back out to world space.</summary>
        private static float3 ApplyInverseInertiaWorld(
            in quaternion inertialOrientation, in float3 invInertiaDiagonal, in float3 worldVector)
        {
            float3 localVector = math.mul(math.inverse(inertialOrientation), worldVector);
            float3 scaledLocal = localVector * invInertiaDiagonal;
            return math.mul(inertialOrientation, scaledLocal);
        }

        /// <summary>
        /// The angular contribution to a point constraint's generalized inverse mass:
        /// <c>(r × n)ᵀ · I⁻¹ · (r × n)</c>, the standard term from the two-body XPBD positional
        /// constraint derivation.
        /// </summary>
        private static float AngularInverseMass(
            in float3 invInertiaDiagonal, in quaternion inertialOrientation, in float3 leverArm, in float3 axis)
        {
            float3 crossTerm = math.cross(leverArm, axis);
            float3 applied = ApplyInverseInertiaWorld(in inertialOrientation, in invInertiaDiagonal, in crossTerm);
            return math.dot(crossTerm, applied);
        }

        /// <summary>A body's angular inverse mass about a pure rotation axis (no lever arm): <c>axisᵀ · I⁻¹ · axis</c>, used by the limit constraint's angular-only correction.</summary>
        private static float AngularSpin(in float3 invInertiaDiagonal, in quaternion inertialOrientation, in float3 axis)
        {
            float3 applied = ApplyInverseInertiaWorld(in inertialOrientation, in invInertiaDiagonal, in axis);
            return math.dot(axis, applied);
        }

        /// <summary>
        /// Applies a point constraint's angular correction: <c>q += 0.5·[I⁻¹(r × p), 0]⊗q</c>,
        /// renormalized — the rotational half of the same two-body XPBD derivation
        /// <see cref="SolveJointConstraint"/> uses for its linear half.
        /// </summary>
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
