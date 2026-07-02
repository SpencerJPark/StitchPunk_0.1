using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Ticks the player's per-swing AttackCooldown down each frame and disables it when it expires.
/// Enabled = on cooldown. Mirrors PlayerRollInputSystem's OnRollPlayerInput.rollTime tick.
/// PlayerAttackSystem starts the cooldown; this system clears it.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(PlayerInputSystemGroup))]
public partial struct PlayerAttackCooldownSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach ((RefRW<AttackCooldown> attackCooldown,
                  EnabledRefRW<AttackCooldown> attackCooldownEnabled) in
            SystemAPI.Query<
                RefRW<AttackCooldown>,
                EnabledRefRW<AttackCooldown>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!attackCooldownEnabled.ValueRO) continue;

            attackCooldown.ValueRW.remaining -= deltaTime;
            if (attackCooldown.ValueRO.remaining <= 0f)
            {
                attackCooldown.ValueRW.remaining = 0f;
                attackCooldownEnabled.ValueRW = false;
            }
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
