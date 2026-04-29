using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(PlayerInputSystemGroup))]
public partial struct PlayerAttackSystem : ISystem
{
    private const float RANGED_CONE_HALF_ANGLE_DEG = 45f;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<UnitDataLibrary>();
        state.RequireForUpdate<Player>();
    }

    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;
        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;
        float cosHalfAngle = math.cos(math.radians(RANGED_CONE_HALF_ANGLE_DEG));

        foreach (var (transform, attackRequest, attackRequestEnabled, attackInputEnabled,
                      actionMap, cooldown, entity) in
            SystemAPI.Query<
                RefRO<LocalTransform>,
                RefRW<AttackRequest>,
                EnabledRefRW<AttackRequest>,
                EnabledRefRW<OnAttackPlayerInput>,
                RefRO<PlayerActionMap>,
                RefRW<AttackCooldown>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                    .WithEntityAccess())
        {
            if (actionMap.ValueRO.activeActionMap != ActionMaps.Player) continue;
            if (!attackInputEnabled.ValueRO) continue;

            attackInputEnabled.ValueRW = false;
            if (cooldown.ValueRO.timer > 0f) continue;

            UnitData unitData = SystemAPI.GetComponent<UnitData>(entity);
            int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
            if (unitIndex < 0 || unitIndex >= unitLibrary.Value.units.Length) continue;

            ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];
            if (unitBlob.attacks.Length == 0) continue;

            ActionType actionType  = unitBlob.attacks[0].action;
            AttackType attackType  = unitBlob.attacks[0].attack;
            int        attackIndex = (int)attackType;
            if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length) continue;
            ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];

            float3 playerPos  = transform.ValueRO.Position;
            float3 facingDir  = math.forward(transform.ValueRO.Rotation);
            facingDir.y       = 0f;
            facingDir         = math.normalizesafe(facingDir);

            bool   isMelee  = actionType == ActionType.MeleeContinuous ||
                              actionType == ActionType.MeleeSingle;
            float  rangeSq  = attackBlob.range * attackBlob.range;

            Entity bestTarget = Entity.Null;
            float  bestDistSq = float.MaxValue;
            float  bestDot    = float.MinValue;

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
                        bestDot    = dot;
                        bestTarget = enemyEntity;
                    }
                }
            }

            if (bestTarget == Entity.Null) continue;

            attackRequest.ValueRW.targetEntity = bestTarget;
            attackRequest.ValueRW.attackType   = attackType;
            attackRequest.ValueRW.hitFired     = false;

            cooldown.ValueRW.timer = attackBlob.cooldown;

            DynamicBuffer<SetAnimation> setAnimations = SystemAPI.GetBuffer<SetAnimation>(entity);
            setAnimations.Add(new SetAnimation
            {
                layer     = AnimationLayerType.Action,
                animation = GetAttackAnimation(ref unitBlob, actionType),
                speed     = 1f,
                looping   = false,
            });
            SystemAPI.SetComponentEnabled<AnimationRequest>(entity, true);

            attackRequestEnabled.ValueRW = true;
        }
    }

    private static AnimationType GetAttackAnimation(ref UnitDataBlob unitBlob, ActionType actionType)
    {
        ref BlobArray<ActionAnimationMappingBlob> mappings = ref unitBlob.actionAnimations;
        for (int i = 0; i < mappings.Length; i++)
        {
            if (mappings[i].action == actionType)
                return mappings[i].animation;
        }
        return AnimationType.None;
    }
}
