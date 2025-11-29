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
        state.RequireForUpdate<PlayerInput>();
        state.RequireForUpdate<PlayerCharacter>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        PlayerInput input = SystemAPI.GetSingleton<PlayerInput>();

        // Convert 2D input into 3D world-space direction (XZ plane)
        float3 moveDir = new float3(input.moveInput.x, 0f, input.moveInput.y);

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
