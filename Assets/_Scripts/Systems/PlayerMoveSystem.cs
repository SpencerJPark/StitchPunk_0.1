using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(UnitMoverSystem))]  
partial struct PlayerMoveSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerInputData>();
        state.RequireForUpdate<PlayerCharacter>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        PlayerInputData inputData = SystemAPI.GetSingleton<PlayerInputData>();
        
        // Early out if different action mode
        if (inputData.activeActionMap != ActionMaps.Player)
        {
            return;
        }

        // Convert 2D input into 3D world-space direction (XZ plane)
        float3 moveDir = new float3(inputData.moveInput.x, 0f, inputData.moveInput.y);

        bool hasMoveInput = math.lengthsq(moveDir) > 0.0001f;
        if (hasMoveInput)
        {
            moveDir = math.normalize(moveDir);
        }

        foreach (var (transform, unitMover) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRW<UnitMover>>()
                     .WithAll<PlayerCharacter>())
        {
            float3 currentPos = transform.ValueRO.Position;

            if (hasMoveInput)
            {
                // For continuous WASD-style movement, just target “far ahead”
                // so UnitMoverJob keeps moving in that direction.
                unitMover.ValueRW.targetPosition = currentPos + moveDir * 50f;
            }
            else
            {
                // No input: target current position so UnitMoverJob will stop
                unitMover.ValueRW.targetPosition = currentPos;
            }
        }
    }
}
