using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

[BurstCompile]
[UpdateInGroup(typeof(AIActionSystemGroup))]
public partial struct MeleeSingleActionSystem : ISystem
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

        state.Dependency = new MeleeSingleActionJob
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
[WithDisabled(typeof(ActionRequest))]
[WithPresent(typeof(PathRequest), typeof(Target), typeof(AttackRequest))]
public partial struct MeleeSingleActionJob : IJobEntity
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
        ref AttackRequest     attackRequest,
        ref DynamicBuffer<AnimationLayer> layers,
        EnabledRefRW<MeleeSingleAction>   meleeSingle,
        EnabledRefRW<ActionRequest>   actionRequest,
        EnabledRefRW<PathRequest>   pathRequestEnabled,
        EnabledRefRW<Target>        targetEnabled,
        EnabledRefRW<AttackRequest> attackRequestEnabled)
    {
        Entity hostile = combatTarget.targetEntity;

        if (!transformLookup.TryGetComponent(hostile, out LocalTransform targetTransform))
        {
            targetEnabled.ValueRW      = false;
            attackRequestEnabled.ValueRW = false;
            attackRequest.hitFired     = false;
            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled, localTransform);
            actionRequest.ValueRW = true;
            meleeSingle.ValueRW        = false;
            return;
        }

        if (!AIUtils.IsTargetAlive(hostile, aliveLookup) ||
            AIUtils.IsTargetOutOfRange(localTransform, targetTransform, awareness.range))
        {
            targetEnabled.ValueRW      = false;
            attackRequestEnabled.ValueRW = false;
            attackRequest.hitFired     = false;
            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled, localTransform);
            actionRequest.ValueRW = true;
            meleeSingle.ValueRW        = false;
            return;
        }

        int attackIndex = (int)currentAction.attackType;
        if (attackIndex < 0 || attackIndex >= attackLibrary.Value.attacks.Length)
        {
            actionRequest.ValueRW = true;
            meleeSingle.ValueRW        = false;
            return;
        }
        ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];
        float distSq     = math.distancesq(localTransform.Position, targetTransform.Position);
        float breakOffSq = (attackBlob.range * HYSTERESIS_MULT) * (attackBlob.range * HYSTERESIS_MULT);

        if (distSq > breakOffSq)
        {
            targetEnabled.ValueRW = false;
            float movedSq = math.distancesq(pathRequest.targetPosition, targetTransform.Position);
            if (movedSq >= REPATH_DIST_SQ)
                AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, targetTransform.Position);
            return;
        }

        AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled, localTransform);
        target.entity     = hostile;
        targetEnabled.ValueRW = true;

        if (cooldown.timer > 0f && !attackRequestEnabled.ValueRO)
        {
            actionRequest.ValueRW = true;
            meleeSingle.ValueRW   = false;
            return;
        }

        if (cooldown.timer <= 0f && !attackRequestEnabled.ValueRO)
        {
            attackRequest.targetEntity   = hostile;
            attackRequest.hitTime        = attackBlob.hitTime;
            attackRequest.hitFired       = false;
            attackRequestEnabled.ValueRW = true;
            cooldown.timer               = attackBlob.cooldown;

            // Start the attack animation on the Action layer
            int unitIndex = (int)unitData.unitType;
            AnimationType attackAnimation = AnimationType.None;
            if (unitIndex >= 0 && unitIndex < unitLibrary.Value.units.Length)
                attackAnimation = GetAttackAnimation(ref unitLibrary.Value.units[unitIndex], attackBlob.actionType);
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

