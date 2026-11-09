using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Holds a mounted player on the workstation's mount point. Inside the caravan that point is on the floor, which is not
// ground to UnitGravitySystem (the caravan collider is on the Objects layer), so without this the player drops through.
[BurstCompile]
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
[UpdateAfter(typeof(UnitGravitySystem))]
[UpdateAfter(typeof(PlayerBodyBlockSystem))]
public partial struct WorkstationMountPinSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WorkstationMounted>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Main-thread lookup reads on other entities wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();

        foreach ((RefRW<LocalTransform> playerTransform, RefRO<WorkstationMounted> workstationMounted, Entity playerEntity)
                 in SystemAPI.Query<RefRW<LocalTransform>, RefRO<WorkstationMounted>>()
                     .WithAll<Player>()
                     .WithEntityAccess())
        {
            Entity workstationEntity = workstationMounted.ValueRO.workstation;
            if (!SystemAPI.HasComponent<Workstation>(workstationEntity) || !SystemAPI.HasComponent<LocalTransform>(workstationEntity))
                continue;

            LocalTransform workstationTransform = SystemAPI.GetComponent<LocalTransform>(workstationEntity);
            float3 mountPosition = workstationTransform.Position
                + math.rotate(workstationTransform.Rotation, SystemAPI.GetComponent<Workstation>(workstationEntity).mountLocalOffset);
            playerTransform.ValueRW.Position = mountPosition;

            if (SystemAPI.HasComponent<Movement>(playerEntity))
                SystemAPI.GetComponentRW<Movement>(playerEntity).ValueRW.targetPosition = mountPosition;
            if (SystemAPI.HasComponent<Gravity>(playerEntity))
                SystemAPI.GetComponentRW<Gravity>(playerEntity).ValueRW.verticalVelocity = 0f;
        }
    }
}
