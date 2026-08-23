// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The ragdoll on/off switch (Phase D, amendment A50, spec §5.1). Enabled = the ragdoll drives
    /// the pose; disabled = the animation does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Baked disabled, always.</strong> An actor an <see cref="Authoring.ActorBaker"/> gives
    /// a ragdoll starts standing on its own animation, exactly as it did before this feature
    /// existed. A game enables this on death and disables it on revive; the Clip Editor's toolbar
    /// toggle does the same thing to the preview (§8.4). This is the whole public control surface —
    /// no other component here is meant to be poked directly by host code.
    /// </para>
    /// <para>
    /// Carries no fields on purpose. Everything a ragdoll needs to run — bodies, rest pose, solver
    /// state — is baked whether or not this is ever enabled, so flipping the bit costs nothing more
    /// than the enable/disable itself (§9 G1, G2: read this through <c>EnabledRefRW</c>/<c>RO</c>
    /// with <c>[WithPresent(...)]</c>, never as a job's <c>All</c> filter).
    /// </para>
    /// </remarks>
    public struct RagdollActor : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// One box-collider body of an actor's ragdoll (Phase D, amendment A50, spec §5.2), living on
    /// the actor root alongside every other ragdoll component.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>All of an actor's ragdoll state is baked here, on this one entity</strong> — the same
    /// reason <see cref="BillboardRootElement"/> lives on the actor root rather than on the node it
    /// concerns: a ragdoll body can be a bare grouping transform that carries no authoring
    /// component of its own, and a baker may only write the entity it is baking.
    /// </para>
    /// <para>
    /// <strong>Buffer order is hierarchy depth, shallowest first, and it is load-bearing</strong> —
    /// identically to <see cref="BillboardRootElement"/>'s ordering contract, and for the identical
    /// reason: <see cref="RagdollSolver.Step"/> walks this buffer in order and a child's joint and
    /// limit constraints read through its parent's already-integrated state
    /// (<see cref="parameters"/>'s <c>parentBodyIndex</c>). Reorder this buffer and chains solve
    /// backwards.
    /// </para>
    /// <para>
    /// <strong><see cref="parameters"/> and <see cref="state"/> are not redeclared here — they are
    /// composed.</strong> This is the D2/D3 seam spec §6.0 describes: <see cref="RagdollSolver"/> is
    /// written and fully tested against <see cref="RagdollBodyParams"/> and
    /// <see cref="RagdollBodyState"/> before this type exists, exactly as
    /// <see cref="BillboardRootElement"/> composes a <see cref="BillboardSettings"/> rather than
    /// restating its fields. The §5.2 field table is authoritative; it is simply split across the
    /// two composed structs at the baked/state boundary its own <c>Kind</c> column marks.
    /// </para>
    /// <para>
    /// <strong><see cref="parentBodyIndex"/> duplicates <see cref="RagdollBodyParams.parentBodyIndex"/>
    /// deliberately.</strong> Every other doubled fact in this package's ragdoll data is refused on
    /// sight — no authored <c>isRoot</c> flag, no authored parent reference (spec §3.4) — because a
    /// second statement that can disagree with the first is exactly the failure this package's
    /// stable-id discipline exists to avoid. This one field survives that scrutiny only because §5.2
    /// and §6.0 both name it as a top-level field independently of <see cref="parameters"/>, and
    /// because it is baked, not authored: both copies are written from the same hierarchy walk in
    /// the same baker call, so they cannot drift the way an authored duplicate could. It exists for
    /// D4/D6 code that walks this buffer to convert a solved body's pose through its parent's without
    /// reaching into <see cref="parameters"/> for a value the solver itself also needs. Flagged here
    /// because it is the one deliberate exception to this feature's own anti-duplication rule.
    /// </para>
    /// </remarks>
    [InternalBufferCapacity(16)]
    public struct RagdollBody : IBufferElementData
    {
        /// <summary>
        /// The authored <see cref="RagdollBodyId"/> this element came from. A launch impulse or a
        /// debug overlay addresses a body by this id, never by buffer position, so re-pointing a
        /// body at a different node or reordering the authored list cannot silently retarget either.
        /// </summary>
        public RagdollBodyId bodyId;

        /// <summary>
        /// The entity whose transform this body writes — the node <see cref="parameters"/>'s
        /// <c>boxCenter</c> is local to. Patched by <c>Instantiate</c> via <c>LinkedEntityGroup</c>,
        /// like <c>RigPartBinding.actorRoot</c>.
        /// </summary>
        public Entity node;

        /// <summary>
        /// Index into this same buffer; −1 for the root. See the type remarks for why this
        /// duplicates <see cref="RagdollBodyParams.parentBodyIndex"/> rather than requiring every
        /// reader to reach into <see cref="parameters"/>.
        /// </summary>
        public int parentBodyIndex;

        /// <summary>This body's constant, baked-once configuration. See <see cref="RagdollBodyParams"/>.</summary>
        public RagdollBodyParams parameters;

        /// <summary>This body's integrated, world-space state. See <see cref="RagdollBodyState"/>.</summary>
        public RagdollBodyState state;
    }

    /// <summary>
    /// One body's pre-drop pose, captured the frame its actor's ragdoll switched on (Phase D,
    /// amendment A50, spec §5.3). Parallel to <see cref="RagdollBody"/> — same length, same order,
    /// one element per body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the entirety of "turning the ragdoll off restores the character exactly."</strong>
    /// <c>RagdollReleaseSystem</c> (D4) copies these two fields straight back onto the node's
    /// <c>LocalTransform</c> and <c>PostTransformMatrix</c>. Nothing else needs restoring, because
    /// nothing else about a node's pose lives anywhere but those two components — the same pair
    /// <c>TransformApplySystem</c> writes every frame for an animating part (that system's own
    /// remarks: scale lives in <c>PostTransformMatrix</c>, never in <c>LocalTransform.Scale</c>).
    /// </para>
    /// <para>
    /// <strong>Captured, not baked.</strong> "Before" means before <em>this</em> drop, not before
    /// any drop ever — a character knocked over mid-swing and revived returns to the swing, not to
    /// its rig's rest pose. Baking a rest-pose value here would be a different, wrong feature.
    /// </para>
    /// <para>
    /// <strong>Baked at full length, structural-free.</strong> <see cref="Authoring.ActorBaker"/>
    /// sizes this buffer to match <see cref="RagdollBody"/>'s exactly, filled with identity values
    /// that are never read until the first capture writes real ones. <c>RagdollCaptureSystem</c>
    /// is therefore always a write into existing buffer slots, never a structural
    /// <c>AddBuffer</c>/resize — the same "baked at full length" contract <c>PlaybackLayer</c> and
    /// <c>RigPartRef</c> already rely on for their own capacity-planning callers.
    /// </para>
    /// </remarks>
    [InternalBufferCapacity(16)]
    public struct RagdollRestPose : IBufferElementData
    {
        /// <summary>The node's <c>LocalTransform</c> at the moment its ragdoll switched on.</summary>
        public LocalTransform localTransform;

        /// <summary>The node's <c>PostTransformMatrix</c> at the moment its ragdoll switched on — carries scale (see the type remarks).</summary>
        public PostTransformMatrix postTransformMatrix;
    }

    /// <summary>
    /// Per-actor flags an active ragdoll carries between steps (Phase D, amendment A50, spec §5.4).
    /// </summary>
    [Flags]
    public enum RagdollStateFlags : byte
    {
        /// <summary>No flags.</summary>
        None = 0,

        /// <summary>Every body has stayed below both sleep-speed thresholds for <c>sleepDelaySeconds</c>. <c>RagdollSolveSystem</c> skips dynamics for a sleeping actor (but not the apply pass — §9 G1).</summary>
        Sleeping = 1 << 0,

        /// <summary><c>RagdollActor</c> was just enabled and <c>RagdollCaptureSystem</c> has not yet run this switch-on. Set at bake (an actor starts disabled, so its first enable must always capture) and after every release.</summary>
        CaptureNeeded = 1 << 1,

        /// <summary><c>RagdollActor</c> was just disabled and <c>RagdollReleaseSystem</c> has not yet restored <see cref="RagdollRestPose"/>.</summary>
        RestoreNeeded = 1 << 2
    }

    /// <summary>
    /// An active ragdoll's per-step bookkeeping (Phase D, amendment A50, spec §5.4): the gravity
    /// frame it falls in, the fixed-step accumulator, and how long it has been quiet.
    /// </summary>
    /// <remarks>
    /// <strong><see cref="frameRotation"/> is re-read every step, not captured once.</strong> An
    /// orbiting camera must carry the plane with it (spec §6.2), so <c>RagdollSolveSystem</c>
    /// re-resolves the actor's billboard frame — through <c>BillboardQuery.TryGetFrame</c>, never
    /// recomputed by hand — every step this actor is enabled, and writes the result here purely so
    /// the value used for gravity and the value published for anything else that asks are the same
    /// one. <see cref="planeNormal"/> is cached alongside it because <c>RagdollSpace.Planar2D</c>
    /// projects against it on every constraint of every iteration (spec §6.1), and deriving it once
    /// per step beats deriving it once per projection.
    /// </remarks>
    public struct RagdollState : IComponentData
    {
        /// <summary>This step's billboard frame — identity when the rig declares no billboard root (the documented fallback, spec §6.2).</summary>
        public quaternion frameRotation;

        /// <summary>The frame's local +Z in world space, from <see cref="RagdollSolver.ComputePlaneNormal"/>. Only meaningful in <c>RagdollSpace.Planar2D</c>.</summary>
        public float3 planeNormal;

        /// <summary>
        /// The point <see cref="RagdollSolverSettings.planeOrigin"/> must be supplied
        /// with every step (§6.2) — the actor root's world position, captured once by
        /// <c>RagdollCaptureSystem</c> at switch-on and never moved afterward.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>D4 finding: §5.4's field table did not carry this, and it has to live
        /// somewhere.</strong> §6.2 names "the actor origin" as the point the Planar2D plane passes
        /// through and says D3/D4 must supply it, but no baked or state component in §5 has a slot
        /// for it — <see cref="frameRotation"/> is re-read every step for exactly this reason
        /// (an orbiting camera), while the plane's translation anchor has no corresponding reason to
        /// move: within-plane motion is already unconstrained, so any fixed point the plane passes
        /// through is equally correct, and the actor's position at the moment it started ragdolling
        /// is the simplest one to capture once and never revisit. It is placed here, beside
        /// <see cref="planeNormal"/>, because both are "what <c>RagdollSolveSystem</c> needs to
        /// build this step's <c>RagdollSolverSettings</c>, cached on the actor."
        /// </para>
        /// <para>
        /// Meaningless in <c>RagdollSpace.Spatial3D</c>, exactly like <see cref="planeNormal"/>.
        /// </para>
        /// </remarks>
        public float3 planeOrigin;

        /// <summary>Fixed-timestep remainder carried from the last frame that stepped this actor.</summary>
        public float substepAccumulator;

        /// <summary>Seconds every body has spent below both sleep-speed thresholds, consecutively.</summary>
        public float sleepTimer;

        /// <summary>See <see cref="RagdollStateFlags"/>.</summary>
        public RagdollStateFlags flags;
    }

    /// <summary>
    /// An impulse a host writes before enabling a ragdoll (Phase D, amendment A50, spec §5.5): the
    /// death blow that sends a character flying rather than merely collapsing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Not baked.</strong> Nothing in the authoring model (<c>RagdollBodyDefinition</c>,
    /// <c>RagdollRigSettings</c>) declares an opt-in for it, because a launch is a fact about one
    /// live event, not about a rig every instance shares — the same reasoning spec §3.4 gives for
    /// leaving an enabled flag off <c>RagdollBodyDefinition</c> entirely. A host adds this component
    /// with <c>EntityManager.AddComponentData</c> (or an ECB) immediately before enabling
    /// <see cref="RagdollActor"/>, and <c>RagdollCaptureSystem</c> consumes and disables it in the
    /// same pass that seeds the drop.
    /// </para>
    /// <para>
    /// <strong>Because it may not exist at all, read it through a <c>ComponentLookup</c> with
    /// <c>HasComponent</c> — never as a job's <c>All</c>/<c>WithPresent</c> parameter.</strong> An
    /// enrolled query would silently exclude every actor that never launches, which is exactly the
    /// <c>PartFacing</c> trap this package has already paid for once (spec §5.5, §9).
    /// </para>
    /// </remarks>
    public struct RagdollLaunch : IComponentData, IEnableableComponent
    {
        /// <summary>World-space impulse applied to the struck body's linear velocity.</summary>
        public float3 worldImpulse;

        /// <summary>World-space point the impulse is applied at, for the torque it contributes about the struck body's centre of mass.</summary>
        public float3 worldPoint;

        /// <summary>World-space torque applied directly to the struck body's angular velocity, independent of <see cref="worldPoint"/>.</summary>
        public float3 worldTorque;
    }

    /// <summary>
    /// One world contact for <see cref="RagdollSolver"/> to resolve as a non-penetration constraint
    /// (Phase D, amendment A50, spec §5.6) — the ECS twin of D2's plain <see cref="RagdollContact"/>,
    /// which is what keeps Unity Physics an optional dependency of this package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A probe fills this buffer; the solver never names a physics assembly.</strong> The
    /// always-present <c>RagdollProbeFallbackSystem</c> (D4) writes one ground-plane contact per
    /// body from <c>RagdollConfig.fallbackGroundHeight</c>; the optional
    /// <c>DotsAnimationToolkit.Runtime.Physics</c> assembly's <c>RagdollPhysicsProbeSystem</c> (D7)
    /// replaces it with real box-casts when that package is present. Either way
    /// <c>RagdollSolveSystem</c> reads this buffer and nothing else.
    /// </para>
    /// <para>
    /// <strong><see cref="bodyIndex"/> is not optional.</strong> This buffer is per <em>actor</em>,
    /// not per body — a multi-body actor's solver cannot resolve a contact it cannot attribute to a
    /// body, so every provider must record which body's box it tested against. See
    /// <see cref="RagdollContact"/>'s remarks for the fuller version of this note; both types must
    /// agree on it or the solver cannot consume what a provider fills.
    /// </para>
    /// </remarks>
    [InternalBufferCapacity(16)]
    public struct RagdollWorldContact : IBufferElementData
    {
        /// <summary>Index into this actor's <see cref="RagdollBody"/> buffer that this contact resolves against.</summary>
        public int bodyIndex;

        /// <summary>World-space point of contact. Not yet consumed by <see cref="RagdollSolver"/>'s linear-only contact response (spec §6.1); carried for a future angular-aware pass and for debug gizmos.</summary>
        public float3 point;

        /// <summary>World-space contact normal, pointing away from the surface and into the body.</summary>
        public float3 normal;

        /// <summary>Signed separation along <see cref="normal"/>; negative means penetrating.</summary>
        public float distance;

        /// <summary>
        /// The body's world position when <see cref="distance"/> was measured. Every provider must
        /// fill this — see <see cref="RagdollContact.referencePosition"/> for what goes wrong when a
        /// frame-measured distance is paired with a substep-start reference.
        /// </summary>
        public float3 referencePosition;

        /// <summary>Contact restitution (bounce), [0, 1]. Combined with the struck body's own at resolve time.</summary>
        public float restitution;

        /// <summary>Contact friction coefficient. Combined with the struck body's own at resolve time.</summary>
        public float friction;
    }

    /// <summary>
    /// Global ragdoll tuning (Phase D, amendment A50, spec §5.7), a singleton
    /// <c>ConfigBootstrapSystem</c> creates with defaults when a world does not already have one —
    /// the same "works with zero setup" contract <see cref="AnimationToolkitConfig"/> already makes.
    /// </summary>
    /// <remarks>
    /// Flat floats, nothing enum-indexed, so a plain component rather than a blob — spec §5.7's own
    /// reasoning, and the same shape <see cref="AnimationToolkitConfig"/> already uses for exactly
    /// the same reason: nothing here is a function of authored per-rig data, so there is nothing for
    /// a bake-time build/dedup pass to buy.
    /// </remarks>
    public struct RagdollConfig : IComponentData
    {
        /// <summary>World-space gravity, before a rig's own <c>RagdollRigSettings.gravityScale</c>. Default (0, −9.81, 0).</summary>
        public float3 worldGravity;

        /// <summary>Below this linear speed (units/second) a body counts as settled for the sleep check.</summary>
        public float sleepLinearSpeed;

        /// <summary>Below this angular speed (radians/second) a body counts as settled for the sleep check.</summary>
        public float sleepAngularSpeed;

        /// <summary>Seconds every body of an actor must stay below both sleep thresholds before that actor sleeps.</summary>
        public float sleepDelaySeconds;

        /// <summary>Hard cap on fixed substeps <c>RagdollSolveSystem</c> runs in a single frame, so a stalled frame cannot make the next one simulate an unbounded catch-up.</summary>
        public int maxSubstepsPerFrame;

        /// <summary>World-space height of <c>RagdollProbeFallbackSystem</c>'s one ground plane, used whenever the optional physics probe (D7) is absent.</summary>
        public float fallbackGroundHeight;

        /// <summary>Extra radius a probe provider inflates a body's box by when testing for contact, so a fast-falling body is caught slightly before true penetration rather than one substep late.</summary>
        public float contactProbeRadius;
    }

    /// <summary>
    /// One actor's baked copy of its rig's <c>RagdollRigSettings</c> (Phase D, amendment A50,
    /// spec §3.2) — the rig-wide solver knobs <c>RagdollSolveSystem</c> needs every step and no
    /// other §5 component carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>D4 finding: this component does not exist in spec §5, and §7.2 cannot function
    /// without it.</strong> §3.2 authors <c>space</c>, <c>gravityScale</c>, <c>jointStiffness</c>,
    /// <c>jointDamping</c>, <c>solverIterations</c> and <c>substepHz</c> on
    /// <c>RigAsset.ragdollSettings</c>, and D3's baker (<c>ActorBaker.BuildBodyParams</c>) reads
    /// that struct only to resolve the two damping sentinels into <see cref="RagdollBodyParams"/> —
    /// it folds the rig defaults into every body and then never bakes the rig-wide fields
    /// themselves anywhere. <see cref="RagdollState"/> is per-step mutable state, not a place for
    /// baked constants (the same split <see cref="RagdollBodyParams"/>/<see cref="RagdollBodyState"/>
    /// already draws for bodies), so this is the missing other half: everything §6.1's
    /// <c>RagdollSolverSettings</c> needs that is constant for the whole rig, baked once, exactly
    /// as <see cref="RagdollBodyParams"/> is.
    /// </para>
    /// <para>
    /// <see cref="substepDeltaTime"/>, not a rate, for the identical reason
    /// <see cref="RagdollSolverSettings.substepDeltaTime"/> stores a duration rather than
    /// <c>substepHz</c>: the solver's per-substep loop multiplies by it and must never divide.
    /// </para>
    /// </remarks>
    public struct RagdollRigConfig : IComponentData
    {
        /// <summary>Which plane of freedom this rig's ragdoll simulates in (spec §3.2).</summary>
        public RagdollSpace space;

        /// <summary>Multiplies <see cref="RagdollConfig.worldGravity"/> for this rig.</summary>
        public float gravityScale;

        /// <summary>Limit-constraint stiffness, rig-wide, [0, 1]. Never touches the joint pin itself — see <see cref="RagdollSolverSettings.jointStiffness"/>.</summary>
        public float jointStiffness;

        /// <summary>Limit-constraint damping, rig-wide, [0, 1].</summary>
        public float jointDamping;

        /// <summary>Position-solve iterations per fixed substep.</summary>
        public byte solverIterations;

        /// <summary>The fixed substep duration in seconds — the reciprocal of the rig's authored <c>substepHz</c>, inverted once at bake.</summary>
        public float substepDeltaTime;
    }
}
