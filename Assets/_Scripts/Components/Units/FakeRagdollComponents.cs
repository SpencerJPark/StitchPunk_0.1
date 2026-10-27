using Unity.Entities;
using Unity.Mathematics;

// A code-driven stand-in for the toolkit ragdoll on units whose rig has no ragdoll bodies yet: the root flies on
// an arc, the billboarded Visual topples over in screen space, the limbs flail, it lands flat, lies, and stands
// back up. A dead unit stays down until revived. Started by FakeRagdollControl.Knock.
public enum FakeRagdollPhase : byte { Airborne, Lying, StandingUp, Restoring }

public struct FakeRagdoll : IComponentData, IEnableableComponent
{
    public FakeRagdollPhase phase;
    public float phaseSeconds;
    public float3 position;          // authoritative root position while down; gravity's write is overruled
    public float3 velocity;
    public float3 fallDirection;     // horizontal; the pose system turns it into a screen-left/right topple
    public float groundHeight;
    public float lieSeconds;         // how long a living unit stays down after landing
    public float toppleDegrees;      // 0 standing .. 90 flat
    // The unit has real toolkit ragdoll bodies: the toolkit flings the parts, this only times the stand-up.
    public bool drivesToolkitRagdoll;
}

// Baked by FakeRagdollAuthoring. The rest pose is captured at runtime the first time the unit goes down, so
// whatever pose the rig really has (PTM scale, part offsets) is what it returns to.
public struct FakeRagdollRig : IComponentData
{
    public Entity visual;
    public bool restCaptured;
    public float4x4 visualRestMatrix;
}

public struct FakeRagdollLimb : IBufferElementData
{
    public Entity limb;
    public float flailDegrees;
    public float flailPhase;
    public quaternion restRotation;
}
