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
[WithAll(typeof(ActiveBrain), typeof(MeleeSingleAction))]
[WithDisabled(typeof(ActionRequest))]
[WithPresent(typeof(Target), typeof(PathRequest), typeof(AttackRequest), typeof(AnimationRequest))]
public partial struct MeleeSingleActionJob : IJobEntity
{
    private const float REPATH_DIST_SQ  = 1.0f;
    private const float HYSTERESIS_MULT = 1.33f;

    [ReadOnly] public ComponentLookup<Alive> aliveLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob> attackLibrary;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> unitLibrary;

    public void Execute(
        in LocalTransform localTransform,
        in Awareness awareness,
        in UnitData unitData,
        in CurrentAction currentAction,
        ref AttackCooldown cooldown,
        ref CombatTarget combatTarget,
        ref PathRequest pathRequest,
        ref Target target,
        ref AttackRequest attackRequest,
        ref DynamicBuffer<SetAnimation> setAnimations,
        EnabledRefRW<MeleeSingleAction> meleeSingle,
        EnabledRefRW<ActionRequest> actionRequest,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<Target> targetEnabled,
        EnabledRefRW<AttackRequest> attackRequestEnabled,
        EnabledRefRW<AnimationRequest> animationRequestEnabled)
    {
        Entity hostile = combatTarget.targetEntity;

        // 1. Validation & Early Exits
        bool targetMissing = !transformLookup.TryGetComponent(hostile, out LocalTransform targetTransform);
        bool targetInvalid = !targetMissing && (!AIUtils.IsTargetAlive(hostile, aliveLookup) || AIUtils.IsTargetOutOfRange(localTransform, targetTransform, awareness.range));
        int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);

        if (targetMissing || targetInvalid || unitIndex < 0)
        {
            Terminate(ref attackRequest, ref pathRequest, meleeSingle, targetEnabled, actionRequest, pathRequestEnabled, attackRequestEnabled);
            return;
        }

        // 2. Attack Data Lookup
        AttackType attackType = AIUtils.GetAttackByAction(ref unitLibrary.Value.units[unitIndex], currentAction.actionType);
        int attackIndex = (int)attackType;
        if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length)
        {
            Terminate(ref attackRequest, ref pathRequest, meleeSingle, targetEnabled, actionRequest, pathRequestEnabled, attackRequestEnabled);
            return;
        }

        ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];
        float distSq = math.distancesq(localTransform.Position, targetTransform.Position);
        float maxRange = attackBlob.range * HYSTERESIS_MULT;

        // 3. Movement Logic
        if (distSq > (maxRange * maxRange))
        {
            targetEnabled.ValueRW = false;
            TryRepath(targetTransform.Position, attackBlob.range, ref pathRequest, pathRequestEnabled);
            return;
        }

        // 4. Combat Logic
        AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
        target.entity = hostile;
        targetEnabled.ValueRW = true;

        // Single action specific: if on cooldown, we are done
        if (cooldown.timer > 0f && !attackRequestEnabled.ValueRO)
        {
            meleeSingle.ValueRW = false;
            actionRequest.ValueRW = true;
            return;
        }

        if (cooldown.timer <= 0f && !attackRequestEnabled.ValueRO)
        {
            FireAttack(hostile, attackType, attackBlob.cooldown, unitIndex, ref attackRequest, ref setAnimations, attackRequestEnabled, animationRequestEnabled);
            cooldown.timer = attackBlob.cooldown;
        }
    }

    private void Terminate(
        ref AttackRequest attackReq, 
        ref PathRequest pathReq,
        EnabledRefRW<MeleeSingleAction> meleeSingle,
        EnabledRefRW<Target> targetEnabled,
        EnabledRefRW<ActionRequest> actionRequest,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<AttackRequest> attackRequestEnabled)
    {
        targetEnabled.ValueRW = false;
        attackRequestEnabled.ValueRW = false;
        attackReq.hitFired = false;
        AIUtils.HaltPathing(ref pathReq, pathRequestEnabled);
        actionRequest.ValueRW = true;
        meleeSingle.ValueRW = false;
    }

    private void TryRepath(float3 targetPos, float range, ref PathRequest pathReq, EnabledRefRW<PathRequest> pathReqEnabled)
    {
        if (math.distancesq(pathReq.targetPosition, targetPos) >= REPATH_DIST_SQ)
        {
            AIUtils.BeginPathRequest(ref pathReq, pathReqEnabled, targetPos, range * 0.9f);
        }
    }

    private void FireAttack(
        Entity hostile, 
        AttackType type, 
        float cooldownTime, 
        int unitIndex,
        ref AttackRequest attackReq, 
        ref DynamicBuffer<SetAnimation> anims,
        EnabledRefRW<AttackRequest> attackReqEnabled,
        EnabledRefRW<AnimationRequest> animReqEnabled)
    {
        attackReq.targetEntity = hostile;
        attackReq.attackType = type;
        attackReq.hitFired = false;
        attackReqEnabled.ValueRW = true;

        AnimationType animType = AIUtils.GetAnimationByAction(ref unitLibrary.Value.units[unitIndex], ActionType.MeleeSingle);
        anims.Add(new SetAnimation { layer = AnimationLayerType.Action, animation = animType, speed = 1f });
        animReqEnabled.ValueRW = true;
    }
}

