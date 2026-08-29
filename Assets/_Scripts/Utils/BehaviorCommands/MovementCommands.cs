using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Approach / FleeFromTarget — the two blocking movement commands. Both own their advancement
// (CurrentCommandIndex/CommandTimer only change on arrival), so BehaviorExecutionSystem's dispatch
// just calls in and returns without a generic post-switch advance.
[BurstCompile]
public static class MovementCommands
{
    public static void RunApproach(
        ref BehaviorCommandContext context,
        ref StateMachine           stateMachine,
        ref PathRequest            pathRequest,
        EnabledRefRW<PathRequest>  pathRequestEnabled,
        in LocalTransform          transform,
        float                      stoppingDist,
        int                        stanceIntParam)
    {
        // Raw position target (player move orders): no entity to track — path once and wait
        // for arrival. The moving-target block below degrades safely (lookups miss on Null).
        if (stateMachine.targetEntity == Entity.Null && stateMachine.hasTargetPosition)
        {
            if (!pathRequestEnabled.ValueRO)
            {
                stateMachine.currentStance = (StanceType)stanceIntParam;
                MovementAPI.BeginPathRequest(ref pathRequest, pathRequestEnabled,
                    stateMachine.targetPosition, stoppingDist);
            }
        }
        else if (stateMachine.targetEntity == Entity.Null)
        {
            stateMachine.currentPhase = BehaviorPhase.Complete;
            return;
        }
        else if (!pathRequestEnabled.ValueRO)
        {
            // Dead target — give up immediately rather than pathing to a corpse.
            if (context.deadLookup.TryGetComponent(stateMachine.targetEntity, out Dead dead)
                && context.deadLookup.IsComponentEnabled(stateMachine.targetEntity))
            {
                stateMachine.currentPhase = BehaviorPhase.Complete;
                return;
            }

            if (!context.transformLookup.TryGetComponent(stateMachine.targetEntity, out LocalTransform tgt))
            {
                stateMachine.currentPhase = BehaviorPhase.Complete;
                return;
            }

            // Apply radius scatter for waypoint targets so wandering looks natural.
            float3 targetPos = tgt.Position;
            if (context.waypointLookup.TryGetComponent(stateMachine.targetEntity, out NavigationWaypoint waypoint)
                && waypoint.radius > 0f)
            {
                Unity.Mathematics.Random rng = new Unity.Mathematics.Random(
                    (uint)(context.entityIndex + 1) * (uint)(context.timestamp * 1000f + 1f));
                float angle = rng.NextFloat(0f, math.PI * 2f);
                float dist  = rng.NextFloat(0f, waypoint.radius);
                targetPos  += new float3(math.cos(angle) * dist, 0f, math.sin(angle) * dist);
            }

            stateMachine.currentStance = (StanceType)stanceIntParam;
            MovementAPI.BeginPathRequest(ref pathRequest, pathRequestEnabled, targetPos, stoppingDist);
        }

        // Moving targets (units): re-path when the target drifts from the pathed point, and
        // measure ARRIVAL against the live target — the pathed point is stale by up to the
        // repath threshold, and checking against it deadlocks when both units stand close but
        // the stale point sits just out of stopping range (neither arrival nor re-path fires).
        const float REPATH_THRESHOLD_SQ = 1f;
        float3 arrivalPoint = pathRequest.targetPosition;
        if (!context.waypointLookup.HasComponent(stateMachine.targetEntity)
            && context.transformLookup.TryGetComponent(stateMachine.targetEntity, out LocalTransform livePos))
        {
            arrivalPoint = livePos.Position;

            if (pathRequestEnabled.ValueRO
                && math.distancesq(livePos.Position, pathRequest.targetPosition) > REPATH_THRESHOLD_SQ)
            {
                MovementAPI.BeginPathRequest(ref pathRequest, pathRequestEnabled, livePos.Position, stoppingDist);
            }
        }

        float arrivalSq = stoppingDist * stoppingDist;
        if (math.distancesq(transform.Position, arrivalPoint) <= arrivalSq)
        {
            MovementAPI.HaltPathing(ref pathRequest, pathRequestEnabled);
            stateMachine.CurrentCommandIndex++;
            stateMachine.CommandTimer = 0f;
        }
    }

    // Scans nearby waypoints and paths at Running speed toward the one farthest from the aggressor.
    // stateMachine.targetEntity must be the aggressor. On first frame: picks waypoint and starts path.
    // On subsequent frames: waits for arrival. On arrival: replaces targetEntity with the waypoint
    // (so the job's PushRecent logs the destination, not the aggressor) and advances.
    // centerCell: InteractionSpatialHashSystem.GetCell(transform.Position), computed by the caller —
    // that system lives in the Systems assembly, which Utils cannot reference.
    public static void RunFlee(
        ref BehaviorCommandContext        context,
        ref StateMachine                  stateMachine,
        ref PathRequest                   pathRequest,
        EnabledRefRW<PathRequest>         pathRequestEnabled,
        in LocalTransform                 transform,
        int2                               centerCell,
        ref DynamicBuffer<RecentWaypoint> recentWaypoints)
    {
        if (!pathRequestEnabled.ValueRO)
        {
            // Pick waypoint farthest from aggressor.
            float3 unitPos      = transform.Position;
            float3 aggressorPos = unitPos; // fallback if aggressor has no transform
            if (stateMachine.targetEntity != Entity.Null)
                context.transformLookup.TryGetComponent(stateMachine.targetEntity, out LocalTransform aggressorXf);

            // Reread after TryGet — use a local variable to avoid the ref issue.
            if (stateMachine.targetEntity != Entity.Null
                && context.transformLookup.TryGetComponent(stateMachine.targetEntity, out LocalTransform agXf))
                aggressorPos = agXf.Position;

            int    cellRange  = 2; // 2 × 20m cells = 40m search radius

            Entity bestWaypoint  = Entity.Null;
            float  bestDistSq    = float.MinValue;

            for (int x = -cellRange; x <= cellRange; x++)
            {
                for (int z = -cellRange; z <= cellRange; z++)
                {
                    int2 cell = centerCell + new int2(x, z);
                    if (!context.waypointCells.TryGetFirstValue(cell, out Entity waypoint,
                            out NativeParallelMultiHashMapIterator<int2> it))
                        continue;
                    do
                    {
                        if (IsRecent(waypoint, recentWaypoints)) continue;
                        if (!context.transformLookup.TryGetComponent(waypoint, out LocalTransform wpXf)) continue;

                        // Score = distance from aggressor; higher = safer.
                        float distSq = math.distancesq(aggressorPos, wpXf.Position);
                        if (distSq <= bestDistSq) continue;
                        bestDistSq  = distSq;
                        bestWaypoint = waypoint;
                    }
                    while (context.waypointCells.TryGetNextValue(out waypoint, ref it));
                }
            }

            if (bestWaypoint == Entity.Null
                || !context.transformLookup.TryGetComponent(bestWaypoint, out LocalTransform dest))
            {
                // No valid flee waypoint found — give up and let scoring re-evaluate.
                stateMachine.currentPhase = BehaviorPhase.Complete;
                return;
            }

            stateMachine.currentStance = StanceType.Running;
            // Swap aggressor for chosen waypoint so the job's PushRecent logs the destination on complete.
            stateMachine.targetEntity  = bestWaypoint;
            MovementAPI.BeginPathRequest(ref pathRequest, pathRequestEnabled, dest.Position, 0.5f);
        }

        // Blocking: wait until arrival.
        const float ARRIVE_SQ = 0.25f;
        if (math.distancesq(transform.Position, pathRequest.targetPosition) <= ARRIVE_SQ)
        {
            MovementAPI.HaltPathing(ref pathRequest, pathRequestEnabled);
            stateMachine.CurrentCommandIndex++;
            stateMachine.CommandTimer = 0f;
        }
    }

    private static bool IsRecent(Entity entity, in DynamicBuffer<RecentWaypoint> recent)
    {
        for (int i = 0; i < recent.Length; i++)
            if (recent[i].entity == entity) return true;
        return false;
    }
}
