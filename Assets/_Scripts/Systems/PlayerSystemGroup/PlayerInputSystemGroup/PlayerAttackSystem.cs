using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Player bypasses the AI decision/execution split and writes AttackRequest itself. Melee only.
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
        // A scene without the narrative singleton behaves as "no cutscene".
        if (SystemAPI.TryGetSingletonEntity<NarrativeEventTag>(out Entity narrativeEntity)
            && SystemAPI.IsComponentEnabled<CutsceneActiveTag>(narrativeEntity))
            return;

        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;
        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;
        ComponentLookup<AnimationCommandPending> animationCommandPendingLookup =
            SystemAPI.GetComponentLookup<AnimationCommandPending>(false);

        foreach ((EnabledRefRW<OnAttackPlayerInput> onAttackPlayerInputEnabled, Entity selfEntity) in
            SystemAPI.Query<EnabledRefRW<OnAttackPlayerInput>>()
                .WithAll<Player>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            if (SystemAPI.GetComponent<PlayerActionMap>(selfEntity).activeActionMap != ActionMaps.Player)
                continue;

            if (!onAttackPlayerInputEnabled.ValueRO) continue;
            onAttackPlayerInputEnabled.ValueRW = false;

            // No swinging mid-roll. A press near the end is queued and PlayerRollMoveSystem re-raises it on landing.
            if (SystemAPI.HasComponent<PlayerRoll>(selfEntity) && SystemAPI.IsComponentEnabled<PlayerRoll>(selfEntity))
            {
                PlayerRollSettings rollSettings = SystemAPI.GetComponent<PlayerRollSettings>(selfEntity);
                RefRW<PlayerRoll> playerRoll = SystemAPI.GetComponentRW<PlayerRoll>(selfEntity);
                float rollSecondsRemaining = rollSettings.rollDuration - playerRoll.ValueRO.elapsed;
                if (rollSecondsRemaining <= rollSettings.attackBufferWindow)
                    playerRoll.ValueRW.attackQueued = true;
                continue;
            }

            if (SystemAPI.IsComponentEnabled<AttackCooldown>(selfEntity)) continue;

            if (!SystemAPI.HasComponent<UnitData>(selfEntity)) continue;
            UnitData unitData = SystemAPI.GetComponent<UnitData>(selfEntity);

            int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
            if (unitIndex < 0 || unitIndex >= unitLibrary.Value.units.Length) continue;

            ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];
            if (unitBlob.attacks.Length == 0) continue;

            // Selected attack, else the unit's first entry.
            PlayerSelectedAttack selectedAttack = SystemAPI.GetComponent<PlayerSelectedAttack>(selfEntity);
            DamageSource damageSource = selectedAttack.damageSource != DamageSource.None
                ? selectedAttack.damageSource
                : unitBlob.attacks[0].attack;

            ActionType actionType = AIUtils.GetActionByAttack(ref unitBlob, damageSource);
            int attackIndex = (int)damageSource;
            if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length) continue;
            ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];

            if (!SystemAPI.IsComponentEnabled<CombatTarget>(selfEntity)) continue;
            Entity targetEntity = SystemAPI.GetComponent<CombatTarget>(selfEntity).entity;
            if (targetEntity == Entity.Null) continue;
            // Dead present but disabled = alive.
            if (!SystemAPI.HasComponent<Dead>(targetEntity)) continue;
            if (SystemAPI.IsComponentEnabled<Dead>(targetEntity)) continue;
            // Rendezvous hold: player has input while cutscene actors are still immune (DamageEventSystem).
            if (SystemAPI.HasComponent<CutsceneActor>(targetEntity)
                && SystemAPI.IsComponentEnabled<CutsceneActor>(targetEntity)) continue;
            if (!SystemAPI.HasComponent<LocalTransform>(targetEntity)) continue;

            LocalTransform playerTransform = SystemAPI.GetComponent<LocalTransform>(selfEntity);
            LocalTransform targetTransform = SystemAPI.GetComponent<LocalTransform>(targetEntity);

            float3 toTarget = targetTransform.Position - playerTransform.Position;
            toTarget.y = 0f;
            float distanceSq = math.lengthsq(toTarget);

            // No auto-step-in: out of range means no swing.
            if (distanceSq > attackBlob.range * attackBlob.range) continue;

            float3 facingDir = math.normalizesafe(toTarget);
            if (math.lengthsq(facingDir) > 0.0001f)
            {
                RefRW<LocalTransform> playerTransformRW =
                    SystemAPI.GetComponentRW<LocalTransform>(selfEntity);
                playerTransformRW.ValueRW.Rotation = quaternion.LookRotationSafe(facingDir, math.up());
            }

            // AttackRequestSystem re-validates range at hitTime and enqueues the DamageEvent.
            RefRW<AttackRequest> attackRequest = SystemAPI.GetComponentRW<AttackRequest>(selfEntity);
            attackRequest.ValueRW.targetEntity = targetEntity;
            attackRequest.ValueRW.damageSource = damageSource;
            attackRequest.ValueRW.hitFired     = false;
            attackRequest.ValueRW.elapsed      = 0f;
            SystemAPI.SetComponentEnabled<AttackRequest>(selfEntity, true);

            // The toolkit picks the directional clip from the actor's ActorFacing.
            uint animationKey = AIUtils.GetAnimationKeyByAction(ref unitBlob, actionType);
            if (animationKey != 0 && SystemAPI.HasBuffer<AnimationCommand>(selfEntity)
                && animationCommandPendingLookup.HasComponent(selfEntity))
            {
                DynamicBuffer<AnimationCommand> animationCommands =
                    SystemAPI.GetBuffer<AnimationCommand>(selfEntity);
                PlaybackApi.PlayAnimation(
                    ref animationCommands,
                    animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(selfEntity),
                    animationKey);
            }

            // Cooldown is game feel, not clip sync.
            RefRW<AttackCooldown> attackCooldown = SystemAPI.GetComponentRW<AttackCooldown>(selfEntity);
            attackCooldown.ValueRW.remaining = attackBlob.cooldown;
            SystemAPI.SetComponentEnabled<AttackCooldown>(selfEntity, true);
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
