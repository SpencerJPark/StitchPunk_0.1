using Unity.Entities;
using Unity.Mathematics;

// ── On the visual child entity ───────────────────────────────────────────────
// Enabled on death, disabled on revive by Ragdoll2DReviveSystem.
// No auto-disable timer — the character stays flat while dead.
// Flail velocity decays naturally via damping so it settles without any shutdown logic.
public struct Ragdoll2D : IComponentData, IEnableableComponent
{
    // Authored (preserved across deaths, used to reset simulation state each death)
    public float fallSpeed;

    // Authored (copied from Ragdoll2DConfig at init)
    public float groundBuffer;
    public float tiltOffset;   // additive degrees on top of MAX_TILT_DEG — selected per fall direction at init

    // Simulation state (reset by Ragdoll2DInitSystem each death)
    public float bodyZAngle;
    public quaternion initialRotation;
    // +1 = fall toward positive Z tilt (attacker was on left), -1 = negative Z tilt (attacker on right)
    public float fallSideSign;
}

// ── On joint pivot entities (arm/leg bend empties) ───────────────────────────
// Enabled on death, disabled on revive by Ragdoll2DReviveSystem.
public struct Ragdoll2DJoint : IComponentData, IEnableableComponent
{
    // Set at init from Ragdoll2DJointRef
    public float settleSpeed;  // deg/s to reach targetAngle

    // Simulation state (reset each death)
    public float targetAngle;          // randomly chosen from a landing zone at init
    public float currentZAngle;
    public quaternion initialLocalRotation;
}

// ── On the root body entity — static config, never toggled ──────────────────
public struct Ragdoll2DConfig : IComponentData
{
    public Entity visualRoot;
    public float groundBufferForward;         // used when fallSideSign >= 0 (falling forward)
    public float groundBufferBackward; // used when fallSideSign <  0 (falling backward)
    public float tiltOffsetForward;    // additive degrees added to target tilt when falling forward
    public float tiltOffsetBackward;   // additive degrees added to target tilt when falling backward
    public float fallSpeed;
}

// ── On the root body entity — enabled on death, drives the arc trajectory ───
public struct Ragdoll2DLaunch : IComponentData, IEnableableComponent
{
    public float velocityX;
    public float velocityY;
    public float groundY;   // root Y at death — clamp target when landing
}

// Ragdoll2DJointRef / Ragdoll2DJointZone were removed. Joints are now discovered through the root's
// BodyPart buffer (BodyPartFlags.RagdollJoint entries) and their landing zones + settle speed come
// from the PartLibrary blob via the joint's PartDefId. Ragdoll2DJoint carries the baked settleSpeed.
