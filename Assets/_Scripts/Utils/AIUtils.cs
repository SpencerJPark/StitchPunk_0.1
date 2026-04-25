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
    
    public static bool IsTargetAlive(Entity hostile, ComponentLookup<Alive> aliveLookup)
    {
        return hostile != Entity.Null &&
               aliveLookup.HasComponent(hostile) && 
               aliveLookup.IsComponentEnabled(hostile);
    }
    
    public static bool IsTargetOutOfRange(
        LocalTransform myTransform, 
        LocalTransform targetTransform, 
        float range)
    {
        float distSq = math.distancesq(myTransform.Position, targetTransform.Position);
        float rangeSq = range * range;
        
        return distSq > rangeSq;
    }
    
    public static bool IsTargetInRange(
        LocalTransform myTransform,
        LocalTransform targetTransform,
        float range)
    {
        float distSq = math.distancesq(myTransform.Position, targetTransform.Position);
       
        if (distSq <= range * range)
        {
            return true;
        }
        return false;
    }


    public static void BeginPathRequest(
        ref PathRequest            pathRequest,
        EnabledRefRW<PathRequest>  pathRequestEnabled,
        float3                     targetPosition,
        PathfindingMode            mode     = PathfindingMode.DStarLite)
    {
        pathRequest.targetPosition = targetPosition;
        pathRequest.requestedMode  = mode;
        pathRequestEnabled.ValueRW = true;
    }


    
    public static void HaltPathing(
        ref PathRequest pathRequest,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        LocalTransform localTransform)
    {
        pathRequest.targetPosition = localTransform.Position;
        pathRequest.requestedMode  = PathfindingMode.None;
        pathRequestEnabled.ValueRW = true;
    }
}
