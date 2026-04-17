using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

/// <summary>
/// AI attack loop — three jobs handle the full combat lifecycle.
///
/// CombatMoveJob    (CombatTarget enabled, not arrived):
///   Enables MoveToTargetRequest toward the hostile. MoveToTargetSystem handles pathing.
///
/// CombatAttackJob  (CombatTarget enabled, ArrivedAtTarget enabled):
///   Sets Target + enables Attack. Monitors drift and re-engages chasing if needed.
///   Cleans up and releases back to AI when the target dies.
///
/// CombatAbandonJob (CombatTarget disabled, NeedsAction disabled, category == Attack):
///   Hostile left awareness range — clean up all combat state and re-enable NeedsAction.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
[UpdateAfter(typeof(MoveToTargetSystem))]
public partial struct CombatAttackExecutionSystem : ISystem
{
    private ComponentLookup<Alive>          aliveLookup;
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();

        aliveLookup     = state.GetComponentLookup<Alive>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        aliveLookup.Update(ref state);
        transformLookup.Update(ref state);

        state.Dependency = new CombatMoveJob
        {
            aliveLookup = aliveLookup,
        }.Schedule(state.Dependency);

        state.Dependency = new CombatAttackJob
        {
            aliveLookup     = aliveLookup,
            transformLookup = transformLookup,
        }.Schedule(state.Dependency);

        state.Dependency = new CombatAbandonJob().Schedule(state.Dependency);
    }
}

// ── Phase A: move toward hostile ─────────────────────────────────────────────
[BurstCompile]
[WithAll(typeof(CombatTarget))]
[WithDisabled(typeof(NeedsAction))]
[WithDisabled(typeof(ArrivedAtTarget))]
[WithPresent(typeof(MoveToTargetRequest))]
[WithDisabled(typeof(PlayerControlled))]
public partial struct CombatMoveJob : IJobEntity
{
    private const float MELEE_RANGE = 1.5f;

    [ReadOnly] public ComponentLookup<Alive> aliveLookup;

    public void Execute(
        in SelectedAction                   selectedAction,
        in CombatTarget                     combatTarget,
        ref MoveToTargetRequest             moveRequest,
        EnabledRefRW<MoveToTargetRequest>   moveRequestEnabled,
        EnabledRefRW<NeedsAction>           needsActionEnabled,
        EnabledRefRW<CombatTarget>          combatTargetEnabled)
    {
        if (selectedAction.category != ActionCategory.Attack)
            return;

        Entity hostile = combatTarget.targetEntity;

        bool targetAlive = hostile != Entity.Null &&
                           !(aliveLookup.HasComponent(hostile) && !aliveLookup.IsComponentEnabled(hostile));

        if (!targetAlive)
        {
            combatTargetEnabled.ValueRW  = false;
            moveRequestEnabled.ValueRW   = false;
            needsActionEnabled.ValueRW   = true;
            return;
        }

        moveRequest.targetEntity = hostile;
        moveRequest.arrivalRange = MELEE_RANGE;
        moveRequestEnabled.ValueRW = true;
    }
}

// ── Phase B: attack at range ──────────────────────────────────────────────────
[BurstCompile]
[WithAll(typeof(CombatTarget))]
[WithDisabled(typeof(NeedsAction))]
[WithAll(typeof(ArrivedAtTarget))]
[WithPresent(typeof(Attack), typeof(Target))]
[WithDisabled(typeof(PlayerControlled))]
public partial struct CombatAttackJob : IJobEntity
{
    private const float MELEE_RANGE            = 1.5f;
    private const float MELEE_RANGE_HYSTERESIS = 2.0f;

    [ReadOnly] public ComponentLookup<Alive>          aliveLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;

    public void Execute(
        in SelectedAction                selectedAction,
        in LocalTransform                myTransform,
        in CombatTarget                  combatTarget,
        in AttackCooldown                cooldown,
        ref Target                       target,
        EnabledRefRW<CombatTarget>       combatTargetEnabled,
        EnabledRefRW<ArrivedAtTarget>    arrivedEnabled,
        EnabledRefRW<Attack>             attackEnabled,
        EnabledRefRW<Target>             targetEnabled,
        EnabledRefRW<NeedsAction>        needsActionEnabled)
    {
        if (selectedAction.category != ActionCategory.Attack)
            return;

        Entity hostile = combatTarget.targetEntity;

        bool targetAlive = hostile != Entity.Null &&
                           !(aliveLookup.HasComponent(hostile) && !aliveLookup.IsComponentEnabled(hostile));

        if (!targetAlive)
        {
            attackEnabled.ValueRW       = false;
            targetEnabled.ValueRW       = false;
            combatTargetEnabled.ValueRW = false;
            arrivedEnabled.ValueRW      = false;
            needsActionEnabled.ValueRW  = true;
            return;
        }

        target.entity         = hostile;
        targetEnabled.ValueRW = true;

        if (cooldown.timer <= 0f)
            attackEnabled.ValueRW = true;

        // If target drifted out of melee range, go back to chasing
        if (transformLookup.TryGetComponent(hostile, out LocalTransform hostileTransform))
        {
            float distSq = math.distancesq(myTransform.Position, hostileTransform.Position);
            if (distSq > MELEE_RANGE_HYSTERESIS * MELEE_RANGE_HYSTERESIS)
            {
                attackEnabled.ValueRW  = false;
                arrivedEnabled.ValueRW = false;
            }
        }
    }
}

// ── Cleanup: hostile left range mid-action ────────────────────────────────────
[BurstCompile]
[WithDisabled(typeof(CombatTarget))]
[WithDisabled(typeof(NeedsAction))]
[WithPresent(typeof(MoveToTargetRequest), typeof(ArrivedAtTarget), typeof(Attack), typeof(Target))]
[WithDisabled(typeof(PlayerControlled))]
public partial struct CombatAbandonJob : IJobEntity
{
    public void Execute(
        in SelectedAction                 selectedAction,
        EnabledRefRW<MoveToTargetRequest> moveRequestEnabled,
        EnabledRefRW<ArrivedAtTarget>     arrivedEnabled,
        EnabledRefRW<Attack>              attackEnabled,
        EnabledRefRW<Target>              targetEnabled,
        EnabledRefRW<NeedsAction>         needsActionEnabled)
    {
        if (selectedAction.category != ActionCategory.Attack)
            return;

        moveRequestEnabled.ValueRW = false;
        arrivedEnabled.ValueRW     = false;
        attackEnabled.ValueRW      = false;
        targetEnabled.ValueRW      = false;
        needsActionEnabled.ValueRW = true;
    }
}
