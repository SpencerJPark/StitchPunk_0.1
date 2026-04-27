using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(CombatResolutionSystemGroup))]
[UpdateBefore(typeof(AttackResolutionSystem))]
public partial struct PlayerAttackSystem : ISystem
{
    // Cone used for ranged/directional attacks. Melee targets closest in range instead.
    private const float RANGED_CONE_HALF_ANGLE_DEG = 45f;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<Player>();
    }

    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<AttackLibraryBlob> attackLibrary = SystemAPI.GetSingleton<AttackLibrary>().library;
        float cosHalfAngle = math.cos(math.radians(RANGED_CONE_HALF_ANGLE_DEG));

        foreach (var (transform, attackData, target, attackEnabled, attackInputEnabled, actionMap) in
            SystemAPI.Query<
                RefRO<LocalTransform>,
                RefRO<AttackData>,
                RefRW<Target>,
                EnabledRefRW<Attack>,
                EnabledRefRW<OnAttackPlayerInput>,
                RefRO<PlayerActionMap>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (actionMap.ValueRO.activeActionMap != ActionMaps.Player) continue;
            if (!attackInputEnabled.ValueRO) continue;

            attackInputEnabled.ValueRW = false;

            float3 playerPos = transform.ValueRO.Position;
            float3 facingDir = math.forward(transform.ValueRO.Rotation);
            facingDir.y = 0f;
            facingDir = math.normalizesafe(facingDir);

            AttackType attackType  = attackData.ValueRO.attackType;
            int        attackIndex = (int)attackType;
            if (attackIndex < 0 || attackIndex >= attackLibrary.Value.attacks.Length)
                continue;
            ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];
            float range = attackBlob.range;
            float rangeSq = range * range;
            bool isMelee = attackBlob.actionType == ActionType.Melee;

            Entity bestTarget = Entity.Null;
            float bestDistSq = float.MaxValue;
            float bestDot = float.MinValue;

            foreach (var (enemyTransform, enemyEntity) in
                SystemAPI.Query<RefRO<LocalTransform>>()
                    .WithAll<Health, Alive>()
                    .WithNone<Player, PlayerImmune>()
                    .WithEntityAccess())
            {
                float3 toEnemy = enemyTransform.ValueRO.Position - playerPos;
                toEnemy.y = 0f;
                float distSq = math.lengthsq(toEnemy);

                if (distSq < 0.0001f || distSq > rangeSq) continue;

                if (isMelee)
                {
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestTarget = enemyEntity;
                    }
                }
                else
                {
                    float dot = math.dot(facingDir, toEnemy * math.rsqrt(distSq));
                    if (dot < cosHalfAngle) continue;
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestTarget = enemyEntity;
                    }
                }
            }

            if (bestTarget == Entity.Null) continue;

            target.ValueRW = new Target { entity = bestTarget };
            attackEnabled.ValueRW = true;
        }
    }
}
