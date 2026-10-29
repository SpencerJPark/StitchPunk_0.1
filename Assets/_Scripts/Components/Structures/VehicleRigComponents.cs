using Unity.Entities;
using Unity.Mathematics;

// A baked vehicle's code-driven parts (Caravan_System.md CV-D21): VehiclePoseSystem writes their LocalTransforms
// every frame. ⚠ None of them may be a toolkit rig target, or TransformApplySystem overwrites them.
public struct VehicleRig : IComponentData
{
    public Entity frontAxle;               // yawed by steering
    public quaternion frontAxleRestRotation;
    public float3 frontAxleYawAxis;        // in the axle's own space
    public float maxFrontAxleYawDegrees;

    public Entity ride;                    // bounce + lean, plus the shake/broken fallback when no clip is authored
    public float3 rideRestPosition;
    public quaternion rideRestRotation;
    public float shakeFrequency;
    public float brokenSagDegrees;

    public Entity puller;                  // the horse's spot, moved per facing; Null for a car
}

public struct VehicleWheel : IBufferElementData
{
    public Entity wheel;
    public quaternion restRotation;
    public float3 spinAxis;                // in the wheel's own space
}

// Companion ParticleSystems that VehicleSmokeSystem starts and stops. Null = not authored.
public struct VehicleSmoke : IComponentData
{
    public Entity chimneySmoke;
    public Entity smokeBurst;
    public Entity brokenSmoke;
    public int lastSeenSmokeBurstCount;
    public bool hasSeenSmokeBurstCount;    // the first read only learns the counter, so loading never replays a burst
}

// Body clips on the vehicle's own actor, resolved by name at bake; 0 = not authored, the code fallback runs.
public struct VehicleAnimationKeys : IComponentData
{
    public uint shakeBuildUp;
    public uint shakeBurst;
    public uint broken;
    public uint playingShake;              // the shake key last started, so it starts once and stops once
    public bool playingBroken;
}

// On a door whose open/close/slam clips exist (any resolved). Its owner's actor plays them, so DoorSystem leaves its hinge alone.
public struct DoorAnimationKeys : IComponentData
{
    public uint open;
    public uint close;
    public uint slam;
    public DoorPhase lastAnimatedPhase;
}
