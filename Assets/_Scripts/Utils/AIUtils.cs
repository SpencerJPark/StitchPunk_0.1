using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public static class AIUtils
{

    public static float EvaluateScoringCurve(
        BlobAssetReference<AIScoringLibraryBlob> library,
        MotivationType motivationType,
        float needValue)
    {
        if (!library.IsCreated) return 0f;
        
        ref var blob = ref library.Value;

        for (int i = 0; i < blob.curves.Length; i++)
        {
            if (blob.curves[i].motivationType == motivationType)
            {
                return blob.curves[i].curve.Evaluate(needValue);
            }
        }

        // No curve registered for this motivation — treat as neutral (1.0) so the
        // option's raw utilityScore passes through unchanged rather than being zeroed.
        return 1.0f;
    }


    public static float FastDistanceScore(float3 from, float3 to, float maxRangeSq)
    {
        // Uses (x*x + y*y + z*z), skipping the heavy Square Root step
        float distSq = math.distancesq(from, to);
        return 1.0f - math.saturate(distSq / maxRangeSq);
    }

    public static void QueryNearbyInteractionsByType(
        in NativeParallelMultiHashMap<SpatialInteractionKey, Entity> interactionCells,
        in ComponentLookup<InteractionProvider> interactionProviderLookup,
        in ComponentLookup<LocalTransform> transformLookup,
        float3 position,
        float range,
        float cellSize,
        MotivationType motivationType,
        ref NativeList<Entity> results)
    {
        float rangeSq = range * range;

        int2 minCell = new int2(
            (int)math.floor((position.x - range) / cellSize),
            (int)math.floor((position.z - range) / cellSize));
        int2 maxCell = new int2(
            (int)math.floor((position.x + range) / cellSize),
            (int)math.floor((position.z + range) / cellSize));

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                var key = new SpatialInteractionKey(new int2(x, y), motivationType);

                if (!interactionCells.TryGetFirstValue(key, out Entity candidate, out var iterator))
                    continue;

                do
                {
                    // Check if provider is enabled
                    if (!interactionProviderLookup.IsComponentEnabled(candidate))
                        continue;

                    if (!transformLookup.TryGetComponent(candidate, out LocalTransform targetTransform))
                        continue;

                    float distSq = math.distancesq(position, targetTransform.Position);

                    if (distSq > rangeSq)
                        continue;

                    results.Add(candidate);

                } while (interactionCells.TryGetNextValue(out candidate, ref iterator));
            }
        }
    }

    // Issues a path request and keeps the agent + mover in sync in one shot.
    // Call from inside an IJobEntity.Execute with refs already in hand — no lookups needed.
    // stoppingDistance = 0 uses the coordinator's default arrival distance.
    // Pass PathfindingMode.None to use agent.preferredMode.
    public static void BeginPathRequest(
        ref PathRequest            pathRequest,
        EnabledRefRW<PathRequest>  pathRequestEnabled,
        ref PathfindingAgent       agent,
        ref Movement               movement,
        float3                     targetPosition,
        float                      stoppingDistance = 0f,
        PathfindingMode            modeOverride     = PathfindingMode.None)
    {

        PathfindingMode mode = modeOverride == PathfindingMode.None
            ? agent.preferredMode
            : modeOverride;

        pathRequest.targetPosition = targetPosition;
        pathRequest.requestedMode  = mode;
        pathRequestEnabled.ValueRW = true;

        agent.targetPosition   = targetPosition;
        agent.stoppingDistance = stoppingDistance;
        agent.isActive         = true;
        agent.needsRepath      = true;

        movement.targetPosition = targetPosition;
        
    }

    public static bool CheckInRange(
        LocalTransform transform,
        ref EnabledRefRW<ArrivedAtTarget> arrivedEnabled,
        float3 targetPosition,
        float range)
    {
        float distSq = math.distancesq(transform.Position, targetPosition);
       
        if (distSq <= range * range)
        {
            arrivedEnabled.ValueRW = true;
            return true;
        }
        return false;
    }

    // Immediate halt — use for mid-task interrupts (target lost, command cancelled).
    // For ordinary "arrived at range" stops, let PathfindingCoordinatorSystem halt
    // followers via the stoppingDistance check instead of calling this.
    public static void HaltPathing(
        ref PathfindingAgent               agent,
        EnabledRefRW<PathRequest>          pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower>    dstarFollowerEnabled,
        EnabledRefRW<FlowFieldFollower>    flowFollowerEnabled)
    {
        agent.isActive    = false;
        agent.needsRepath = false;
        pathRequestEnabled.ValueRW   = false;
        dstarFollowerEnabled.ValueRW = false;
        flowFollowerEnabled.ValueRW  = false;
    }
}
