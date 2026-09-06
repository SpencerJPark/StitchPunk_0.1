// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>Which plane of freedom a rig's ragdoll simulates in. Rig-wide, never per body: every body in a <see cref="RagdollSolver.Step"/> call obeys the same switch together.</summary>
    public enum RagdollSpace : byte
    {
        /// <summary>Bodies translate within the billboard frame's XY plane and rotate only about its Z axis. The default: a flat billboarded character should fall via its billboard, not in world space.</summary>
        Planar2D = 0,

        /// <summary>Bodies translate and rotate freely in three dimensions, with swing/twist joint limits in place of the Planar2D hinge range.</summary>
        Spatial3D = 1
    }

    /// <summary>Per-body flags baked once and read every substep; the solver never writes these back.</summary>
    [Flags]
    public enum RagdollBodyFlags : byte
    {
        /// <summary>No flags.</summary>
        None = 0,

        /// <summary>This body resolves against <see cref="RagdollContact"/> entries. Off lets a body — a cape tip, most often — pass through world geometry while still taking part in self-collision and its joint.</summary>
        CollidesWithWorld = 1 << 0,

        // Duplicates RagdollBodyParams.parentBodyIndex being negative rather than replacing it (a
        // flag read is cheaper than re-deriving the fact every time). Both are derived once, at
        // bake, from the same hierarchy walk, and must never disagree.
        /// <summary>This body has no ragdolled ancestor.</summary>
        IsRoot = 1 << 1
    }

    /// <summary>
    /// One body's constant, baked-once configuration. The solver's "body" is the box's own center
    /// of mass and orientation, not the authored node's — see <see cref="boxCenter"/>.
    /// </summary>
    public struct RagdollBodyParams
    {
        public float3 boxCenter; // box collider center, in the authored node's local space; NOT read by RagdollSolver — only the capture/apply systems convert with it

        public float3 boxHalfExtents; // half-extents, not full size; the collision shape, centered on RagdollBodyState.position

        public quaternion boxRotation; // box's rotation relative to the body's own orientation: math.mul(state.orientation, boxRotation) gives the box's world orientation

        public float invMass; // 0 pins the body (static)

        public float3 invInertiaDiagonal; // in the box's own local axes (boxRotation composed onto the body's orientation); diagonal because a box's tensor is diagonal in its own principal axes

        public float linearDamping; // per second; rig default and the -1 sentinel are both resolved at bake, never at runtime

        public float angularDamping; // per second; same resolution rule as linearDamping

        public float restitution; // [0, 1]

        public float friction;

        public float limitMin; // Planar2D signed hinge range minimum, radians, measured from restRelativeRotation's twist; ignored in Spatial3D

        public float limitMax; // Planar2D signed hinge range maximum, radians

        public float swingLimit; // Spatial3D cone half-angle, radians, [0, pi]; ignored in Planar2D

        public float twistLimit; // Spatial3D half-range about the joint's axis (child's rest-local +Y), radians, [0, pi]

        public quaternion restRelativeRotation; // child's orientation relative to parent at rest; every limit is a departure from this, not from world identity

        public float3 parentAnchorOffset; // joint anchor as a fixed offset from the parent's center of mass, in the parent's rest-local axes; meaningless (zero) on a root body

        public int parentBodyIndex; // index into the same step's arrays; negative for a root body

        public byte selfGroup; // which of 8 self-collision groups this body belongs to (a bit index, 0-7, not a mask)

        public byte selfCollidesWith; // bitmask of the 8 groups this body collides with; both bodies of a pair must admit each other's group

        public RagdollBodyFlags flags;

        public bool CollidesWithWorld
        {
            get { return (flags & RagdollBodyFlags.CollidesWithWorld) != 0; }
        }

        public bool IsRoot // should always agree with parentBodyIndex < 0
        {
            get { return (flags & RagdollBodyFlags.IsRoot) != 0; }
        }
    }

    /// <summary>One body's integrated state, world-space, entirely owned by <see cref="RagdollSolver"/>: nothing here is baked, and it changes every substep it is simulated.</summary>
    public struct RagdollBodyState
    {
        public float3 position; // world-space center of mass

        public quaternion orientation; // world-space; not the box's own — see RagdollBodyParams.boxRotation

        public float3 linearVelocity;

        public float3 angularVelocity;
    }

    /// <summary>One contact to resolve as a non-penetration constraint — the plain-struct shape a probe provider fills for the solver.</summary>
    public struct RagdollContact
    {
        public int bodyIndex; // index into the same step's body arrays; mandatory, since a multi-body actor's solver cannot resolve a contact it cannot attribute to a body

        public float3 point; // world-space; accepted but not yet used for torque (contact resolution here is linear-only)

        public float3 normal; // world-space, pointing away from the surface and into the body

        public float distance; // signed separation along normal; negative = penetrating

        // Without this the solver injects energy across substeps: a provider measures distance once
        // per frame, but Step runs several substeps against it, and pairing that distance with a
        // substep-start reference re-applies a push-out already resolved, so the body gains speed
        // every frame instead of settling. Carrying the measurement position keeps the pair
        // self-consistent regardless of substep count.
        public float3 referencePosition;

        public float restitution; // [0, 1], combined with the body's own at resolve time

        public float friction; // combined with the body's own at resolve time
    }

    /// <summary>Everything one <see cref="RagdollSolver.Step"/> call needs beyond the bodies themselves — a flat subset assembled fresh every step by whichever caller owns the source data.</summary>
    public struct RagdollSolverSettings
    {
        public RagdollSpace space;

        public float3 worldGravity; // before gravityScale

        public float3 planeOrigin; // the point the Planar2D plane passes through, typically the actor root's world position when the ragdoll switched on; ignored in Spatial3D

        public float gravityScale; // multiplies worldGravity for this rig

        // Read only in Planar2D; ignored in Spatial3D. The caller re-resolves this every step
        // (never the solver), so an orbiting camera carries the plane with it; identity is the
        // fallback when a rig declares no billboard root.
        public quaternion frameRotation;

        public byte solverIterations; // position-solve iterations per substep

        public float substepDeltaTime; // duration, not a rate — the solver multiplies by it every substep and should not divide

        public float jointStiffness; // [0, 1]; limit constraint only, never the joint pin itself (which is always solved rigidly)

        public float jointDamping; // [0, 1]; fraction of angular speed along the limited axis removed while a limit is actively correcting, so a joint settles instead of ringing

        public float sleepLinearSpeed; // units/second; below this a body counts as settled for the sleep check

        public float sleepAngularSpeed; // radians/second; below this a body counts as settled for the sleep check
    }
}
