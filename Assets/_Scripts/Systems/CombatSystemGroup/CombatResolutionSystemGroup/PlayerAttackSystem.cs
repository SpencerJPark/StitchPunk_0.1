using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(CombatResolutionSystemGroup))]
[UpdateBefore(typeof(AttackResolutionSystem))]
public partial struct PlayerAttackSystem : ISystem
{
    // Cone used for ranged/directional attacks. Melee targets closest in range instead.
    private const float RANGED_CONE_HALF_ANGLE_DEG = 45f;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<PlayerInputData>();
        state.RequireForUpdate<Player>();
    }

    public void OnUpdate(ref SystemState state)
    {
        PlayerInputData inputData = SystemAPI.GetSingleton<PlayerInputData>();
        if (inputData.activeActionMap != ActionMaps.Player)
            return;

        BlobAssetReference<AttackLibraryBlob> attackLibrary = SystemAPI.GetSingleton<AttackLibrary>().library;
        float cosHalfAngle = math.cos(math.radians(RANGED_CONE_HALF_ANGLE_DEG));
        int playerQueryCount = 0;

        foreach (var (transform, attackData, combatTarget, attackEnabled, attackInputEnabled) in
            SystemAPI.Query<
                RefRO<LocalTransform>,
                RefRO<AttackData>,
                RefRW<CombatTarget>,
                EnabledRefRW<Attack>,
                EnabledRefRW<PlayerAttackInput>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!attackInputEnabled.ValueRO)
                continue;

            playerQueryCount++;
            attackInputEnabled.ValueRW = false;

            float3 playerPos = transform.ValueRO.Position;
            float3 facingDir = math.forward(transform.ValueRO.Rotation);
            facingDir.y = 0f;
            facingDir = math.normalizesafe(facingDir);

            AttackType attackType = attackData.ValueRO.attackType;
            ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[(int)attackType];
            float range = attackBlob.range;
            float rangeSq = range * range;
            bool isMelee = attackBlob.attackDelivery == AttackDelivery.Melee;

            Debug.Log($"[PlayerAttackSystem] attackType={attackType} delivery={attackBlob.attackDelivery} range={range} facing={facingDir} pos={playerPos}");

            Entity bestTarget = Entity.Null;
            float bestDistSq = float.MaxValue;
            float bestDot = float.MinValue;
            int candidateCount = 0;

            foreach (var (enemyTransform, enemyEntity) in
                SystemAPI.Query<RefRO<LocalTransform>>()
                    .WithAll<Health, Alive>()
                    .WithNone<Player, PlayerImmune>()
                    .WithEntityAccess())
            {
                candidateCount++;
                float3 toEnemy = enemyTransform.ValueRO.Position - playerPos;
                toEnemy.y = 0f;
                float distSq = math.lengthsq(toEnemy);

                if (distSq < 0.0001f || distSq > rangeSq)
                {
                    Debug.Log($"[PlayerAttackSystem] {enemyEntity} rejected — dist={math.sqrt(distSq):F2} range={range:F2}");
                    continue;
                }

                if (isMelee)
                {
                    // Melee: closest target in range wins, no cone required
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestTarget = enemyEntity;
                    }
                }
                else
                {
                    // Ranged: most forward-aligned target within cone wins
                    float dot = math.dot(facingDir, toEnemy * math.rsqrt(distSq));
                    if (dot < cosHalfAngle)
                    {
                        Debug.Log($"[PlayerAttackSystem] {enemyEntity} rejected — outside cone dot={dot:F2} required={cosHalfAngle:F2}");
                        continue;
                    }
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestTarget = enemyEntity;
                    }
                }
            }

            Debug.Log($"[PlayerAttackSystem] Scanned {candidateCount} candidates. bestTarget={bestTarget}");

            if (bestTarget == Entity.Null)
            {
                Debug.LogWarning("[PlayerAttackSystem] No valid target found — Attack NOT enabled");
                continue;
            }

            Debug.Log($"[PlayerAttackSystem] Target found {bestTarget} — enabling Attack");
            combatTarget.ValueRW = new CombatTarget { entity = bestTarget };
            attackEnabled.ValueRW = true;
        }

        if (playerQueryCount == 0)
            Debug.LogWarning("[PlayerAttackSystem] PlayerAttackInput is enabled but no player entity matched the query (needs Player + AttackData + CombatTarget + Attack + PlayerAttackInput)");
    }
}
