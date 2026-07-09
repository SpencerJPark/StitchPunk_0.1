using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// ── On the visual child entity ───────────────────────────────────────────────
// Enabled on death, disabled on revive by Ragdoll2DReviveSystem.
// No auto-disable timer — the character stays flat while dead. Once every angle has settled the
// root's Ragdoll2DLaunch.sleeping flag short-circuits the dynamics; the driver keeps re-writing the
// settled rotations each frame because ApplyPoseJob stomps every part LocalTransform unconditionally.
public struct Ragdoll2D : IComponentData, IEnableableComponent
{
    // Authored (copied from Ragdoll2DConfig at init)
    public float fallSpeed;
    public float groundBuffer;
    public float tiltOffset;   // additive degrees on top of MAX_TILT_DEG — selected per fall direction at init

    // Per-attack (copied from Health.kill* at init)
    public float flailIntensity; // scales joint flail response; 1 = baseline
    public float spin;           // deg/s additive Z tumble while airborne; damps out on the ground

    // Simulation state (reset by Ragdoll2DInitSystem each death)
    public float bodyZAngle;
    public quaternion initialRotation;
    // +1 = fall toward positive Z tilt (attacker was on left), -1 = negative Z tilt (attacker on right)
    public float fallSideSign;
}

// ── On joint pivot entities (arm/leg bend empties) ───────────────────────────
// Enabled on death, disabled on revive by Ragdoll2DReviveSystem.
// Flail is a 1-segment pendulum in the character's plane: angle + angular velocity, driven by
// gravity + root-motion pseudo-forces while airborne, then handed to the authored settle lerp.
public struct Ragdoll2DJoint : IComponentData, IEnableableComponent
{
    // Baked by CharacterRigBakingSystem (per-placement override / PartLibrary blob default)
    public float settleSpeed;   // exp-lerp rate toward targetAngle once grounded
    public float segmentLength; // pendulum length for the flail (PartDef.ragdollSegmentLength)
    public float weight;        // scales inherited motion + landing impulse (PartDef.ragdollWeight)

    // Simulation state (reset each death)
    public float targetAngle;     // randomly chosen from a landing zone at init
    public float currentZAngle;
    public float angularVelocity; // deg/s flail state
    public quaternion initialLocalRotation;
}

// ── On the root body entity — static config, never toggled ──────────────────
public struct Ragdoll2DConfig : IComponentData
{
    public Entity visualRoot;
    public float groundBufferForward;  // used when fallSideSign >= 0 (falling forward)
    public float groundBufferBackward; // used when fallSideSign <  0 (falling backward)
    public float tiltOffsetForward;    // additive degrees added to target tilt when falling forward
    public float tiltOffsetBackward;   // additive degrees added to target tilt when falling backward
    public float fallSpeed;
}

// ── On the root body entity — enabled on death, drives the 3D flight ────────
// Velocity is a real float3 built from the kill-source direction; landing height comes from a
// CollisionWorld raycast every airborne frame (no frozen groundY), walls bounce via restitution.
public struct Ragdoll2DLaunch : IComponentData, IEnableableComponent
{
    public float3 velocity;
    public float restitution; // energy kept on ground/wall bounces; seeded per-attack at init
    public byte airborne;     // 1 while flying/bouncing, 0 once at rest on the ground
    public byte sleeping;     // 1 once every angle settled — driver skips all dynamics
}

// ── Global sim tuning — flat singleton, baked by RagdollSimConfigAuthoring ──
// Flat floats (nothing enum-indexed), so a plain component instead of a blob. Systems fall back to
// the same defaults when the authoring hasn't been placed in the scene yet.
public struct RagdollSimConfig : IComponentData
{
    public float gravity;               // units/s² downward while airborne
    public float horizontalDrag;        // exponential XZ deceleration while airborne
    public float defaultRestitution;    // used when an attack authored restitution 0
    public float bounceMinSpeed;        // impacts below this rest instead of bouncing
    public float groundRaycastDistance; // how far below the root to probe for ground
    public float landingImpulseScale;   // joint flail kick per unit of landing impact speed
    public float flailDamping;          // exponential decay on joint angular velocity + grounded spin
    public float sleepAngularSpeedDeg;  // all angular speeds below this (deg/s) → corpse sleeps
    public float corpseCellSize;        // XZ cell size of the corpse-stacking hash
    public float corpseStackOffset;     // landing-height raise per settled corpse already in the cell
    public int   corpseStackMax;        // cap on counted corpses per cell
}

// ── Corpse-stacking spatial hash — singleton owned by CorpseCellSystem ──────
// Rebuilt every frame from settled corpses (world-services reset pattern, like DamageBus), so
// revive/despawn bookkeeping is free. Keyed by XZ cell; value is the corpse root's Y. A native
// container through a singleton bypasses ECS dependency tracking — readers must register their
// JobHandle via CorpseCellSystem.AddJobHandleForReader.
public struct CorpseCells : IComponentData
{
    public NativeParallelMultiHashMap<int2, float> map;
}
