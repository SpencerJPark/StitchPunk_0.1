using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// One swinging door (Caravan_System.md CV-D20). Zones and scripts say what they want; DoorSystem decides and swings it.
public struct Door : IComponentData
{
    public DoorPhase phase;
    public float phaseSeconds;
    public float openSeconds;
    public float closeSeconds;
    public float burstSeconds;
    public float openDegrees;              // signed: the sign picks which way it swings
    public float angleDegrees;             // this frame's swing, read by the hinge and by hybrid art
    public bool zoneWantsOpen;             // rewritten every frame by DoorZoneSystem
    public DoorOverride scriptOverride;    // beats the zones while set
    public bool burstRequested;            // one-shot slam open, consumed by DoorSystem
}

// Hybrid art (the caravan isn't baked) finds its hinge by this name among its owner's doors.
public struct DoorName : IComponentData
{
    public FixedString32Bytes value;
}

// The vehicle or building the door belongs to.
public struct DoorOwner : IComponentData
{
    public Entity owner;
}

// Rest pose, so DoorSystem can swing a baked door about its parent's up axis.
public struct DoorHinge : IComponentData
{
    public quaternion restRotation;
}

// An oriented box in the zone entity's own space: while an allowed opener stands in it, its doors open.
public struct DoorZone : IComponentData
{
    public float3 localCenter;
    public float3 halfExtents;
    public DoorOpeners openers;
    public Entity lockingVehicle;          // opens nothing while this vehicle is being driven; Null = never locked
}

public struct DoorZoneDoor : IBufferElementData
{
    public Entity door;
}
