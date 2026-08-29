using Unity.Entities;

// Bake-time only: carries UnitSO speeds from UnitBakingUtil.BakeRequirements to
// UnitSpeedBakingSystem, which copies them into the Movement component added by
// MovementAuthoring. Stripped from the runtime world.
[BakingType]
public struct UnitSpeedBakingData : IComponentData
{
    public float moveSpeed;
    public float runSpeed;
    public float rotationSpeed;
}
