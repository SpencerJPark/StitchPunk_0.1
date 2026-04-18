using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

/// <summary>
/// AI attack loop — three jobs handle the full combat lifecycle.
///
/// CombatMoveJob    (CombatTarget enabled, not arrived):
///   Issues a path request toward the hostile with stoppingDistance = attack range.
///   PathfindingCoordinatorSystem halts the follower once the unit is inside range.
///
/// CombatAttackJob  (CombatTarget enabled, ArrivedAtTarget enabled):
///   Sets Target + enables Attack. Monitors drift and re-engages chasing if needed.
///   Cleans up and releases back to AI when the target dies.
///
/// CombatAbandonJob (CombatTarget disabled, NeedsAction disabled, category == Attack):
///   Hostile left awareness range — halt pathing, clean up all combat state, re-enable NeedsAction.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct CombatAttackExecutionSystem : ISystem
{
    private ComponentLookup<Alive>          aliveLookup;
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<AttackLibrary>();

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

        state.Dependency = new CombatMoveJob
        {
            aliveLookup     = aliveLookup,
            transformLookup = transformLookup,
            attackLibrary   = attackLibrary,
        }.Schedule(state.Dependency);

        state.Dependency = new CombatAttackJob
        {
            aliveLookup     = aliveLookup,
            transformLookup = transformLookup,
            attackLibrary   = attackLibrary,
        }.Schedule(state.Dependency);

        state.Dependency = new CombatAbandonJob().Schedule(state.Dependency);
    }
}

// ── Phase A: move toward hostile ─────────────────────────────────────────────
[BurstCompile]
[WithAll(typeof(ActiveBrain))]
[WithDisabled(typeof(NeedsAction))]
[WithAll(typeof(CombatTarget))]
[WithPresent(typeof(PathRequest), typeof(ArrivedAtTarget))]
public partial struct CombatMoveJob : IJobEntity
{
    private const float REPATH_DIST_SQ = 1.0f;
    
    [ReadOnly] public ComponentLookup<Alive>                  aliveLookup;
    [ReadOnly] public ComponentLookup<LocalTransform>         transformLookup;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob>   attackLibrary;

    public void Execute(
        in  SelectedAction            selectedAction,
        in  LocalTransform            localTransform,
        ref CombatTarget              combatTarget,
        ref PathRequest               pathRequest,
        ref PathfindingAgent          agent,
        ref Movement                  movement,
        EnabledRefRW<PathRequest>     pathRequestEnabled,
        EnabledRefRW<NeedsAction>     needsActionEnabled,
        EnabledRefRW<ArrivedAtTarget> arrivedEnabled,
        EnabledRefRW<CombatTarget>    combatTargetEnabled)
    {
        if (selectedAction.category != ActionCategory.Attack)
            return;

        Entity hostile = combatTarget.targetEntity;

        bool targetAlive = hostile != Entity.Null &&
                           !(aliveLookup.HasComponent(hostile) && !aliveLookup.IsComponentEnabled(hostile));

        if (!targetAlive)
        {
            combatTargetEnabled.ValueRW  = false;
            needsActionEnabled.ValueRW   = true;
            return;
        }

        float attackRange         = attackLibrary.Value.attacks[(int)combatTarget.selectedAttack].range;
        float3 lastKnownTargetPos = transformLookup[hostile].Position;

        if (AIUtils.CheckInRange(localTransform, ref arrivedEnabled, lastKnownTargetPos, attackRange))
        {
            // Cancel any stale PathRequest scheduled by the coordinator's periodic
            // repath — otherwise DStarLiteSystem/FlowFieldSystem will process it
            // this same frame and re-set agent.isActive = true, bypassing the halt.
            pathRequestEnabled.ValueRW = false;
            return;
        }
        
        float movedSq = math.distancesq(pathRequest.targetPosition, lastKnownTargetPos);
        if (movedSq >= REPATH_DIST_SQ)
        {
            AIUtils.BeginPathRequest(
                ref pathRequest, pathRequestEnabled,
                ref agent, ref movement,
                targetPosition: lastKnownTargetPos,
                stoppingDistance: attackRange,
                modeOverride: PathfindingMode.DStarLite);
        }
    }
}

// ── Phase B: attack at range ──────────────────────────────────────────────────
[BurstCompile]
[WithAll(typeof(CombatTarget))]
[WithDisabled(typeof(NeedsAction))]
[WithAll(typeof(ArrivedAtTarget))]
[WithPresent(typeof(Attack), typeof(Target))]
[WithAll(typeof(ActiveBrain))]
public partial struct CombatAttackJob : IJobEntity
{
    // Target must drift this much past attack range before we resume chasing —
    // prevents flapping between Arrived and Move when the hostile fidgets on the boundary.
    private const float HYSTERESIS_MULT = 1.33f;

    [ReadOnly] public ComponentLookup<Alive>                  aliveLookup;
    [ReadOnly] public ComponentLookup<LocalTransform>         transformLookup;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob>   attackLibrary;

    public void Execute(
        in  SelectedAction               selectedAction,
        in  LocalTransform               myTransform,
        ref CombatTarget                 combatTarget,
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

        // If target drifted out of attack range, go back to chasing
        if (transformLookup.TryGetComponent(hostile, out LocalTransform hostileTransform))
        {
            float attackRange   = attackLibrary.Value.attacks[(int)combatTarget.selectedAttack].range;
            float hysteresis    = attackRange * HYSTERESIS_MULT;
            float distSq        = math.distancesq(myTransform.Position, hostileTransform.Position);
            if (distSq > hysteresis * hysteresis)
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
[WithPresent(typeof(PathRequest), typeof(DStarLiteFollower), typeof(FlowFieldFollower),
             typeof(ArrivedAtTarget), typeof(Attack), typeof(Target))]
[WithAll(typeof(ActiveBrain))]
public partial struct CombatAbandonJob : IJobEntity
{
    public void Execute(
        in  SelectedAction                selectedAction,
        ref PathfindingAgent              agent,
        EnabledRefRW<PathRequest>         pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower>   dstarFollowerEnabled,
        EnabledRefRW<FlowFieldFollower>   flowFollowerEnabled,
        EnabledRefRW<ArrivedAtTarget>     arrivedEnabled,
        EnabledRefRW<Attack>              attackEnabled,
        EnabledRefRW<Target>              targetEnabled,
        EnabledRefRW<NeedsAction>         needsActionEnabled)
    {
        if (selectedAction.category != ActionCategory.Attack)
            return;

        AIUtils.HaltPathing(ref agent, pathRequestEnabled, dstarFollowerEnabled, flowFollowerEnabled);

        arrivedEnabled.ValueRW     = false;
        attackEnabled.ValueRW      = false;
        targetEnabled.ValueRW      = false;
        needsActionEnabled.ValueRW = true;
    }
}
