using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public static class AIUtil
{
    public static void AddActionOption(ref DynamicBuffer<ActionOption> options, ref Entity interactionEntity, float score)
    {
        if (interactionEntity != Entity.Null)
        {
            options.Add(new ActionOption
            {
                interactableEntity = interactionEntity,
                score = score
            });
        }
    }

    public static float EvaluateScoringCurve(
        ref BlobAssetReference<AIScoringLibraryBlob> library,
        MotivationType motivationType,
        float needValue)
    {
        ref AIScoringLibraryBlob blob = ref library.Value;

        for (int i = 0; i < blob.curves.Length; i++)
        {
            if (blob.curves[i].motivationType == motivationType)
                return blob.curves[i].curve.Evaluate(needValue);
        }

        return -needValue;
    }

    /// <summary>
    /// Query nearby interactions of a SPECIFIC motivation type.
    /// Only returns interactions that have the specified motivation component.
    /// </summary>
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

    /// <summary>
    /// Original query for ALL nearby interactions (kept for backwards compatibility).
    /// </summary>
    public static void QueryNearbyInteractions(
        in NativeParallelMultiHashMap<int2, Entity> waypointCells,
        in ComponentLookup<InteractionProvider> interactionLookup,
        in ComponentLookup<LocalTransform> transformLookup,
        float3 position,
        float range,
        float cellSize,
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
                if (!waypointCells.TryGetFirstValue(new int2(x, y), out Entity candidate, out var iterator))
                    continue;

                do
                {
                    if (!interactionLookup.HasComponent(candidate))
                        continue;

                    if (!transformLookup.TryGetComponent(candidate, out LocalTransform targetTransform))
                        continue;

                    float distSq = math.distancesq(position, targetTransform.Position);

                    if (distSq > rangeSq)
                        continue;

                    results.Add(candidate);

                } while (waypointCells.TryGetNextValue(out candidate, ref iterator));
            }
        }
    }

    // -------------------------------------------------------
    // SHARED EXECUTION HELPERS
    // -------------------------------------------------------

    public static void SelectWinners(
        DynamicBuffer<InteractionOccupant> occupants,
        int maxOccupants,
        ref ComponentLookup<NeedsAction> needsActionLookup)
    {
        // Sort occupants by score descending (simple selection sort)
        for (int i = 0; i < occupants.Length - 1; i++)
        {
            int bestIndex = i;
            float bestScore = occupants[i].score;

            for (int j = i + 1; j < occupants.Length; j++)
            {
                if (occupants[j].score > bestScore)
                {
                    bestScore = occupants[j].score;
                    bestIndex = j;
                }
            }

            if (bestIndex != i)
            {
                InteractionOccupant temp = occupants[i];
                occupants[i] = occupants[bestIndex];
                occupants[bestIndex] = temp;
            }
        }

        // Reject everyone beyond maxOccupants
        for (int i = occupants.Length - 1; i >= maxOccupants; i--)
        {
            needsActionLookup.SetComponentEnabled(occupants[i].entity, true);
            occupants.RemoveAt(i);
        }
    }

    public static void AssignWinners(
        in DynamicBuffer<InteractionOccupant> occupants,
        float3 interactionPosition,
        ActionType actionType,
        ref ComponentLookup<BrainLink> brainLinkLookup,
        ref ComponentLookup<TargetPositionPathQueued> targetPositionLookup,
        ref ComponentLookup<UnitAction> unitActionLookup)
    {
        for (int i = 0; i < occupants.Length; i++)
        {
            Entity brainEntity = occupants[i].entity;

            if (!brainLinkLookup.TryGetComponent(brainEntity, out BrainLink brainLink))
                continue;

            Entity body = brainLink.body;

            if (!targetPositionLookup.HasComponent(body))
                continue;

            if (!unitActionLookup.HasComponent(body))
                continue;

            targetPositionLookup[body] = new TargetPositionPathQueued
            {
                targetPosition = interactionPosition
            };
            targetPositionLookup.SetComponentEnabled(body, true);

            unitActionLookup[body] = new UnitAction
            {
                current = actionType
            };
        }
    }

    public static bool CheckArrival(
        in DynamicBuffer<InteractionOccupant> occupants,
        in LocalTransform interactionTransform,
        float interactionRange,
        ref ComponentLookup<BrainLink> brainLinkLookup,
        ref ComponentLookup<LocalTransform> transformLookup)
    {
        if (occupants.Length == 0)
            return false;

        Entity brainEntity = occupants[0].entity;

        if (!brainLinkLookup.TryGetComponent(brainEntity, out BrainLink brainLink))
            return false;

        if (!transformLookup.TryGetComponent(brainLink.body, out LocalTransform bodyTransform))
            return false;

        float distSq = math.distancesq(bodyTransform.Position, interactionTransform.Position);
        float rangeSq = interactionRange * interactionRange;

        return distSq <= rangeSq;
    }

    public static void ReleaseOccupants(
        DynamicBuffer<InteractionOccupant> occupants,
        ref ComponentLookup<NeedsAction> needsActionLookup,
        ref ComponentLookup<UnitAction> unitActionLookup,
        ref ComponentLookup<BrainLink> brainLinkLookup)
    {
        for (int i = 0; i < occupants.Length; i++)
        {
            Entity brainEntity = occupants[i].entity;

            if (brainLinkLookup.TryGetComponent(brainEntity, out BrainLink brainLink))
            {
                if (unitActionLookup.HasComponent(brainLink.body))
                {
                    unitActionLookup[brainLink.body] = new UnitAction
                    {
                        current = ActionType.Idle
                    };
                }
            }

            needsActionLookup.SetComponentEnabled(brainEntity, true);
        }

        occupants.Clear();
    }

    public static float ScoreInteraction(
        Entity candidate,
        float3 pos,
        float needValue,
        float awarenessRange,
        MotivationType motivationType,
        ref BlobAssetReference<AIScoringLibraryBlob> scoringLibrary,
        ref ComponentLookup<LocalTransform> transformLookup)
    {
        float3 targetPos = transformLookup[candidate].Position;
        float distance = math.distance(pos, targetPos);

        float baseScore = EvaluateScoringCurve(ref scoringLibrary, motivationType, needValue);
        float distanceBonus = math.remap(0f, awarenessRange, 10f, 0f, distance);

        return math.clamp(baseScore + distanceBonus, -100f, 100f);
    }
}