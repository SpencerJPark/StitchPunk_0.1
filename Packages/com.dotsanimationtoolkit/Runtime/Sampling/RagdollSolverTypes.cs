// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Which plane of freedom a rig's ragdoll simulates in (Phase D, amendment A50).
    /// </summary>
    /// <remarks>
    /// Rig-wide, never per body: half an articulated body constrained to a plane and half free in
    /// space is not a mode, it is a bug (spec §3.2). <see cref="RagdollSolverSettings.space"/> is
    /// the single switch every body in a <see cref="RagdollSolver.Step"/> call obeys together.
    /// </remarks>
    public enum RagdollSpace : byte
    {
        /// <summary>
        /// Bodies translate within the billboard frame's XY plane and rotate only about its Z axis
        /// (§6.2). The default — most rigs this feature targets are billboarded 2.5D characters,
        /// and a flat character should fall <em>via its billboard</em>, not in world space.
        /// </summary>
        Planar2D = 0,

        /// <summary>
        /// Bodies translate and rotate freely in three dimensions, with swing/twist joint limits in
        /// place of the Planar2D hinge range (§6.3).
        /// </summary>
        Spatial3D = 1
    }

    /// <summary>
    /// Per-body flags baked once and read every substep; the solver never writes these back
    /// (§5.2's <c>flags</c> column).
    /// </summary>
    [Flags]
    public enum RagdollBodyFlags : byte
    {
        /// <summary>No flags.</summary>
        None = 0,

        /// <summary>
        /// This body resolves against <see cref="RagdollContact"/> entries. Off lets a body — a
        /// cape tip, most often — pass through world geometry entirely while still taking part in
        /// self-collision and its joint (§3.3).
        /// </summary>
        CollidesWithWorld = 1 << 0,

        /// <summary>
        /// This body has no ragdolled ancestor. Carried alongside <see cref="RagdollBodyParams.parentBodyIndex"/>
        /// being negative rather than replacing it, because a solver loop that reads a flag is
        /// cheaper than one that re-derives the same fact from a sentinel every time it is asked.
        /// The two must never disagree — which is exactly why authoring stores neither on its own
        /// (§3.4): both are derived once, at bake, from the same hierarchy walk.
        /// </summary>
        IsRoot = 1 << 1
    }

    /// <summary>
    /// One body's constant, baked-once configuration — the "baked" half of §5.2's field table,
    /// split from the integrated half at the D2/D3 seam described in §6.0.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Plain struct, no <c>IComponentData</c>, no <c>Entity</c>.</strong> That split is the
    /// whole point of this phase: <see cref="RagdollSolver"/> is written and fully tested against
    /// this struct before <c>RagdollComponents.cs</c> exists. Phase D3 wraps one of these inside
    /// <c>RagdollBody : IBufferElementData</c> rather than redeclaring its fields — the same
    /// relationship <see cref="BillboardSettings"/> has to <c>BillboardRootElement</c>.
    /// </para>
    /// <para>
    /// <strong>The solver's "body" is the box's own center of mass, not the authored node's
    /// origin.</strong> This is a deliberate choice §5.2 leaves open. A box's own inertia tensor is
    /// diagonal only in axes through its own center; if the solver instead integrated the node's
    /// origin (with <see cref="boxCenter"/> almost always non-zero, since a limb's node sits at its
    /// proximal joint while the box fills the limb) every rotation would need a full,
    /// non-diagonal, parallel-axis-shifted tensor. Diagonal storage — three floats instead of a
    /// matrix — stays possible only because <see cref="RagdollBodyState.position"/> /
    /// <see cref="RagdollBodyState.orientation"/> track the box's center directly.
    /// <see cref="RagdollSolver"/> therefore never reads <see cref="boxCenter"/>; it exists purely
    /// as a bake/apply-time conversion field so D3's capture system can seed
    /// <c>position = nodeWorldPosition + rotate(nodeWorldRotation, boxCenter)</c> and its apply
    /// system can invert that when writing the solved pose back onto the node's
    /// <c>LocalTransform</c>. <see cref="boxRotation"/> is <em>not</em> free of this either — see
    /// its own remarks.
    /// </para>
    /// <para>
    /// <strong>Angles are radians, mass and inertia are inverted, extents are halved.</strong> All
    /// three conversions happen once at bake so the solver's inner loop never repeats them per body
    /// per substep — the same call <see cref="BillboardSettings"/> makes for its own angles, and
    /// <see cref="RagdollSolver.ComputeBoxInverseInertia"/> is the shared function bake (D3) and
    /// preview build (D6) both call so neither redoes this arithmetic its own way.
    /// </para>
    /// <para>
    /// <strong><c>parentAnchorOffset</c> is not in §5.2's table.</strong> The spec's field list has
    /// no per-body anchor point, yet §6.1's joint constraint needs one to pin against — "the child
    /// body's anchor point to the parent's anchor point" is not well-defined without one. The child
    /// side needed no new field: its own center of mass <em>is</em> its anchor, because a joint
    /// authored by hierarchy (§3.3) places the child node — and so the child box's center, once
    /// <see cref="boxCenter"/> is folded in — at the joint itself. The parent side is not
    /// generally at its own center (an upper-arm box's joint to the forearm is at its far end, not
    /// its middle), so a fixed offset in the parent's own rest-local axes is unavoidable. It costs
    /// nothing new at authoring time: D3's baker already walks the rest hierarchy to produce
    /// <see cref="restRelativeRotation"/>, and the same walk gives it this offset for free.
    /// </para>
    /// </remarks>
    public struct RagdollBodyParams
    {
        /// <summary>
        /// The box collider's center, in the authored node's local space, exactly as authored
        /// (§3.3). <strong>Not read by <see cref="RagdollSolver"/></strong> — see the type remarks
        /// for why the solver's own body frame is defined at this point instead of at the node's
        /// origin. D3's capture and apply systems are the only readers.
        /// </summary>
        public float3 boxCenter;

        /// <summary>
        /// Box collider half-extents. Half, not full size — halving once at bake beats halving per
        /// body per substep (§5.2). This is what <see cref="RagdollSolver"/> uses as the collision
        /// shape, centered exactly on <see cref="RagdollBodyState.position"/>.
        /// </summary>
        public float3 boxHalfExtents;

        /// <summary>
        /// The box's rotation relative to the body's own orientation. Composed as
        /// <c>math.mul(state.orientation, boxRotation)</c> to reach the box's world orientation —
        /// which is also the frame <see cref="invInertiaDiagonal"/> is expressed in, since the two
        /// are computed from the same box at bake and must never drift apart.
        /// </summary>
        public quaternion boxRotation;

        /// <summary>
        /// Inverse mass. Zero pins the body: a solver divides by mass on every constraint, and a
        /// static body needing no branch is worth the field being named for its reciprocal rather
        /// than for mass itself (§5.2).
        /// </summary>
        public float invMass;

        /// <summary>
        /// Inverse inertia tensor diagonal, in the box's own local axes (<see cref="boxRotation"/>
        /// composed onto the body's orientation — <em>not</em> the body's orientation alone). A
        /// box's inertia tensor is diagonal in its own principal axes by construction, so these
        /// three numbers are the whole tensor. See <see cref="RagdollSolver.ComputeBoxInverseInertia"/>
        /// for the closed form this is baked from.
        /// </summary>
        public float3 invInertiaDiagonal;

        /// <summary>Linear velocity damping per second. Rig default and the −1 sentinel are both resolved at bake (§5.2) — never at runtime.</summary>
        public float linearDamping;

        /// <summary>Angular velocity damping per second. Same resolution rule as <see cref="linearDamping"/>.</summary>
        public float angularDamping;

        /// <summary>Contact restitution (bounce), [0, 1].</summary>
        public float restitution;

        /// <summary>Contact friction coefficient.</summary>
        public float friction;

        /// <summary>
        /// <see cref="RagdollSpace.Planar2D"/> signed hinge range minimum, radians, measured from
        /// <see cref="restRelativeRotation"/>'s twist about the frame normal. Ignored in
        /// <see cref="RagdollSpace.Spatial3D"/>.
        /// </summary>
        public float limitMin;

        /// <summary><see cref="RagdollSpace.Planar2D"/> signed hinge range maximum, radians.</summary>
        public float limitMax;

        /// <summary>
        /// <see cref="RagdollSpace.Spatial3D"/> cone half-angle, radians, [0, π]. The limit on the
        /// swing half of a swing-twist decomposition of the departure from
        /// <see cref="restRelativeRotation"/>. Ignored in <see cref="RagdollSpace.Planar2D"/>.
        /// </summary>
        public float swingLimit;

        /// <summary>
        /// <see cref="RagdollSpace.Spatial3D"/> half-range about the joint's own axis, radians,
        /// [0, π]. By convention that axis is the child's rest-local +Y — the same "up unless a
        /// mode names its own axis" convention <see cref="BillboardMath"/> uses — measured in the
        /// frame <see cref="restRelativeRotation"/> defines, so it travels with however the rig was
        /// actually authored rather than assuming every bone points the same way in world space.
        /// </summary>
        public float twistLimit;

        /// <summary>
        /// The child's orientation relative to its parent at rest:
        /// <c>inverse(parentRestOrientation) * childRestOrientation</c>. Every limit is measured as
        /// a departure from this, not from world identity, so a rig authored with a bent elbow
        /// keeps that elbow as its own zero (§3.3).
        /// </summary>
        public quaternion restRelativeRotation;

        /// <summary>
        /// The joint anchor, expressed as a fixed offset from the parent body's own center of mass
        /// in the parent's rest-local axes. Not in §5.2's table — see the type remarks for why the
        /// joint constraint needs it and why it costs D3's baker nothing new to produce. Meaningless
        /// (left at <see cref="float3.zero"/>) on a root body, which has no parent to anchor to.
        /// </summary>
        public float3 parentAnchorOffset;

        /// <summary>Index into the same step's arrays; negative for a root body. See also <see cref="RagdollBodyFlags.IsRoot"/>.</summary>
        public int parentBodyIndex;

        /// <summary>Which of 8 self-collision groups this body belongs to (a bit index, 0–7, not a mask).</summary>
        public byte selfGroup;

        /// <summary>Bitmask of the 8 groups this body collides with. Both bodies of a pair must admit each other's group for the pair to collide — see <see cref="RagdollSolver.ShouldSelfCollide"/>.</summary>
        public byte selfCollidesWith;

        /// <summary>Bake-time flags. See <see cref="RagdollBodyFlags"/>.</summary>
        public RagdollBodyFlags flags;

        /// <summary>True when <see cref="flags"/> carries <see cref="RagdollBodyFlags.CollidesWithWorld"/>.</summary>
        public bool CollidesWithWorld
        {
            get { return (flags & RagdollBodyFlags.CollidesWithWorld) != 0; }
        }

        /// <summary>True when <see cref="flags"/> carries <see cref="RagdollBodyFlags.IsRoot"/>. Should always agree with <c>parentBodyIndex &lt; 0</c>.</summary>
        public bool IsRoot
        {
            get { return (flags & RagdollBodyFlags.IsRoot) != 0; }
        }
    }

    /// <summary>
    /// One body's integrated state — the "state" half of §5.2's field table. World-space, and
    /// entirely owned by <see cref="RagdollSolver"/>: nothing here is baked, and everything here
    /// changes every substep it is simulated.
    /// </summary>
    /// <remarks>
    /// Position and orientation are the box's own center of mass and orientation, not the authored
    /// node's — see <see cref="RagdollBodyParams"/>'s remarks. D3's capture system seeds this from
    /// the live hierarchy plus <see cref="RagdollBodyParams.boxCenter"/> when a ragdoll switches on;
    /// its apply system inverts the same transform to write results back onto the node.
    /// </remarks>
    public struct RagdollBodyState
    {
        /// <summary>World-space position of the body's own center of mass.</summary>
        public float3 position;

        /// <summary>World-space orientation of the body (not the box — see <see cref="RagdollBodyParams.boxRotation"/> for the box's own offset from this).</summary>
        public quaternion orientation;

        /// <summary>World-space linear velocity of the center of mass.</summary>
        public float3 linearVelocity;

        /// <summary>World-space angular velocity.</summary>
        public float3 angularVelocity;
    }

    /// <summary>
    /// One contact to resolve as a non-penetration constraint — D2's plain-struct twin of the
    /// eventual <c>RagdollWorldContact</c> buffer element (§5.6). D2 cannot reference D3's
    /// <c>IBufferElementData</c>, so this is what a probe provider's result looks like to the
    /// solver; D3 wraps or converts into this shape the same way it wraps <see cref="RagdollBodyParams"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong><see cref="bodyIndex"/> is not in §5.6's field list.</strong> §5.6 describes a flat,
    /// per-actor buffer with no body reference, but a multi-body actor's solver cannot resolve a
    /// contact it cannot attribute to a body. Whichever provider fills the eventual ECS buffer —
    /// the fallback ground plane, a future <c>Unity.Physics</c> box-cast — already knows which
    /// body's box it tested against, so recording that index costs the provider nothing and is the
    /// only way <see cref="RagdollSolver.Step"/> can consume the buffer at all. Flagged here because
    /// D3's <c>RagdollWorldContact</c> needs the same field, and its own field list should not
    /// silently disagree with the struct that actually processes it.
    /// </para>
    /// <para>
    /// <strong><see cref="distance"/> is signed.</strong> Negative means penetrating by that many
    /// units along <see cref="normal"/>; zero or positive means touching or separated and
    /// contributes no correction. This is the ordinary signed-distance convention, chosen because it
    /// lets a provider report "not quite touching yet" without a second field.
    /// </para>
    /// <para>
    /// <strong><see cref="point"/> is accepted but not yet used for torque.</strong> §6.1's contact
    /// resolution in this phase is linear-only (see <see cref="RagdollSolver"/>'s remarks on that
    /// choice) — the body is pushed along <see cref="normal"/> without the off-center leverage a
    /// true contact point would apply. The field is kept because the eventual ECS component needs
    /// it regardless (a future angular-aware pass, or a debug gizmo, both want the actual point),
    /// and because dropping it now would be a second, disagreeing field list the moment D3 is
    /// written.
    /// </para>
    /// </remarks>
    public struct RagdollContact
    {
        /// <summary>Index into the same step's body arrays that this contact resolves against.</summary>
        public int bodyIndex;

        /// <summary>World-space point of contact. Not yet consumed by the solver — see the type remarks.</summary>
        public float3 point;

        /// <summary>World-space contact normal, pointing away from the surface and into the body.</summary>
        public float3 normal;

        /// <summary>Signed separation along <see cref="normal"/>; negative means penetrating.</summary>
        public float distance;

        /// <summary>
        /// The body's world position at the moment <see cref="distance"/> was measured.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Without this the solver injects energy across substeps, and it is subtle enough
        /// to have shipped twice.</strong> A provider measures <see cref="distance"/> once per frame,
        /// but <see cref="RagdollSolver.Step"/> runs several substeps against that one measurement.
        /// Re-deriving live penetration needs both terms to refer to the same instant: pairing a
        /// frame-start <see cref="distance"/> with a substep-start reference position re-applies the
        /// push-out that earlier substeps already resolved, so the body gains speed every frame and
        /// bounces higher forever instead of settling.
        /// </para>
        /// <para>
        /// Carrying the measurement position on the contact makes the pair self-consistent for any
        /// number of substeps, and means a provider can keep running once per frame — which the
        /// Unity Physics probe, which casts against a <c>CollisionWorld</c>, very much wants to do.
        /// </para>
        /// </remarks>
        public float3 referencePosition;

        /// <summary>Contact restitution (bounce), [0, 1]. Combined with the body's own at resolve time.</summary>
        public float restitution;

        /// <summary>Contact friction coefficient. Combined with the body's own at resolve time.</summary>
        public float friction;
    }

    /// <summary>
    /// Everything one <see cref="RagdollSolver.Step"/> call needs beyond the bodies themselves: the
    /// rig-wide tuning of §3.2's future <c>RagdollRigSettings</c>, folded together with the slice of
    /// §5.7's future <c>RagdollConfig</c> and §5.4's future <c>RagdollState</c> a single substep
    /// actually reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// D2 does not know any of those three ECS types exist yet. This is not a wrapper around them —
    /// it is the flat subset the solver needs, assembled fresh every step by whichever caller does
    /// own them (D4's job, or D6's preview accumulator). Keeping it flat here is what let this file
    /// compile and get tested before <c>RagdollComponents.cs</c> existed, and it is what the editor
    /// preview builds directly from the rig asset with no parallel type of its own (§6.0).
    /// </para>
    /// <para>
    /// <strong><see cref="substepDeltaTime"/>, not a rate.</strong> §3.2 authors a <c>substepHz</c>;
    /// this stores its reciprocal, pre-inverted for the same reason
    /// <see cref="RagdollBodyParams.invMass"/> is — <see cref="RagdollSolver.Step"/> multiplies by
    /// it every substep and should not divide. Converting once, at the top of whichever fixed-step
    /// accumulator owns <c>substepHz</c>, costs nothing measurable; converting inside the solver's
    /// inner loop would.
    /// </para>
    /// <para>
    /// <strong><see cref="jointStiffness"/> and <see cref="jointDamping"/> govern the limit
    /// constraint only.</strong> §3.2 describes them as "limit-constraint softness, rig-wide"; the
    /// joint pin itself (§6.1's "pin the child body's anchor point to the parent's anchor point") is
    /// always solved rigidly. A soft joint pin would let a ragdoll's bones visibly separate at the
    /// socket, which is a bug wherever it happens; a soft limit is what makes a joint feel organic
    /// rather than a hard stop.
    /// </para>
    /// </remarks>
    public struct RagdollSolverSettings
    {
        /// <summary>Which plane of freedom this rig simulates in.</summary>
        public RagdollSpace space;

        /// <summary>World-space gravity, before <see cref="gravityScale"/>.</summary>
        public float3 worldGravity;

        /// <summary>
        /// The point the <see cref="RagdollSpace.Planar2D"/> plane passes through — §6.2 says
        /// translation is constrained "through the actor origin" but names no field that carries
        /// that origin into the solver's reach. In practice this is the actor root's world position
        /// at the moment the ragdoll switched on (D3/D4's concern to supply); ignored entirely in
        /// <see cref="RagdollSpace.Spatial3D"/>.
        /// </summary>
        public float3 planeOrigin;

        /// <summary>Multiplies <see cref="worldGravity"/> for this rig (§3.2 — a paper cutout and a stone golem fall differently).</summary>
        public float gravityScale;

        /// <summary>
        /// The billboard frame resolved this step. Read only when <see cref="space"/> is
        /// <see cref="RagdollSpace.Planar2D"/>; ignored entirely in <see cref="RagdollSpace.Spatial3D"/>,
        /// so a 3D ragdoll on a rig with no billboard roots costs nothing extra (§6.3). The caller
        /// re-resolves this every step (never the solver) so an orbiting camera carries the plane
        /// with it (§6.2); identity is the documented fallback when a rig declares no billboard
        /// root.
        /// </summary>
        public quaternion frameRotation;

        /// <summary>Position-solve iterations per substep (§3.2, default 6).</summary>
        public byte solverIterations;

        /// <summary>The fixed substep duration in seconds. See the type remarks for why this is a duration, not a rate.</summary>
        public float substepDeltaTime;

        /// <summary>Limit-constraint stiffness, rig-wide, [0, 1]. 1 corrects a violated limit fully within one iteration; lower softens it. See the type remarks — this never touches the joint pin.</summary>
        public float jointStiffness;

        /// <summary>Limit-constraint damping, rig-wide, [0, 1] — the fraction of a body's angular speed along the limited axis removed on the iteration a limit is actively correcting, so a joint settles at its stop instead of ringing against it.</summary>
        public float jointDamping;

        /// <summary>Below this linear speed (units/second) a body counts as settled for the sleep check (§6.1 step 4).</summary>
        public float sleepLinearSpeed;

        /// <summary>Below this angular speed (radians/second) a body counts as settled for the sleep check.</summary>
        public float sleepAngularSpeed;
    }
}
