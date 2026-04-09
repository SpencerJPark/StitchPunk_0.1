using Unity.Entities;
using Unity.Mathematics;

public struct Movement : IComponentData
{
    public float moveSpeed;
    public float rotationSpeed;
    public float3 targetPosition;
    public bool isMoving;
}

public struct Gravity : IComponentData
{
    public float fallSpeed;
    public float verticalVelocity;
    // Distance from entity origin down to the floor contact point.
    // 0 for units (origin at feet). Set to half-height for center-origin meshes (e.g. 0.5 for a 1x1x1 cube).
    public float groundOffset;
    // Set by UnitGravitySystem each frame. Read by ThrownItemSystem to stop horizontal movement on landing.
    public bool isGrounded;
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
    // Controls how members arrange around the horde's targetPosition.
    // Offsets are computed by FormationOffsetSystem and stored in HordeMembership.formationOffset.
    public FormationType formationType;
    // Scene entity for the destination marker visual (particle/quad).
    // Shown by MinionCommandSystem when an order is issued; hidden by OrderMarkerSystem on completion.
    public Entity markerEntity;
}

[InternalBufferCapacity(16)]
public struct HordeMemberBuffer : IBufferElementData
{
    public Entity memberEntity;
}