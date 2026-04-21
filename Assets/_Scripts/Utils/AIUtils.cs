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
    public static void AddActionOption(ref DynamicBuffer<ActionOption> options, ref Entity targetEntity, float score,
        MotivationType motivationType = MotivationType.None)
    {
        if (targetEntity != Entity.Null)
        {
            options.Add(new ActionOption
            {
                utilityScore         = score,
                category      = ActionCategory.Interaction,
                motivationType = motivationType,
                targetEntity  = targetEntity,
            });
        }
    }

    public static void AddActionOption(ref DynamicBuffer<ActionOption> options, float score, ActionCategory category, Entity targetEntity, float3 targetPosition)
    {
        options.Add(new ActionOption
        {
            utilityScore          = score,
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
    //
    // Also writes CurrentInteraction on each winning unit and enables the
    // kind-specific task component so per-kind execution systems can pick up
    // the work via their own [WithAll(typeof(KindTask))] queries. When kind
    // is None the universal InteractionExecutionSystem handles the flow instead.
    public static void AssignWinners(
        Entity interactionEntity,
        in DynamicBuffer<InteractionOccupant> occupants,
        ActionType actionType,
        InteractionKind kind,
        ref ComponentLookup<UnitAction> unitActionLookup,
        ref ComponentLookup<CurrentInteraction> currentInteractionLookup,
        ref ComponentLookup<PickupInteraction> pickupLookup,
        ref ComponentLookup<BuildInteraction> buildLookup,
        ref ComponentLookup<DrinkInteraction> drinkLookup,
        ref ComponentLookup<EatInteraction> eatLookup,
        ref ComponentLookup<TouchInteraction> touchAnimateLookup,
        ref ComponentLookup<WanderInteraction> wanderAreaLookup,
        ref ComponentLookup<SitInteraction> sitLookup,
        ref ComponentLookup<RepairInteraction> repairLookup)
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

            if (currentInteractionLookup.HasComponent(unitEntity))
            {
                currentInteractionLookup[unitEntity] = new CurrentInteraction
                {
                    interaction      = interactionEntity,
                    drivingMotivation = occupants[i].motivationType,
                };
            }

            EnableKindTask(
                unitEntity, kind,
                ref pickupLookup, ref buildLookup, ref drinkLookup, ref eatLookup,
                ref touchAnimateLookup, ref wanderAreaLookup, ref sitLookup, ref repairLookup);
        }
    }

    private static void EnableKindTask(
        Entity unitEntity,
        InteractionKind kind,
        ref ComponentLookup<PickupInteraction> pickupLookup,
        ref ComponentLookup<BuildInteraction> buildLookup,
        ref ComponentLookup<DrinkInteraction> drinkLookup,
        ref ComponentLookup<EatInteraction> eatLookup,
        ref ComponentLookup<TouchInteraction> touchAnimateLookup,
        ref ComponentLookup<WanderInteraction> wanderAreaLookup,
        ref ComponentLookup<SitInteraction> sitLookup,
        ref ComponentLookup<RepairInteraction> repairLookup)
    {
        switch (kind)
        {
            case InteractionKind.Pickup:
                if (pickupLookup.HasComponent(unitEntity))
                    pickupLookup.SetComponentEnabled(unitEntity, true);
                break;
            case InteractionKind.Build:
                if (buildLookup.HasComponent(unitEntity))
                    buildLookup.SetComponentEnabled(unitEntity, true);
                break;
            case InteractionKind.Drink:
                if (drinkLookup.HasComponent(unitEntity))
                    drinkLookup.SetComponentEnabled(unitEntity, true);
                break;
            case InteractionKind.Eat:
                if (eatLookup.HasComponent(unitEntity))
                    eatLookup.SetComponentEnabled(unitEntity, true);
                break;
            case InteractionKind.TouchAnimate:
                if (touchAnimateLookup.HasComponent(unitEntity))
                    touchAnimateLookup.SetComponentEnabled(unitEntity, true);
                break;
            case InteractionKind.WanderArea:
                if (wanderAreaLookup.HasComponent(unitEntity))
                    wanderAreaLookup.SetComponentEnabled(unitEntity, true);
                break;
            case InteractionKind.Sit:
                if (sitLookup.HasComponent(unitEntity))
                    sitLookup.SetComponentEnabled(unitEntity, true);
                break;
            case InteractionKind.Repair:
                if (repairLookup.HasComponent(unitEntity))
                    repairLookup.SetComponentEnabled(unitEntity, true);
                break;
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
