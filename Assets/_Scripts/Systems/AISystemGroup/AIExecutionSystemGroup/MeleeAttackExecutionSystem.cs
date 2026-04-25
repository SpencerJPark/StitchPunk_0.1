using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using System.Runtime.CompilerServices;

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
public partial struct MeleeAttackExecutionSystem : ISystem
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

        EntityCommandBuffer.ParallelWriter ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();

        state.Dependency = new MeleeProcessorJob
        {
            aliveLookup = aliveLookup,
            transformLookup = transformLookup,
            attackLibrary = attackLibrary,
            ecb = ecb
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain))]
[WithDisabled(typeof(NeedsAction))]
[WithPresent(typeof(PathRequest))]
public partial struct MeleeProcessorJob : IJobEntity
{
    private const float REPATH_DIST_SQ = 1.0f;
    private const float HYSTERESIS_MULT = 1.33f;

    [ReadOnly] public ComponentLookup<Alive> aliveLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob> attackLibrary;
    public EntityCommandBuffer.ParallelWriter ecb;

    public void Execute(
        in LocalTransform localTransform,
        in Awareness awareness,
        in AttackCooldown cooldown,
        in CurrentAction currentAction,
        ref CombatTarget combatTarget,
        ref PathRequest pathRequest,
        in DynamicBuffer<AvailableAttack> attacks,
        EnabledRefRW<MeleeAction> meleeAction,
        EnabledRefRW<NeedsAction> needsActionEnabled,
        EnabledRefRW<PathRequest> pathRequestEnabled)
    {
        Entity hostile = combatTarget.targetEntity;
        
        if (!transformLookup.TryGetComponent(hostile, out LocalTransform targetTransform))
        {
            AIUtils.HaltPathing(ref pathRequest, ref pathRequestEnabled, localTransform);
            needsActionEnabled.ValueRW = true;
            meleeAction.ValueRW = false;
            return;
        }
        
        if (!AIUtils.IsTargetAlive(hostile, aliveLookup) && !AIUtils.IsTargetOutOfRange(localTransform, targetTransform, awareness.range))
        {
            AIUtils.HaltPathing(ref pathRequest, ref pathRequestEnabled, localTransform);
            needsActionEnabled.ValueRW = true;
            meleeAction.ValueRW = false;
            return;
        }
        
        if (!AIUtils.IsTargetInRange(localTransform, targetTransform, awareness.range))
        {
            float movedSq = math.distancesq(pathRequest.targetPosition, targetTransform.Position);
            if (movedSq >= REPATH_DIST_SQ)
            {
                AIUtils.BeginPathRequest(ref pathRequest, ref pathRequestEnabled, targetTransform.Position);
            }
            return;
        }
        
        AIUtils.HaltPathing(ref pathRequest, ref pathRequestEnabled, localTransform);
        
        // Attack Logic
        // Fetch Attack Info
        // Start Attack, Hit, start animation, timer repeat

    }
    

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetCombatMetrics(Entity hostile, AttackType attackType, LocalTransform myTransform,
        out float3 hostilePos, out float attackRange, out float distSq)
    {
        hostilePos = default;
        attackRange = default;
        distSq = default;

        if (!transformLookup.TryGetComponent(hostile, out LocalTransform hostileTransform))
            return false;

        hostilePos = hostileTransform.Position;
        attackRange = attackLibrary.Value.attacks[(int)attackType].range;
        distSq = math.distancesq(myTransform.Position, hostilePos);
        return true;
    }

    // Returns true when the attack swing fires this frame (Attack just enabled).
    // Caller uses this to hand back to NeedsAction for re-evaluation.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HandleAttackingState(
        Entity hostile, float distSq, float attackRange, AttackCooldown cooldown,
        ref Target target, EnabledRefRW<Target> targetEnabled,
        EnabledRefRW<Attack> attackEnabled, EnabledRefRW<ArrivedAtTarget> arrivedEnabled)
    {
        float breakChaseThreshold = attackRange * HYSTERESIS_MULT;

        if (distSq > (breakChaseThreshold * breakChaseThreshold))
        {
            arrivedEnabled.ValueRW = false;
            attackEnabled.ValueRW = false;
            targetEnabled.ValueRW = false;
            return false;
        }

        target.entity = hostile;
        targetEnabled.ValueRW = true;

        if (cooldown.timer <= 0f)
        {
            attackEnabled.ValueRW = true;
            return true;
        }

        return false;
    }
    
}

