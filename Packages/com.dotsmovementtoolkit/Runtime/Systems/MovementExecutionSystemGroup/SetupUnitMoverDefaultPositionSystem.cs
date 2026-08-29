using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace DotsMovementToolkit
{
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
partial struct SetupUnitMoverDefaultPositionSystem : ISystem {

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        EntityCommandBuffer entityCommandBuffer =
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach ((
            RefRO<LocalTransform> localTransform,
            RefRW<Movement> unitMover,
            RefRO<SetupUnitMoverDefaultPosition> setupUnitMoverDefaultPosition,
            Entity entity)
            in SystemAPI.Query<
                RefRO<LocalTransform>,
                RefRW<Movement>,
                RefRO<SetupUnitMoverDefaultPosition>>().WithEntityAccess()) {

            unitMover.ValueRW.targetPosition = localTransform.ValueRO.Position;
            entityCommandBuffer.RemoveComponent<SetupUnitMoverDefaultPosition>(entity);
        }
    }


}
}