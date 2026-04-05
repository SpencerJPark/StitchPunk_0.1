using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Runs while the player holds the aim button.
/// - Reads LookPlayerInput (right stick / mouse direction set by PlayerInputManager)
///   and writes AimDirection.
/// - Rotates the player entity to face the aim direction (throw uses Forward() automatically).
/// - Shows/hides the aim indicator child entity by setting its scale.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(GameManagerSystemGroup))]
[UpdateAfter(typeof(PlayerInputSystemGroup))]
public partial struct PlayerAimSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (transform, aimDir, indicatorRef, aimEnabled, lookInput, lookEnabled) in
            SystemAPI.Query<
                RefRW<LocalTransform>,
                RefRW<AimDirection>,
                RefRO<AimIndicatorRef>,
                EnabledRefRO<AimPlayerInput>,
                RefRO<LookPlayerInput>,
                EnabledRefRO<LookPlayerInput>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            bool isAiming = aimEnabled.ValueRO;

            // Show or hide the indicator by scaling it
            Entity indicatorEntity = indicatorRef.ValueRO.visualEntity;
            if (indicatorEntity != Entity.Null && SystemAPI.HasComponent<LocalTransform>(indicatorEntity))
            {
                RefRW<LocalTransform> indicatorTransform = SystemAPI.GetComponentRW<LocalTransform>(indicatorEntity);
                indicatorTransform.ValueRW.Scale = isAiming ? 1f : 0f;
            }

            if (!isAiming)
                continue;

            // Update aim direction from look input (right stick or mouse direction via PlayerInputManager)
            if (lookEnabled.ValueRO)
            {
                float2 look = lookInput.ValueRO.lookInput;
                if (math.lengthsq(look) > 0.01f)
                    aimDir.ValueRW.direction = math.normalize(new float3(look.x, 0f, look.y));
            }

            // Rotate player to face aim direction — throw uses Forward() so this drives throw dir too
            float3 dir = aimDir.ValueRO.direction;
            if (math.lengthsq(dir) > 0.001f)
                transform.ValueRW.Rotation = quaternion.LookRotationSafe(dir, math.up());
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
