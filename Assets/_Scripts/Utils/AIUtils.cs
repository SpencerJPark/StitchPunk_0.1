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
        NeedType needType,
        float needValue)
    {
        if (!library.IsCreated) return 0f;

        ref var blob = ref library.Value;

        for (int i = 0; i < blob.curves.Length; i++)
        {
            if (blob.curves[i].needType == needType)
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
        in ComponentLookup<Interaction> interactionLookup,
        in ComponentLookup<LocalTransform> transformLookup,
        float3 position,
        float range,
        float cellSize,
        NeedType needType,
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
                var key = new SpatialInteractionKey(new int2(x, y), needType);

                if (!interactionCells.TryGetFirstValue(key, out Entity candidate, out var iterator))
                    continue;

                do
                {
                    if (!interactionLookup.IsComponentEnabled(candidate))
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
    
    public static bool IsTargetDead(Entity hostile, ComponentLookup<Dead> deadLookup)
    {
        return hostile != Entity.Null &&
               deadLookup.HasComponent(hostile) && 
               deadLookup.IsComponentEnabled(hostile);
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
        float                      stoppingDistance = 0f,
        PathfindingMode            mode             = PathfindingMode.DStarLite)
    {
        pathRequest.targetPosition   = targetPosition;
        pathRequest.requestedMode    = mode;
        pathRequest.stoppingDistance = stoppingDistance;
        pathRequestEnabled.ValueRW   = true;
    }

    
    public static void HaltPathing(
        ref PathRequest pathRequest,
        EnabledRefRW<PathRequest> pathRequestEnabled)
    {
        pathRequest.requestedMode  = PathfindingMode.Stop;
        pathRequestEnabled.ValueRW = true;
    }

    public static AttackType GetAttackByAction(ref UnitDataBlob unitBlob, ActionType actionType)
    {
        for (int i = 0; i < unitBlob.attacks.Length; i++)
        {
            if (unitBlob.attacks[i].action == actionType)
                return unitBlob.attacks[i].attack;
        }
        return AttackType.None;
    }

    public static ActionType GetActionByAttack(ref UnitDataBlob unitBlob, AttackType attackType)
    {
        for (int i = 0; i < unitBlob.attacks.Length; i++)
        {
            if (unitBlob.attacks[i].attack == attackType)
                return unitBlob.attacks[i].action;
        }
        return ActionType.Idle;
    }

    public static AnimationType GetAnimationByAction(ref UnitDataBlob unitBlob, ActionType actionType)
    {
        ref BlobArray<ActionAnimationMappingBlob> mappings = ref unitBlob.actionAnimations;
        for (int i = 0; i < mappings.Length; i++)
        {
            if (mappings[i].action == actionType)
                return mappings[i].animation;
        }
        return AnimationType.None;
    }

    public static void SetMotivationValue(
        ref DynamicBuffer<Motivation> motivations,
        NeedType type,
        float value)
    {
        for (int i = 0; i < motivations.Length; i++)
        {
            Motivation motivation = motivations[i];
            if (motivation.needType == type)
            {
                motivation.value = value;
                motivations[i]   = motivation;
                return;
            }
        }
    }

    public static float AttackRangeScore(float dist, float attackRange)
    {
        return dist <= attackRange ? 1.0f : attackRange / dist;
    }
}
