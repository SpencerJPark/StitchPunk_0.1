using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Finds the nearest damageable entity within range of the player each frame and maintains
/// CombatTarget on the player — enabled when a valid target is in range, disabled otherwise.
/// Combat targeting is deliberately kept separate from the interaction Target maintained by
/// PlayerTargetingSystem (attack button vs interact button).
///
/// "Damageable" = has Health, is alive (Dead present-but-disabled), is not the player itself,
/// and is not PlayerImmune (enabled). WithNone respects enableable state, so Dead/PlayerImmune
/// only exclude when enabled. Faction is not filtered — the player can hit anything with Health.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(PlayerInputSystemGroup))]
public partial struct PlayerCombatTargetingSystem : ISystem
{
    // Acquisition range — intentionally wider than a given AttackBlob.range so the player
    // locks on then steps into swing distance. Matches PlayerTargetingSystem.TARGET_RANGE.
    private const float COMBAT_TARGET_RANGE = 5f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float rangeSq = COMBAT_TARGET_RANGE * COMBAT_TARGET_RANGE;

        foreach ((RefRO<LocalTransform> playerTransform,
                  RefRW<CombatTarget> combatTarget,
                  EnabledRefRW<CombatTarget> combatTargetEnabled) in
            SystemAPI.Query<
                RefRO<LocalTransform>,
                RefRW<CombatTarget>,
                EnabledRefRW<CombatTarget>>()
                    .WithAll<Player>()
                    .WithPresent<CombatTarget>())
        {
            float3 playerPos = playerTransform.ValueRO.Position;

            Entity bestTarget = Entity.Null;
            float bestDistSq = float.MaxValue;

            foreach ((RefRO<LocalTransform> candidateTransform, Entity candidateEntity) in
                SystemAPI.Query<RefRO<LocalTransform>>()
                    .WithAll<Health>()
                    .WithNone<Player, PlayerImmune, Dead>()
                    .WithEntityAccess())
            {
                float3 toCandidate = candidateTransform.ValueRO.Position - playerPos;
                toCandidate.y = 0f;
                float distSq = math.lengthsq(toCandidate);

                if (distSq > rangeSq) continue;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestTarget = candidateEntity;
                }
            }

            if (bestTarget != Entity.Null)
            {
                combatTarget.ValueRW = new CombatTarget { entity = bestTarget };
                combatTargetEnabled.ValueRW = true;
            }
            else
            {
                combatTargetEnabled.ValueRW = false;
            }
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
