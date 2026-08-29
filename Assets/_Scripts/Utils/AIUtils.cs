using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public static class AIUtils
{
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


    public static DamageSource GetAttackByAction(ref UnitDataBlob unitBlob, ActionType actionType)
    {
        for (int i = 0; i < unitBlob.attacks.Length; i++)
        {
            if (unitBlob.attacks[i].action == actionType)
                return unitBlob.attacks[i].attack;
        }
        return DamageSource.None;
    }

    public static ActionType GetActionByAttack(ref UnitDataBlob unitBlob, DamageSource damageSource)
    {
        for (int i = 0; i < unitBlob.attacks.Length; i++)
        {
            if (unitBlob.attacks[i].attack == damageSource)
                return unitBlob.attacks[i].action;
        }
        return ActionType.Idle;
    }

    // Directional for free (DirectionFacing_System.md §5): callers resolve clipFacing once via
    // FacingResolver.ResolveClipFacing(unitFacing.current, unitBlob.animationDirections, ...) and
    // pass it through — this stays a plain slot lookup, mirroring
    // UnitAnimationAssignmentJob.GetAnimationForAction.
    public static ClipId GetAnimationByAction(ref UnitDataBlob unitBlob, ActionType actionType, Direction clipFacing)
    {
        ref BlobArray<ActionAnimationMappingBlob> mappings = ref unitBlob.actionAnimations;
        for (int i = 0; i < mappings.Length; i++)
        {
            if (mappings[i].action == actionType)
                return mappings[i].animation.GetSlot(clipFacing);
        }
        return default;
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
