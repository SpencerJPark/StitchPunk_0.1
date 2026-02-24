using Unity.Entities;
using Unity.Mathematics;

public struct UnitMover : IComponentData
{
    public float moveSpeed;
    public float rotationSpeed;
    public float3 targetPosition;
    public bool isMoving;
}

public struct UnitGravity : IComponentData
{
    public float fallSpeed;
    public float verticalVelocity;
}

public struct SetupUnitMoverDefaultPosition : IComponentData {
}

// Horde Components
public struct HordeMembership : IComponentData, IEnableableComponent
{
    public int hordeId;
    public Entity hordeEntity;
    public float2 formationOffset;
    public int priority;
}

public struct Horde : IComponentData
{
    public int hordeId;
    public float3 targetPosition;
    public Entity targetEntity;
    public int flowFieldIndex;
    public int memberCount;
    public bool isActive;
    public bool needsPathUpdate;
    public int behaviorFlags;
}

[InternalBufferCapacity(16)]
public struct HordeMemberBuffer : IBufferElementData
{
    public Entity memberEntity;
}