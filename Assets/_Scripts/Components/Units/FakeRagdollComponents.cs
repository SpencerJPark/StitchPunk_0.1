using Unity.Entities;
using Unity.Mathematics;

// ── On the visual child entity ───────────────────────────────────────────────
// Enabled on death, disabled on revive by FakeRagdollReviveSystem.
// No auto-disable timer — the character stays flat while dead.
// Flail velocity decays naturally via damping so it settles without any shutdown logic.
public struct FakeRagdoll : IComponentData, IEnableableComponent
{
    // Authored (preserved across deaths, used to reset simulation state each death)
    public float fallSpeed;

    // Simulation state (reset by FakeRagdollInitSystem each death)
    public float bodyZAngle;
    public quaternion initialRotation;
    // +1 = fall toward positive Z tilt (attacker was on left), -1 = negative Z tilt (attacker on right)
    public float fallSideSign;
}

// ── On joint pivot entities (arm/leg bend empties) ───────────────────────────
// Enabled on death, disabled on revive by FakeRagdollReviveSystem.
public struct FakeRagdollJoint : IComponentData, IEnableableComponent
{
    // Authored
    public float groundBuffer;

    // Simulation state (reset each death)
    public float zAngularVelocity;
    public float currentZAngle;
    public quaternion initialLocalRotation;
}

// ── On the root body entity — static config, never toggled ──────────────────
public struct FakeRagdollConfig : IComponentData
{
    public Entity visualRoot;
    public float groundBuffer;
    public float fallSpeed;
}

[InternalBufferCapacity(8)]
public struct FakeRagdollJointRef : IBufferElementData
{
    public Entity joint;
}
