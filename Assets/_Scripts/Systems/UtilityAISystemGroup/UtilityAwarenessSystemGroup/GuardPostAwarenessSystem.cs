using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
[UpdateAfter(typeof(EnemyAwarenessSystem))]
[UpdateAfter(typeof(NavigationAwarenessSystem))]
public partial struct GuardPostAwarenessSystem : ISystem
{
    // Combat options use tier 3 (EnemyAwareness, SelfDefence, Flee) and critical flee uses 4;
    // 5 is the only value that preempts a live fight and still wins selection against every combat option.
    public const int LEASH_RETURN_PRIORITY = AIPriorityTier.Leash;

    // A guard walking home only turns back for a hostile well inside the leash, so an intruder
    // standing on the edge cannot make it flip-flop.
    public const float REENGAGE_LEASH_FRACTION = 0.8f;

    // Idle wander re-rolls its point every this many seconds so guards drift instead of twitching per frame.
    public const double IDLE_WANDER_BUCKET_SECONDS = 8.0;

    private ComponentLookup<CombatTarget>    combatTargetLookup;
    private ComponentLookup<AggressiveState> aggressiveStateLookup;
    private ComponentLookup<ActionInterruptRequest> actionInterruptRequestLookup;
    private ComponentLookup<LocalTransform>  transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BrainLibrary>();
        combatTargetLookup    = state.GetComponentLookup<CombatTarget>();
        aggressiveStateLookup = state.GetComponentLookup<AggressiveState>();
        actionInterruptRequestLookup = state.GetComponentLookup<ActionInterruptRequest>();
        transformLookup       = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        combatTargetLookup.Update(ref state);
        aggressiveStateLookup.Update(ref state);
        actionInterruptRequestLookup.Update(ref state);
        transformLookup.Update(ref state);

        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        state.Dependency = new GuardPostAwarenessJob
        {
            aiConfig              = brainLibrary.blob,
            combatTargetLookup    = combatTargetLookup,
            aggressiveStateLookup = aggressiveStateLookup,
            actionInterruptRequestLookup = actionInterruptRequestLookup,
            transformLookup       = transformLookup,
            idleTimeBucket        = (uint)(SystemAPI.Time.ElapsedTime / IDLE_WANDER_BUCKET_SECONDS),
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(GuardPost), typeof(UtilityBrain))]
[WithDisabled(typeof(Dead))]
public partial struct GuardPostAwarenessJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;
    // Each job iteration only touches its own entity's enable bit.
    [NativeDisableParallelForRestriction] public ComponentLookup<CombatTarget>    combatTargetLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<AggressiveState> aggressiveStateLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<ActionInterruptRequest> actionInterruptRequestLookup;
    public uint idleTimeBucket;

    public void Execute(
        Entity                            entity,
        ref DynamicBuffer<UtilityActions> actions,
        in GuardPost                      guardPost,
        in LocalTransform                 transform,
        in UtilityBrain                   brain,
        in StateMachine                   stateMachine)
    {
        float3 offsetFromAnchor     = transform.Position - guardPost.anchorPosition;
        float  distanceToAnchor     = math.length(new float2(offsetFromAnchor.x, offsetFromAnchor.z));

        int wanderDefIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.Wander);
        if (wanderDefIndex < 0) return;

        // Already walking home: keep going until inside the idle radius, otherwise the guard
        // re-acquires the intruder the instant it crosses back under the leash and thrashes at the edge.
        bool alreadyReturningHome = stateMachine.action == ActionType.Wander
            && stateMachine.hasTargetPosition
            && math.distancesq(stateMachine.targetPosition, guardPost.anchorPosition) < 1f
            && distanceToAnchor > guardPost.idleRadius;

        // The leash judges the target's distance to the anchor: a fight inside the guarded area is never abandoned,
        // a chase that dragged the guard out is. Returning guards use a tighter radius to avoid edge flip-flop.
        float engageRadius = alreadyReturningHome
            ? guardPost.leashRadius * GuardPostAwarenessSystem.REENGAGE_LEASH_FRACTION
            : guardPost.leashRadius;

        int keptCombatOptionCount = 0;
        for (int index = actions.Length - 1; index >= 0; index--)
        {
            UtilityActions option = actions[index];
            if (!option.actionType.IsCombatAction()) continue;

            bool insideGuardedArea;
            if (option.targetEntity == Entity.Null)
            {
                insideGuardedArea = option.hasTargetPosition && IsWithinEngageRadius(option.targetPosition, guardPost.anchorPosition, engageRadius);
            }
            else if (transformLookup.HasComponent(option.targetEntity))
            {
                insideGuardedArea = IsWithinEngageRadius(transformLookup[option.targetEntity].Position, guardPost.anchorPosition, engageRadius);
            }
            else
            {
                insideGuardedArea = false;
            }

            if (insideGuardedArea) keptCombatOptionCount++;
            else actions.RemoveAt(index);
        }

        if (keptCombatOptionCount > 0)
        {
            // The walk home runs at tier 5, which a tier-3 fight cannot preempt, so cancel it.
            if (alreadyReturningHome && actionInterruptRequestLookup.HasComponent(entity))
                actionInterruptRequestLookup.SetComponentEnabled(entity, true);
            return;
        }

        if (distanceToAnchor > guardPost.leashRadius || alreadyReturningHome)
        {

            if (combatTargetLookup.HasComponent(entity))
                combatTargetLookup.SetComponentEnabled(entity, false);
            if (aggressiveStateLookup.HasComponent(entity))
                aggressiveStateLookup.SetComponentEnabled(entity, false);

            actions.Add(new UtilityActions
            {
                actionType        = ActionType.Wander,
                actionDefIndex    = wanderDefIndex,
                priority          = GuardPostAwarenessSystem.LEASH_RETURN_PRIORITY,
                targetPosition    = guardPost.anchorPosition,
                hasTargetPosition = true,
            });
            return;
        }

        if (distanceToAnchor <= guardPost.idleRadius) return;

        for (int index = 0; index < actions.Length; index++)
        {
            if (actions[index].actionType.IsCombatAction()) return;
        }

        Random random = Random.CreateFromIndex((uint)entity.Index * 7919u + idleTimeBucket + 1u);
        float2 pointInDisc = random.NextFloat2Direction() * guardPost.idleRadius * math.sqrt(random.NextFloat());

        actions.Add(new UtilityActions
        {
            actionType        = ActionType.Wander,
            actionDefIndex    = wanderDefIndex,
            targetPosition    = guardPost.anchorPosition + new float3(pointInDisc.x, 0f, pointInDisc.y),
            hasTargetPosition = true,
        });
    }

    private static bool IsWithinEngageRadius(float3 position, float3 anchorPosition, float engageRadius)
    {
        float3 offset = position - anchorPosition;
        return math.length(new float2(offset.x, offset.z)) <= engageRadius;
    }
}
