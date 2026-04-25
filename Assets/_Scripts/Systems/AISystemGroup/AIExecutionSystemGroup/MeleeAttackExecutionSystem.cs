using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct MeleeAttackExecutionSystem : ISystem
{
    private ComponentLookup<Alive>          aliveLookup;
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<UnitDataLibrary>();

        aliveLookup     = state.GetComponentLookup<Alive>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        aliveLookup.Update(ref state);
        transformLookup.Update(ref state);

        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;

        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        state.Dependency = new MeleeProcessorJob
        {
            aliveLookup   = aliveLookup,
            transformLookup = transformLookup,
            attackLibrary = attackLibrary,
            unitLibrary   = unitLibrary,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain))]
[WithDisabled(typeof(NeedsAction))]
[WithPresent(typeof(PathRequest), typeof(Target), typeof(Attack), typeof(PendingAttack))]
public partial struct MeleeProcessorJob : IJobEntity
{
    private const float REPATH_DIST_SQ  = 1.0f;
    private const float HYSTERESIS_MULT = 1.33f;

    [ReadOnly] public ComponentLookup<Alive>          aliveLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob> attackLibrary;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>   unitLibrary;

    public void Execute(
        in LocalTransform     localTransform,
        in Awareness          awareness,
        in UnitData           unitData,
        in CurrentAction      currentAction,
        ref AttackCooldown    cooldown,
        ref CombatTarget      combatTarget,
        ref PathRequest       pathRequest,
        ref Target            target,
        ref PendingAttack     pendingAttack,
        ref DynamicBuffer<AnimationLayer> layers,
        EnabledRefRW<MeleeAction>   meleeAction,
        EnabledRefRW<NeedsAction>   needsActionEnabled,
        EnabledRefRW<PathRequest>   pathRequestEnabled,
        EnabledRefRW<Target>        targetEnabled,
        EnabledRefRW<Attack>        attackEnabled,
        EnabledRefRW<PendingAttack> pendingAttackEnabled)
    {
        Entity hostile = combatTarget.targetEntity;

        if (!transformLookup.TryGetComponent(hostile, out LocalTransform targetTransform))
        {
            attackEnabled.ValueRW      = false;
            targetEnabled.ValueRW      = false;
            pendingAttackEnabled.ValueRW = false;
            pendingAttack.hitFired     = false;
            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled, localTransform);
            needsActionEnabled.ValueRW = true;
            meleeAction.ValueRW        = false;
            return;
        }

        if (!AIUtils.IsTargetAlive(hostile, aliveLookup) ||
            AIUtils.IsTargetOutOfRange(localTransform, targetTransform, awareness.range))
        {
            attackEnabled.ValueRW      = false;
            targetEnabled.ValueRW      = false;
            pendingAttackEnabled.ValueRW = false;
            pendingAttack.hitFired     = false;
            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled, localTransform);
            needsActionEnabled.ValueRW = true;
            meleeAction.ValueRW        = false;
            return;
        }

        ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[(int)currentAction.attackType];
        float distSq     = math.distancesq(localTransform.Position, targetTransform.Position);
        float breakOffSq = (attackBlob.range * HYSTERESIS_MULT) * (attackBlob.range * HYSTERESIS_MULT);

        if (distSq > breakOffSq)
        {
            attackEnabled.ValueRW = false;
            targetEnabled.ValueRW = false;
            float movedSq = math.distancesq(pathRequest.targetPosition, targetTransform.Position);
            if (movedSq >= REPATH_DIST_SQ)
                AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, targetTransform.Position);
            return;
        }

        AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled, localTransform);
        target.entity     = hostile;
        targetEnabled.ValueRW = true;

        if (cooldown.timer <= 0f && !pendingAttackEnabled.ValueRO)
        {
            pendingAttack.targetEntity   = hostile;
            pendingAttack.hitTime        = attackBlob.hitTime;
            pendingAttack.hitFired       = false;
            pendingAttackEnabled.ValueRW = true;
            cooldown.timer               = attackBlob.cooldown;

            // Start the attack animation on the Action layer
            AnimationType attackAnimation = GetAttackAnimation(
                ref unitLibrary.Value.units[(int)unitData.unitType],
                attackBlob.actionType);
            AnimationUtils.SetLayer(ref layers, AnimationLayerType.Action, attackAnimation, 1f, false);
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

