using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Executes the Flee action. On activation the unit reads its highest-threat attacker from the
// ThreatEntry buffer, then greedily chains away-from-attacker waypoints: it picks the nearest
// waypoint in the away direction, re-searches from there, and repeats (up to MAX_FLEE_HOPS)
// until the target lies beyond the attacker's awareness range. It then runs to that far target
// at run speed. On arrival it hands control back to selection by re-enabling ActionRequest, so
// flee re-chains while the unit stays wounded and threatened.
[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup))]
public partial struct FleeActionSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Awareness>      awarenessLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<SpatialHashRegistry>();

        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        awarenessLookup = state.GetComponentLookup<Awareness>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        awarenessLookup.Update(ref state);

        SpatialHashRegistry registry = SystemAPI.GetSingleton<SpatialHashRegistry>();

        state.Dependency = new FleeActionJob
        {
            waypointCells   = registry.waypointCells,
            transformLookup = transformLookup,
            awarenessLookup = awarenessLookup,
            deltaTime       = SystemAPI.Time.DeltaTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(FleeAction))]
[WithDisabled(typeof(ActionRequest), typeof(Dead))]
[WithPresent(typeof(PathRequest), typeof(DStarLiteFollower), typeof(ActionTimer))]
public partial struct FleeActionJob : IJobEntity
{
    private const float ARRIVE_RANGE       = 1.5f;
    private const float STOPPING_DIST      = 1.0f;
    private const float FLEE_SEARCH_RADIUS = 40f;   // per-hop waypoint search radius
    private const float MIN_FLEE_DIST      = 5f;
    private const float AWAY_DOT_MIN       = 0.25f;
    private const float FLEE_FALLBACK_DIST = 20f;
    private const int   MAX_FLEE_HOPS      = 4;     // cap on chained waypoints per activation
    private const float OUT_OF_RANGE_BUFFER = 5f;   // added to attacker awareness range
    private const float SAFE_DIST_FALLBACK = 50f;   // used when attacker has no Awareness
    private const float TIMEOUT_SLACK      = 5f;    // added to distance/runSpeed travel estimate

    [ReadOnly] public NativeParallelMultiHashMap<int2, Entity> waypointCells;
    [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;
    [ReadOnly] public ComponentLookup<Awareness>               awarenessLookup;
    public float deltaTime;

    public void Execute(
        in LocalTransform               transform,
        in DynamicBuffer<ThreatEntry>   threats,
        ref PathRequest                 pathRequest,
        ref Movement                    movement,
        ref ActionTimer                 actionTimer,
        EnabledRefRW<FleeAction>        fleeActionEnabled,
        EnabledRefRW<ActionRequest>     actionRequestEnabled,
        EnabledRefRW<PathRequest>       pathRequestEnabled,
        EnabledRefRW<ActionTimer>       actionTimerEnabled,
        EnabledRefRO<DStarLiteFollower> dStarLiteFollowerEnabled)
    {
        float3 unitPos = transform.Position;

        // Arrival: reached the escape waypoint — hand back to selection.
        // On the activation frame pathRequest.targetPosition is float3(MaxValue) (set by
        // PathRequestSystem on the prior Stop), so this never false-fires before pathing.
        if (math.distancesq(unitPos, pathRequest.targetPosition) <= ARRIVE_RANGE * ARRIVE_RANGE)
        {
            Terminate(ref pathRequest, ref movement, ref actionTimer,
                fleeActionEnabled, actionRequestEnabled, pathRequestEnabled, actionTimerEnabled);
            return;
        }

        // En route: count down the timeout guard, give up if pathing stalls.
        if (pathRequestEnabled.ValueRO || dStarLiteFollowerEnabled.ValueRO)
        {
            actionTimer.time -= deltaTime;
            if (actionTimer.time <= 0f)
            {
                Terminate(ref pathRequest, ref movement, ref actionTimer,
                    fleeActionEnabled, actionRequestEnabled, pathRequestEnabled, actionTimerEnabled);
            }
            return;
        }

        // Fresh activation: choose an escape destination and start running there.
        Entity topAttacker = Entity.Null;
        float  topThreat   = 0f;
        for (int i = 0; i < threats.Length; i++)
        {
            if (threats[i].threatScore > topThreat)
            {
                topThreat   = threats[i].threatScore;
                topAttacker = threats[i].attackerEntity;
            }
        }

        // No live threat to flee from — resume normal AI.
        if (topAttacker == Entity.Null ||
            !transformLookup.TryGetComponent(topAttacker, out LocalTransform attackerTransform))
        {
            Terminate(ref pathRequest, ref movement, ref actionTimer,
                fleeActionEnabled, actionRequestEnabled, pathRequestEnabled, actionTimerEnabled);
            return;
        }

        float3 attackerPos = attackerTransform.Position;

        // "Out of range" = beyond the attacker's awareness range (+buffer), else a fixed fallback.
        float outOfRange = awarenessLookup.TryGetComponent(topAttacker, out Awareness attackerAwareness)
            ? attackerAwareness.range + OUT_OF_RANGE_BUFFER
            : SAFE_DIST_FALLBACK;
        float outOfRangeSq = outOfRange * outOfRange;

        // Greedy spatial walk: step to the nearest away-waypoint, re-search from there, repeat
        // until the target clears the attacker's range or we hit the hop cap. awayDir is recomputed
        // from the attacker each hop, so every pick moves further from the attacker.
        float3 searchOrigin = unitPos;
        float3 fleeTarget   = unitPos;
        bool   found        = false;
        for (int hop = 0; hop < MAX_FLEE_HOPS; hop++)
        {
            float3 awayDir = math.normalizesafe(searchOrigin - attackerPos);
            if (!TryFindNearestEscapeWaypoint(searchOrigin, awayDir, out float3 nextWaypoint))
                break;
            fleeTarget   = nextWaypoint;
            found        = true;
            searchOrigin = nextWaypoint;
            if (math.distancesq(nextWaypoint, attackerPos) >= outOfRangeSq)
                break;
        }

        // No waypoints reachable — fall back to a raw away-vector point.
        if (!found)
            fleeTarget = unitPos + math.normalizesafe(unitPos - attackerPos) * FLEE_FALLBACK_DIST;

        movement.isRunning         = true;
        float distToTarget         = math.distance(unitPos, fleeTarget);
        actionTimer.time           = distToTarget / movement.runSpeed + TIMEOUT_SLACK;
        actionTimerEnabled.ValueRW = true;
        AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, fleeTarget, STOPPING_DIST);
    }

    // Searches the waypoint spatial hash for the nearest waypoint that lies in the
    // away-from-attacker hemisphere, between MIN_FLEE_DIST and FLEE_SEARCH_RADIUS of origin.
    // The MIN_FLEE_DIST floor prevents re-selecting the origin waypoint on chained hops.
    private bool TryFindNearestEscapeWaypoint(float3 origin, float3 awayDir, out float3 bestPos)
    {
        bestPos = float3.zero;
        float minDistSq    = MIN_FLEE_DIST * MIN_FLEE_DIST;
        float bestDistSq   = float.MaxValue;
        bool  found        = false;

        int2 centerCell = InteractionSpatialHashSystem.GetCell(origin);
        int  cellRange  = (int)math.ceil(FLEE_SEARCH_RADIUS / InteractionSpatialHashSystem.CELL_SIZE);
        float searchRangeSq = FLEE_SEARCH_RADIUS * FLEE_SEARCH_RADIUS;

        for (int x = -cellRange; x <= cellRange; x++)
        {
            for (int z = -cellRange; z <= cellRange; z++)
            {
                int2 targetCell = centerCell + new int2(x, z);
                if (!waypointCells.TryGetFirstValue(
                        targetCell, out Entity waypoint,
                        out NativeParallelMultiHashMapIterator<int2> it))
                    continue;

                do
                {
                    if (!transformLookup.TryGetComponent(waypoint, out LocalTransform waypointTransform))
                        continue;

                    float3 toWaypoint = waypointTransform.Position - origin;
                    float  distSq     = math.lengthsq(toWaypoint);
                    if (distSq <= minDistSq || distSq > searchRangeSq || distSq >= bestDistSq)
                        continue;

                    if (math.dot(math.normalizesafe(toWaypoint), awayDir) <= AWAY_DOT_MIN)
                        continue;

                    bestDistSq = distSq;
                    bestPos    = waypointTransform.Position;
                    found      = true;
                }
                while (waypointCells.TryGetNextValue(out waypoint, ref it));
            }
        }

        return found;
    }

    private static void Terminate(
        ref PathRequest             pathRequest,
        ref Movement                movement,
        ref ActionTimer             actionTimer,
        EnabledRefRW<FleeAction>    fleeActionEnabled,
        EnabledRefRW<ActionRequest> actionRequestEnabled,
        EnabledRefRW<PathRequest>   pathRequestEnabled,
        EnabledRefRW<ActionTimer>   actionTimerEnabled)
    {
        AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
        movement.isRunning         = false;
        actionTimer.time           = 0f;
        actionTimerEnabled.ValueRW = false;
        fleeActionEnabled.ValueRW  = false;
        actionRequestEnabled.ValueRW = true;
    }
}
