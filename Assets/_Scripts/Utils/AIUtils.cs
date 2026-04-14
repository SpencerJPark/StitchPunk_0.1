using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public static class AIUtils
{
    // Convenience overload used by interaction scoring systems (Hunger, Energy, etc.).
    // Category is always Interaction; targetPosition is resolved by execution systems
    // from the interaction entity's LocalTransform at navigation time.
    public static void AddActionOption(ref DynamicBuffer<ActionOption> options, ref Entity targetEntity, float score)
    {
        if (targetEntity != Entity.Null)
        {
            options.Add(new ActionOption
            {
                score        = score,
                category     = ActionCategory.Interaction,
                targetEntity = targetEntity,
            });
        }
    }

    public static void AddActionOption(ref DynamicBuffer<ActionOption> options, float score, ActionCategory category, Entity targetEntity, float3 targetPosition)
    {
        options.Add(new ActionOption
        {
            score          = score,
            category       = category,
            targetEntity   = targetEntity,
            targetPosition = targetPosition,
        });
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

    // In the single-entity model the occupant entity IS the unit entity —
    // no BodyLink lookup needed. UnitAction lives directly on the occupant.
    public static void AssignWinners(
        in DynamicBuffer<InteractionOccupant> occupants,
        ActionType actionType,
        ref ComponentLookup<UnitAction> unitActionLookup)
    {
        for (int i = 0; i < occupants.Length; i++)
        {
            Entity unitEntity = occupants[i].entity;

            if (unitActionLookup.HasComponent(unitEntity))
            {
                unitActionLookup[unitEntity] = new UnitAction
                {
                    current = actionType
                };
            }
        }
    }

    // Occupant entity IS the unit entity — check its transform directly.
    public static bool CheckArrival(
        in DynamicBuffer<InteractionOccupant> occupants,
        in LocalTransform interactionTransform,
        float interactionRange,
        ref ComponentLookup<LocalTransform> transformLookup)
    {
        if (occupants.Length == 0)
            return false;

        Entity unitEntity = occupants[0].entity;

        if (!transformLookup.TryGetComponent(unitEntity, out LocalTransform unitTransform))
            return false;

        float distSq = math.distancesq(unitTransform.Position, interactionTransform.Position);
        float rangeSq = interactionRange * interactionRange;

        return distSq <= rangeSq;
    }

    // Occupant entity IS the unit entity — apply animation directly.
    public static void StartInteractionAnimation(
        ActionType actionType,
        in DynamicBuffer<InteractionOccupant> occupants,
        ref ComponentLookup<UnitAction> unitActionLookup)
    {
        for (int i = 0; i < occupants.Length; i++)
        {
            Entity unitEntity = occupants[i].entity;

            if (unitActionLookup.HasComponent(unitEntity))
            {
                unitActionLookup[unitEntity] = new UnitAction
                {
                    current = actionType
                };
            }
        }
    }

    /// <summary>
    /// Release occupants and reset their action state.
    /// Dead units are cleared but NeedsAction is NOT re-enabled.
    /// Occupant entity IS the unit entity in the single-entity model.
    /// </summary>
    public static void ReleaseOccupants(
        DynamicBuffer<InteractionOccupant> occupants,
        ref ComponentLookup<NeedsAction> needsActionLookup,
        ref ComponentLookup<UnitAction> unitActionLookup,
        ref ComponentLookup<Dead> deadLookup)
    {
        for (int i = 0; i < occupants.Length; i++)
        {
            Entity unitEntity = occupants[i].entity;

            if (unitActionLookup.HasComponent(unitEntity))
            {
                unitActionLookup[unitEntity] = new UnitAction
                {
                    current = ActionType.Idle
                };
            }

            bool isDead = deadLookup.HasComponent(unitEntity) &&
                          deadLookup.IsComponentEnabled(unitEntity);

            if (!isDead)
                needsActionLookup.SetComponentEnabled(unitEntity, true);
        }

        occupants.Clear();
    }

    /// <summary>
    /// Release occupants with full pathfinding cleanup.
    /// Dead units are cleared but NeedsAction is NOT re-enabled.
    /// Occupant entity IS the unit entity in the single-entity model.
    /// </summary>
    public static void ReleaseOccupants(
        DynamicBuffer<InteractionOccupant> occupants,
        ref ComponentLookup<NeedsAction> needsActionLookup,
        ref ComponentLookup<UnitAction> unitActionLookup,
        ref ComponentLookup<Dead> deadLookup,
        ref ComponentLookup<PathfindingAgent> pathfindingAgentLookup,
        ref ComponentLookup<FlowFieldFollower> flowFieldFollowerLookup,
        ref ComponentLookup<DStarLiteFollower> dstarFollowerLookup)
    {
        for (int i = 0; i < occupants.Length; i++)
        {
            Entity unitEntity = occupants[i].entity;

            bool isDead = deadLookup.HasComponent(unitEntity) &&
                          deadLookup.IsComponentEnabled(unitEntity);

            if (unitActionLookup.HasComponent(unitEntity))
            {
                unitActionLookup[unitEntity] = new UnitAction
                {
                    current = ActionType.Idle
                };
            }

            if (pathfindingAgentLookup.HasComponent(unitEntity))
            {
                PathfindingAgent agent = pathfindingAgentLookup[unitEntity];
                agent.isActive = false;
                pathfindingAgentLookup[unitEntity] = agent;
            }

            if (flowFieldFollowerLookup.HasComponent(unitEntity))
                flowFieldFollowerLookup.SetComponentEnabled(unitEntity, false);

            if (dstarFollowerLookup.HasComponent(unitEntity))
                dstarFollowerLookup.SetComponentEnabled(unitEntity, false);

            if (!isDead)
                needsActionLookup.SetComponentEnabled(unitEntity, true);
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

    /// <summary>
    /// Request a path for an entity using the new pathfinding system.
    /// </summary>
    public static void RequestPath(
        Entity entity,
        float3 targetPosition,
        ref ComponentLookup<PathRequest> pathRequestLookup,
        ref ComponentLookup<PathfindingAgent> pathfindingAgentLookup,
        ref ComponentLookup<Movement> unitMoverLookup)
    {
        if (!pathRequestLookup.HasComponent(entity))
            return;

        PathfindingMode mode = PathfindingMode.DStarLite;
        if (pathfindingAgentLookup.HasComponent(entity))
        {
            mode = pathfindingAgentLookup[entity].preferredMode;

            PathfindingAgent agent = pathfindingAgentLookup[entity];
            agent.targetPosition = targetPosition;
            agent.isActive = true;
            agent.needsRepath = true;
            pathfindingAgentLookup[entity] = agent;
        }

        pathRequestLookup[entity] = new PathRequest
        {
            targetPosition = targetPosition,
            requestedMode = mode
        };
        pathRequestLookup.SetComponentEnabled(entity, true);

        if (unitMoverLookup.HasComponent(entity))
        {
            Movement mover = unitMoverLookup[entity];
            mover.targetPosition = targetPosition;
            unitMoverLookup[entity] = mover;
        }
    }

    /// <summary>
    /// Stop pathfinding for an entity.
    /// </summary>
    public static void StopPathfinding(
        Entity entity,
        ref ComponentLookup<PathfindingAgent> pathfindingAgentLookup,
        ref ComponentLookup<FlowFieldFollower> flowFieldFollowerLookup,
        ref ComponentLookup<DStarLiteFollower> dstarFollowerLookup)
    {
        if (pathfindingAgentLookup.HasComponent(entity))
        {
            PathfindingAgent agent = pathfindingAgentLookup[entity];
            agent.isActive = false;
            pathfindingAgentLookup[entity] = agent;
        }

        if (flowFieldFollowerLookup.HasComponent(entity))
        {
            flowFieldFollowerLookup.SetComponentEnabled(entity, false);
        }

        if (dstarFollowerLookup.HasComponent(entity))
        {
            dstarFollowerLookup.SetComponentEnabled(entity, false);
        }
    }
}
