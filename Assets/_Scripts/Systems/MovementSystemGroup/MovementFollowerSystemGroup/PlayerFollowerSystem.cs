using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(MovementFollowerSystemGroup))]
partial struct PlayerMoveSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // A scene without the narrative singleton behaves as "no cutscene".
        bool cutsceneActive = SystemAPI.TryGetSingletonEntity<NarrativeEventTag>(out Entity narrativeEntity)
            && SystemAPI.IsComponentEnabled<CutsceneActiveTag>(narrativeEntity);

        foreach (var (transform, unitMover, moveInput, moveEnabled, actionMap, aimEnabled) in
            SystemAPI.Query<
                RefRO<LocalTransform>,
                RefRW<Movement>,
                RefRO<MovePlayerInput>,
                EnabledRefRO<MovePlayerInput>,
                RefRO<PlayerActionMap>,
                EnabledRefRO<AimPlayerInput>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (actionMap.ValueRO.activeActionMap != ActionMaps.Player)
            {
                unitMover.ValueRW.targetPosition = transform.ValueRO.Position;
                continue;
            }

            float3 currentPos = transform.ValueRO.Position;

            // Lock movement while a cutscene is running (same shape as the aim lock below).
            if (cutsceneActive)
            {
                unitMover.ValueRW.targetPosition = currentPos;
                continue;
            }

            // Lock movement while aiming
            if (aimEnabled.ValueRO)
            {
                unitMover.ValueRW.targetPosition = currentPos;
                continue;
            }

            if (!moveEnabled.ValueRO)
            {
                unitMover.ValueRW.targetPosition = currentPos;
                continue;
            }

            float3 moveDir = new float3(moveInput.ValueRO.moveInput.x, 0f, moveInput.ValueRO.moveInput.y);
            if (math.lengthsq(moveDir) > 0.0001f)
                unitMover.ValueRW.targetPosition = currentPos + math.normalize(moveDir) * 50f;
            else
                unitMover.ValueRW.targetPosition = currentPos;
        }
    }
}
