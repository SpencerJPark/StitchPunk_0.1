using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;


public struct PlayerInteractable : IComponentData, IEnableableComponent { }

// Optional, for big interactables (the caravan): PlayerTargetingSystem measures range to this capsule's surface
// in the entity's local space instead of to its origin, so you can reach it from anywhere along its side.
public struct PlayerInteractableReach : IComponentData
{
    public float3 localSegmentStart;
    public float3 localSegmentEnd;
    public float radius;
}



