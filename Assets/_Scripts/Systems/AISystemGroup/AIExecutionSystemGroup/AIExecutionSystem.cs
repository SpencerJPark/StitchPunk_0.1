using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Executes the chosen action: moves toward waypoint, performs action on arrival.
///
/// When an NPC arrives at a waypoint:
///   1. Clears all previously active enable components
///   2. Enables the matching ActiveXxx component for the action type
///   3. Enables the matching behavior component (ActiveWanderArea, etc.)
///   4. Downstream systems with [WithAll(typeof(ActiveEat))] only iterate relevant NPCs
///
/// When the action completes:
///   1. Disables all active components
///   2. Releases occupancy
///   3. Signals actionLock.isComplete
///
/// NOTE: This system uses NativeDisableParallelForRestriction on the enable helper lookups
/// because each NPC writes to its own entity's components. The occupant buffer writes
/// target different entities (waypoints) so we keep parallel restriction disabled there too.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct AIExecutionSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;
    private BufferLookup<InteractionOccupant> occupantLookup;
    private ActionEnableHelper actionEnableHelper;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        pathQueuedLookup = state.GetComponentLookup<TargetPositionPathQueued>(false);
        occupantLookup = state.GetBufferLookup<InteractionOccupant>(false);
        actionEnableHelper = ActionEnableHelper.Create(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        pathQueuedLookup.Update(ref state);
        occupantLookup.Update(ref state);
        actionEnableHelper.UpdateLookups(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;
        uint timeSeed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1;

        state.Dependency = new AIExecutionJob
        {
            deltaTime = deltaTime,
            timeSeed = timeSeed,
            transformLookup = transformLookup,
            pathQueuedLookup = pathQueuedLookup,
            occupantLookup = occupantLookup,
            actionEnableHelper = actionEnableHelper
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct AIExecutionJob : IJobEntity
{
    public float deltaTime;
    public uint timeSeed;

    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;
    [NativeDisableParallelForRestriction] public BufferLookup<InteractionOccupant> occupantLookup;
    [NativeDisableParallelForRestriction] public ActionEnableHelper actionEnableHelper;

    public void Execute(
        ref CurrentInteraction currentInteraction,
        ref Needs needs,
        ref SelectedAction selectedAction,
        ref ActionLock selectedAction,
        in ChosenActionOption chosenOption,
        in BrainLink brainLink,
        Entity entity,
        [EntityIndexInQuery] int index)
    {
        Entity body = brainLink.body;

        if (!transformLookup.TryGetComponent(body, out LocalTransform bodyTransform))
            return;

        float3 bodyPos = bodyTransform.Position;

        // -------------------------------------------------------
        // IDLE (innate, no waypoint): stop moving, clear everything
        // -------------------------------------------------------
        if (selectedAction.current == ActionType.Idle && chosenOption.waypoint == Entity.Null)
        {
            if (currentInteraction.target != Entity.Null)
            {
                actionEnableHelper.ClearAllActiveActions(entity);
                currentInteraction = default;
            }

            StopMovement(body, bodyPos);
            return;
        }

        // -------------------------------------------------------
        // WANDER (innate, no waypoint): handled by WanderSystem
        // -------------------------------------------------------
        if (selectedAction.current == ActionType.Wander && chosenOption.waypoint == Entity.Null)
        {
            if (currentInteraction.target != Entity.Null)
            {
                actionEnableHelper.ClearAllActiveActions(entity);
                currentInteraction = default;
            }
            return;
        }

        // -------------------------------------------------------
        // WAYPOINT ACTION
        // -------------------------------------------------------
        if (chosenOption.waypoint == Entity.Null)
            return;

        // Detect if this is a NEW target
        bool isNewTarget = currentInteraction.target != chosenOption.waypoint ||
                           currentInteraction.action != chosenOption.actionType;

        if (isNewTarget)
        {
            // Clear previous action's enable components
            actionEnableHelper.ClearAllActiveActions(entity);

            // Setup new interaction
            currentInteraction.target = chosenOption.waypoint;
            currentInteraction.action = chosenOption.actionType;
            currentInteraction.animation = chosenOption.animation;
            currentInteraction.timeRemaining = chosenOption.duration;
            currentInteraction.interactionRange = chosenOption.interactionRange;
            currentInteraction.needModifiers = chosenOption.needModifiers;
            currentInteraction.isInRange = false;
            currentInteraction.wanderCenter = chosenOption.position;
            currentInteraction.wanderSubTarget = chosenOption.position;
            currentInteraction.wanderSubTargetTimer = 0f;

            // Set movement target ONLY when target changes
            SetMovementTarget(body, chosenOption.position);
        }

        // -------------------------------------------------------
        // Check distance to approach point
        // -------------------------------------------------------
        float distSq = math.distancesq(bodyPos, chosenOption.position);
        float rangeSq = currentInteraction.interactionRange * currentInteraction.interactionRange;

        if (distSq <= rangeSq)
        {
            // -------------------------------------------------------
            // ARRIVED: enable components and start performing
            // -------------------------------------------------------
            if (!currentInteraction.isInRange)
            {
                currentInteraction.isInRange = true;

                // Claim occupancy
                ClaimOccupancy(chosenOption.waypoint, entity);

                // Reset stuck timer since we arrived
                selectedAction.stuckTimer = 0f;

                // ENABLE the action-specific component
                actionEnableHelper.SetActionEnabled(entity, currentInteraction.action, true);


            }
            

            // Apply need modifiers per second
            ApplyNeedModifiers(ref needs, currentInteraction.needModifiers);

            // Countdown
            currentInteraction.timeRemaining -= deltaTime;

            if (currentInteraction.timeRemaining <= 0f)
            {
                // ACTION COMPLETE

                // Disable all enable components
                actionEnableHelper.ClearAllActiveActions(entity);

                // Release occupancy
                ReleaseOccupancy(chosenOption.waypoint, entity);

                // Clear interaction
                currentInteraction = default;

                // Signal completion
                selectedAction.isComplete = true;
            }
        }
        // NOT in range: movement system handles pathfinding
        // Do NOT keep setting target every frame
    }

    private void ExecuteWanderArea(
        ref CurrentInteraction currentInteraction,
        Entity body,
        float3 bodyPos,
        int index)
    {
        currentInteraction.wanderSubTargetTimer -= deltaTime;

        if (currentInteraction.wanderSubTargetTimer <= 0f)
        {
            Unity.Mathematics.Random random = new Unity.Mathematics.Random(
                timeSeed + (uint)index * 7 + (uint)(currentInteraction.timeRemaining * 100));

            float angle = random.NextFloat(0f, math.PI * 2f);
            float radius = random.NextFloat(0f, currentInteraction.wanderRadius);

            float3 offset = new float3(
                math.cos(angle) * radius,
                0f,
                math.sin(angle) * radius
            );

            currentInteraction.wanderSubTarget = currentInteraction.wanderCenter + offset;
            currentInteraction.wanderSubTargetTimer = random.NextFloat(2f, 5f);

            SetMovementTarget(body, currentInteraction.wanderSubTarget);
        }
    }

    private void StopMovement(Entity body, float3 position)
    {
        if (unitMoverLookup.TryGetComponent(body, out UnitMover mover))
        {
            mover.targetPosition = position;
            mover.isMoving = false;
            unitMoverLookup[body] = mover;
        }
    }

    private void SetMovementTarget(Entity body, float3 targetPosition)
    {
        if (pathQueuedLookup.HasComponent(body))
        {
            pathQueuedLookup[body] = new TargetPositionPathQueued { targetPosition = targetPosition };
            pathQueuedLookup.SetComponentEnabled(body, true);
        }
        else if (unitMoverLookup.TryGetComponent(body, out UnitMover mover))
        {
            mover.targetPosition = targetPosition;
            unitMoverLookup[body] = mover;
        }
    }

    private void ClaimOccupancy(Entity waypoint, Entity brain)
    {
        if (!occupantLookup.TryGetBuffer(waypoint, out DynamicBuffer<InteractionOccupant> occupants))
            return;

        for (int i = 0; i < occupants.Length; i++)
        {
            if (occupants[i].brain == brain)
                return;
        }

        occupants.Add(new InteractionOccupant { brain = brain });
    }

    private void ReleaseOccupancy(Entity waypoint, Entity brain)
    {
        if (!occupantLookup.TryGetBuffer(waypoint, out DynamicBuffer<InteractionOccupant> occupants))
            return;

        for (int i = 0; i < occupants.Length; i++)
        {
            if (occupants[i].brain == brain)
            {
                occupants.RemoveAtSwapBack(i);
                return;
            }
        }
    }

    private void ApplyNeedModifiers(ref Needs needs, NeedModifiers mods)
    {
        needs.hunger = math.saturate(needs.hunger + mods.hunger * deltaTime);
        needs.energy = math.saturate(needs.energy + mods.energy * deltaTime);
        needs.entertainment = math.saturate(needs.entertainment + mods.entertainment * deltaTime);
        needs.social = math.saturate(needs.social + mods.social * deltaTime);
        needs.comfort = math.saturate(needs.comfort + mods.comfort * deltaTime);
        needs.bladder = math.saturate(needs.bladder + mods.bladder * deltaTime);
        needs.safety = math.saturate(needs.safety + mods.safety * deltaTime);
        needs.movement = math.saturate(needs.movement + mods.movement * deltaTime);
    }
}