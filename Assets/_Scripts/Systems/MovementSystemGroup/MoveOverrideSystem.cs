using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(MovementSystemGroup))]
partial struct MoveOverrideSystem : ISystem {

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        foreach ((
                     RefRO<LocalTransform> localTransform,
                     RefRO<MoveOverride> moveOverride,
                     EnabledRefRW<MoveOverride> moveOverrideEnabled,
                     RefRW<UnitMover> unitMover)
                 in SystemAPI.Query<
                     RefRO<LocalTransform>,
                     RefRO<MoveOverride>,
                     EnabledRefRW<MoveOverride>,
                     RefRW<UnitMover>>()) {

            if (math.distancesq(localTransform.ValueRO.Position, moveOverride.ValueRO.targetPosition) > UnitMoverSystem.REACHED_TARGET_POSITION_DISTANCE_SQ) {
                // Move closer
                unitMover.ValueRW.targetPosition = moveOverride.ValueRO.targetPosition;
            } else {
                // Reached the move override position
                moveOverrideEnabled.ValueRW = false;
            }
        }
    }


}