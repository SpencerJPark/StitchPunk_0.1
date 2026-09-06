// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The ragdoll on/off switch: enabled means the ragdoll drives the pose, disabled means the
    /// animation does. Baked disabled, carries no fields; read via <c>EnabledRefRW</c>/<c>RO</c>
    /// with <c>[WithPresent(...)]</c>, never as a job's <c>All</c> filter.
    /// </summary>
    public struct RagdollActor : IComponentData, IEnableableComponent
    {
    }

    /// <summary>One box-collider body of an actor's ragdoll, living on the actor root alongside every other ragdoll component.</summary>
    // Buffer order = hierarchy depth, shallowest first; the solver reads parents through it.
    [InternalBufferCapacity(16)]
    public struct RagdollBody : IBufferElementData
    {
        public RagdollBodyId bodyId; // addressed by id, never buffer position

        public Entity node; // the node parameters.boxCenter is local to; patched by Instantiate via LinkedEntityGroup

        public int parentBodyIndex; // index into this buffer; -1 for the root

        public RagdollBodyParams parameters; // constant, baked-once configuration

        public RagdollBodyState state; // integrated, world-space state
    }

    /// <summary>One body's pre-drop pose, captured the frame its actor's ragdoll switched on. Parallel to <see cref="RagdollBody"/> — same length, same order.</summary>
    [InternalBufferCapacity(16)]
    public struct RagdollRestPose : IBufferElementData
    {
        public LocalTransform localTransform; // at the moment its ragdoll switched on

        public PostTransformMatrix postTransformMatrix; // at the moment its ragdoll switched on; carries scale
    }

    /// <summary>Per-actor flags an active ragdoll carries between steps.</summary>
    [Flags]
    public enum RagdollStateFlags : byte
    {
        /// <summary>No flags.</summary>
        None = 0,

        /// <summary>Every body has stayed below both sleep-speed thresholds for <c>sleepDelaySeconds</c>. <c>RagdollSolveSystem</c> skips dynamics for a sleeping actor, but not the apply pass.</summary>
        Sleeping = 1 << 0,

        /// <summary><c>RagdollActor</c> was just enabled and <c>RagdollCaptureSystem</c> has not yet run this switch-on. Set at bake and after every release.</summary>
        CaptureNeeded = 1 << 1,

        /// <summary><c>RagdollActor</c> was just disabled and <c>RagdollReleaseSystem</c> has not yet restored <see cref="RagdollRestPose"/>.</summary>
        RestoreNeeded = 1 << 2
    }

    /// <summary>An active ragdoll's per-step bookkeeping: the gravity frame it falls in, the fixed-step accumulator, and how long it has been quiet.</summary>
    public struct RagdollState : IComponentData
    {
        public quaternion frameRotation; // this step's billboard frame; identity when the rig declares no billboard root

        public float3 planeNormal; // frame's local +Z in world space; only meaningful in RagdollSpace.Planar2D

        public float3 planeOrigin; // world position the Planar2D plane passes through; captured once at switch-on, never moved

        public float substepAccumulator; // fixed-timestep remainder carried from the last frame that stepped this actor

        public float sleepTimer; // seconds every body has spent below both sleep thresholds, consecutively

        public RagdollStateFlags flags;
    }

    /// <summary>
    /// An impulse a host writes before enabling a ragdoll: the death blow that sends a character
    /// flying rather than merely collapsing. Not baked — add with <c>EntityManager.AddComponentData</c>
    /// immediately before enabling <see cref="RagdollActor"/>; read via a <c>ComponentLookup</c>
    /// with <c>HasComponent</c>, never as a job's <c>All</c>/<c>WithPresent</c> parameter.
    /// </summary>
    public struct RagdollLaunch : IComponentData, IEnableableComponent
    {
        public float3 worldImpulse; // applied to the struck body's linear velocity

        public float3 worldPoint; // world-space point the impulse is applied at, for torque about the struck body's centre of mass

        public float3 worldTorque; // applied directly to angular velocity, independent of worldPoint
    }

    /// <summary>
    /// One world contact for <see cref="RagdollSolver"/> to resolve as a non-penetration
    /// constraint. A probe fills this buffer every step (a fallback ground-plane probe, or the
    /// optional Physics assembly's box-cast probe); the solver reads only this buffer.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct RagdollWorldContact : IBufferElementData
    {
        public int bodyIndex; // index into this actor's RagdollBody buffer; mandatory, this buffer is per actor not per body

        public float3 point; // world-space contact point; not yet consumed by the linear-only contact response, carried for a future pass and debug gizmos

        public float3 normal; // world-space, pointing away from the surface and into the body

        public float distance; // signed separation along normal; negative = penetrating

        public float3 referencePosition; // body's world position when distance was measured; every provider must fill this

        public float restitution; // [0, 1], combined with the struck body's own at resolve time

        public float friction; // combined with the struck body's own at resolve time
    }

    /// <summary>Global ragdoll tuning, a singleton <c>ConfigBootstrapSystem</c> creates with defaults when a world does not already have one.</summary>
    public struct RagdollConfig : IComponentData
    {
        public float3 worldGravity; // world space, before a rig's own RagdollRigSettings.gravityScale; default (0, -9.81, 0)

        public float sleepLinearSpeed; // units/second; below this a body counts as settled for the sleep check

        public float sleepAngularSpeed; // radians/second; below this a body counts as settled for the sleep check

        public float sleepDelaySeconds; // every body must stay below both sleep thresholds this long before the actor sleeps

        public int maxSubstepsPerFrame; // hard cap so a stalled frame cannot make the next one simulate an unbounded catch-up

        public float fallbackGroundHeight; // world-space height of RagdollProbeFallbackSystem's ground plane, used when the optional physics probe is absent

        public float contactProbeRadius; // extra radius a probe inflates a body's box by, so a fast-falling body is caught before true penetration
    }

    /// <summary>One actor's baked copy of its rig's <c>RagdollRigSettings</c> — the rig-wide solver knobs <c>RagdollSolveSystem</c> needs every step.</summary>
    public struct RagdollRigConfig : IComponentData
    {
        public RagdollSpace space; // which plane of freedom this rig's ragdoll simulates in

        public float gravityScale; // multiplies RagdollConfig.worldGravity for this rig

        public float jointStiffness; // limit-constraint stiffness, rig-wide, [0, 1]; never touches the joint pin itself

        public float jointDamping; // limit-constraint damping, rig-wide, [0, 1]

        public byte solverIterations; // position-solve iterations per fixed substep

        public float substepDeltaTime; // fixed substep duration in seconds; a duration, not a rate — the solver multiplies by it and must never divide
    }
}
