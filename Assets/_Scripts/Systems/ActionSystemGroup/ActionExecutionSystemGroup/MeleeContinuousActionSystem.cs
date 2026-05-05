using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup))]
public partial struct MeleeContinuousActionSystem : ISystem
{
    private ComponentLookup<Dead>          deadLookup;
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<UnitDataLibrary>();

        deadLookup = state.GetComponentLookup<Dead>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        deadLookup.Update(ref state);
        transformLookup.Update(ref state);

        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;

        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        state.Dependency = new MeleeContinuousActionJob
        {
            deadLookup   = deadLookup,
            transformLookup = transformLookup,
            attackLibrary = attackLibrary,
            unitLibrary   = unitLibrary,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(MeleeContinuousAction))]
[WithDisabled(typeof(ActionRequest))]
[WithPresent(typeof(Dead), typeof(PathRequest), typeof(AttackRequest), typeof(AnimationRequest))]
public partial struct MeleeContinuousActionJob : IJobEntity
{
    private const float REPATH_DIST_SQ  = 1.0f;
    private const float HYSTERESIS_MULT = 1.33f;

    [ReadOnly] public ComponentLookup<Dead> deadLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob> attackLibrary;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> unitLibrary;

    public void Execute(
        in LocalTransform localTransform,
        in UnitData unitData,
        ref CurrentAction action,
        ref AttackCooldown cooldown,
        ref PathRequest pathRequest,
        ref AttackRequest attackRequest,
        ref DynamicBuffer<SetAnimation> setAnimations,
        EnabledRefRO<Dead> dead,
        EnabledRefRW<MeleeContinuousAction> meleeContinuous,
        EnabledRefRW<ActionRequest> actionRequest,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<AttackRequest> attackRequestEnabled,
        EnabledRefRW<AnimationRequest> animationRequestEnabled)
    {
        Entity hostile = action.targetEntity;

        // 1. Validation & Early Exits
        bool targetMissing = !transformLookup.TryGetComponent(hostile, out LocalTransform targetTransform);
        int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
        ref UnitDataBlob unit = ref unitLibrary.Value.units[unitIndex];
        bool targetInvalid = !targetMissing && (AIUtils.IsTargetDead(hostile, deadLookup) || AIUtils.IsTargetOutOfRange(localTransform, targetTransform, unit.awarenessRange));
        bool isDead = dead.ValueRO;

        if (targetMissing || targetInvalid || unitIndex < 0 || isDead)
        {
            Terminate(ref attackRequest, ref pathRequest, meleeContinuous, actionRequest, pathRequestEnabled, attackRequestEnabled);
            return;
        }

        // 2. Attack Data Lookup
        AttackType attackType = AIUtils.GetAttackByAction(ref unit, ActionType.MeleeContinuous);
        int attackIndex = (int)attackType;
        if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length)
        {
            Terminate(ref attackRequest, ref pathRequest, meleeContinuous, actionRequest, pathRequestEnabled, attackRequestEnabled);
            return;
        }

        ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];
        float distSq = math.distancesq(localTransform.Position, targetTransform.Position);
        
        // Use the larger of the two ranges for the "out of range" check
        float maxRange = attackBlob.range * HYSTERESIS_MULT;
        if (distSq > (maxRange * maxRange))
        {
            if (math.distancesq(pathRequest.targetPosition, targetTransform.Position) >= REPATH_DIST_SQ)
                AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, targetTransform.Position, attackBlob.range * 0.9f);
            return;
        }

        float rangeSq = attackBlob.range * attackBlob.range;
        if (distSq > rangeSq)
        {
            if (math.distancesq(pathRequest.targetPosition, targetTransform.Position) >= REPATH_DIST_SQ)
                AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, targetTransform.Position, attackBlob.range * 0.9f);
            return;
        }

        // 3. Within Attack Range: Halt and Execute Attack
        AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);

        if (cooldown.timer <= 0f && !attackRequestEnabled.ValueRO)
        {
            FireAttack(hostile, attackType, attackBlob.cooldown, ref unit, ref attackRequest, ref setAnimations, attackRequestEnabled, animationRequestEnabled);
            cooldown.timer = attackBlob.cooldown;
        }
    }

    // --- Helpers ---

    private void Terminate(
        ref AttackRequest attackReq, 
        ref PathRequest pathReq,
        EnabledRefRW<MeleeContinuousAction> meleeContinuous,
        EnabledRefRW<ActionRequest> actionRequest,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<AttackRequest> attackRequestEnabled)
    {
        attackRequestEnabled.ValueRW = false;
        attackReq.hitFired = false;
        AIUtils.HaltPathing(ref pathReq, pathRequestEnabled);
        actionRequest.ValueRW = true;
        meleeContinuous.ValueRW = false;
    }

    private void FireAttack(
        Entity hostile, 
        AttackType type, 
        float cooldownTime, 
        ref UnitDataBlob unit,
        ref AttackRequest attackReq, 
        ref DynamicBuffer<SetAnimation> anims,
        EnabledRefRW<AttackRequest> attackReqEnabled,
        EnabledRefRW<AnimationRequest> animReqEnabled)
    {
        attackReq.targetEntity = hostile;
        attackReq.attackType = type;
        attackReq.hitFired = false;
        attackReqEnabled.ValueRW = true;

        AnimationType animType = AIUtils.GetAnimationByAction(ref unit, ActionType.MeleeContinuous);
        anims.Add(new SetAnimation { layer = AnimationLayerType.Action, animation = animType, speed = 1f });
        animReqEnabled.ValueRW = true;
    }
}

