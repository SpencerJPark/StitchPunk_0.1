using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Player melee attack (v1). On OnAttackPlayerInput (and off cooldown), swings at the current
/// CombatTarget maintained by PlayerCombatTargetingSystem: snap-faces the target, writes an
/// AttackRequest (consumed the same frame by AttackRequestSystem in CombatExecutionSystemGroup),
/// pushes the swing animation onto the Action layer, and starts AttackCooldown.
///
/// The player is directly controlled, so it bypasses the AI decision/execution split and writes
/// AttackRequest itself — no StateMachine / behavior commands. Melee only in v1 (ranged is deferred).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(PlayerInputSystemGroup))]
public partial struct PlayerAttackSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<UnitDataLibrary>();
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;
        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        foreach ((EnabledRefRW<OnAttackPlayerInput> attackInputEnabled, Entity selfEntity) in
            SystemAPI.Query<EnabledRefRW<OnAttackPlayerInput>>()
                .WithAll<Player>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            // Only swing while on-foot controls are active.
            if (SystemAPI.GetComponent<PlayerActionMap>(selfEntity).activeActionMap != ActionMaps.Player)
                continue;

            if (!attackInputEnabled.ValueRO) continue;
            attackInputEnabled.ValueRW = false;

            // Cadence gate — a live cooldown blocks the swing.
            if (SystemAPI.IsComponentEnabled<AttackCooldown>(selfEntity)) continue;

            if (!SystemAPI.HasComponent<UnitData>(selfEntity)) continue;
            UnitData unitData = SystemAPI.GetComponent<UnitData>(selfEntity);

            int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
            if (unitIndex < 0 || unitIndex >= unitLibrary.Value.units.Length) continue;

            ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];
            if (unitBlob.attacks.Length == 0) continue;

            // Resolve the attack: the player's selected attack, else the unit's first melee entry.
            PlayerSelectedAttack selectedAttack = SystemAPI.GetComponent<PlayerSelectedAttack>(selfEntity);
            DamageSource damageSource = selectedAttack.damageSource != DamageSource.None
                ? selectedAttack.damageSource
                : unitBlob.attacks[0].attack;

            ActionType actionType = AIUtils.GetActionByAttack(ref unitBlob, damageSource);
            int attackIndex = (int)damageSource;
            if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length) continue;
            ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];

            // Target comes from the separate combat-targeting pass; require a live, alive victim.
            if (!SystemAPI.IsComponentEnabled<CombatTarget>(selfEntity)) continue;
            Entity targetEntity = SystemAPI.GetComponent<CombatTarget>(selfEntity).entity;
            if (targetEntity == Entity.Null) continue;
            // Present-and-not-dead: Dead present but disabled = alive.
            if (!SystemAPI.HasComponent<Dead>(targetEntity)) continue;
            if (SystemAPI.IsComponentEnabled<Dead>(targetEntity)) continue;
            if (!SystemAPI.HasComponent<LocalTransform>(targetEntity)) continue;

            LocalTransform playerTransform = SystemAPI.GetComponent<LocalTransform>(selfEntity);
            LocalTransform targetTransform = SystemAPI.GetComponent<LocalTransform>(targetEntity);

            float3 toTarget = targetTransform.Position - playerTransform.Position;
            toTarget.y = 0f;
            float distanceSq = math.lengthsq(toTarget);

            // No auto-step-in in v1 — out of swing range means no swing.
            if (distanceSq > attackBlob.range * attackBlob.range) continue;

            // Snap-face the target on the XZ plane.
            float3 facingDir = math.normalizesafe(toTarget);
            if (math.lengthsq(facingDir) > 0.0001f)
            {
                RefRW<LocalTransform> playerTransformRW =
                    SystemAPI.GetComponentRW<LocalTransform>(selfEntity);
                playerTransformRW.ValueRW.Rotation = quaternion.LookRotationSafe(facingDir, math.up());
            }

            // Fire — AttackRequestSystem validates range again at hitTime and Enqueues the DamageEvent.
            RefRW<AttackRequest> attackRequest = SystemAPI.GetComponentRW<AttackRequest>(selfEntity);
            attackRequest.ValueRW.targetEntity = targetEntity;
            attackRequest.ValueRW.damageSource = damageSource;
            attackRequest.ValueRW.hitFired     = false;
            attackRequest.ValueRW.elapsed      = 0f;
            SystemAPI.SetComponentEnabled<AttackRequest>(selfEntity, true);

            // Swing animation on the Action layer.
            AnimationType animationType = AIUtils.GetAnimationByAction(ref unitBlob, actionType);
            DynamicBuffer<SetAnimation> setAnimations = SystemAPI.GetBuffer<SetAnimation>(selfEntity);
            setAnimations.Add(new SetAnimation
            {
                layer     = AnimationLayerType.Action,
                animation = animationType,
                speed     = 1f,
                looping   = false,
            });
            SystemAPI.SetComponentEnabled<AnimationRequest>(selfEntity, true);

            // Start cooldown — guarantee the hit (at hitTime) lands before the next swing.
            RefRW<AttackCooldown> attackCooldown = SystemAPI.GetComponentRW<AttackCooldown>(selfEntity);
            attackCooldown.ValueRW.remaining = math.max(attackBlob.cooldown, attackBlob.hitTime + 0.05f);
            SystemAPI.SetComponentEnabled<AttackCooldown>(selfEntity, true);
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
