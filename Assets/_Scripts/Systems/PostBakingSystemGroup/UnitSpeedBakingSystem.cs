using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;

// Copies UnitSO movement speeds (carried by the bake-only UnitSpeedBakingData from
// UnitBakingUtil.BakeRequirements) into the Movement component added by MovementAuthoring.
// Units without a brain authoring (player, BaseUnit) have no UnitSpeedBakingData and keep
// their MovementAuthoring values.
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct UnitSpeedBakingSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // IncludePrefab: unit prefabs baked for runtime spawning carry the Prefab tag and
        // would otherwise be skipped, leaving spawned units with MovementAuthoring values.
        foreach ((RefRW<Movement> movement, RefRO<UnitSpeedBakingData> speedData) in
            SystemAPI.Query<RefRW<Movement>, RefRO<UnitSpeedBakingData>>()
                .WithOptions(EntityQueryOptions.IncludePrefab))
        {
            movement.ValueRW.moveSpeed     = speedData.ValueRO.moveSpeed;
            movement.ValueRW.runSpeed      = speedData.ValueRO.runSpeed;
            movement.ValueRW.rotationSpeed = speedData.ValueRO.rotationSpeed;
        }
    }
}
