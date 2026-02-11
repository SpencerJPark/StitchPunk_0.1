using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct AIExecutionSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<UnitMover> unitMoverLookup;
    private ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;
    private BufferLookup<WaypointOccupant> occupantLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        unitMoverLookup = state.GetComponentLookup<UnitMover>(false);
        pathQueuedLookup = state.GetComponentLookup<TargetPositionPathQueued>(false);
        occupantLookup = state.GetBufferLookup<WaypointOccupant>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        unitMoverLookup.Update(ref state);
        pathQueuedLookup.Update(ref state);
        occupantLookup.Update(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new AIExecutionJob
        {
            deltaTime = deltaTime,
            transformLookup = transformLookup,
            unitMoverLookup = unitMoverLookup,
            pathQueuedLookup = pathQueuedLookup,
            occupantLookup = occupantLookup
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct AIExecutionJob : IJobEntity
{
    public float deltaTime;

    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<UnitMover> unitMoverLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;
    [NativeDisableParallelForRestriction] public BufferLookup<WaypointOccupant> occupantLookup;

    public void Execute(
        ref CurrentInteraction currentInteraction,
        ref Needs needs,
        ref SelectedAction selectedAction,
        ref ActionLock actionLock,
        in ChosenActionOption chosenOption,
        in BrainLink brainLink,
        Entity entity)
    {
        Entity body = brainLink.body;

        if (!unitMoverLookup.HasComponent(body))
            return;

        if (!transformLookup.TryGetComponent(body, out LocalTransform bodyTransform))
            return;

        float3 bodyPos = bodyTransform.Position;

        // Idle - stop moving
        if (selectedAction.current == ActionType.Idle)
        {
            if (currentInteraction.target != Entity.Null)
                currentInteraction = default;

            StopMovement(body, bodyPos);
            return;
        }

        // Wander - handled by WanderSystem, just clear interaction
        if (selectedAction.current == ActionType.Wander)
        {
            if (currentInteraction.target != Entity.Null)
                currentInteraction = default;
            return;
        }

        // Waypoint action
        if (chosenOption.waypoint == Entity.Null)
            return;

        // Check if this is a NEW interaction (target changed)
        bool isNewTarget = currentInteraction.target != chosenOption.waypoint;

        if (isNewTarget)
        {
            // Setup new interaction
            currentInteraction.target = chosenOption.waypoint;
            currentInteraction.action = chosenOption.actionType;
            currentInteraction.animation = chosenOption.animation;
            currentInteraction.timeRemaining = chosenOption.duration;
            currentInteraction.interactionRange = chosenOption.interactionRange;
            currentInteraction.needModifiers = chosenOption.needModifiers;
            currentInteraction.isInRange = false;

            // Set movement target ONLY when target changes
            SetMovementTarget(body, chosenOption.position);
        }

        // Process current interaction
        float distSq = math.distancesq(bodyPos, chosenOption.position);
        float rangeSq = currentInteraction.interactionRange * currentInteraction.interactionRange;

        if (distSq <= rangeSq)
        {
            // Arrived
            if (!currentInteraction.isInRange)
            {
                currentInteraction.isInRange = true;
                StopMovement(body, bodyPos);

                // Claim occupancy once
                ClaimOccupancy(chosenOption.waypoint, entity);
            }

            // Apply needs
            ApplyNeedModifiers(ref needs, currentInteraction.needModifiers);

            // Countdown
            currentInteraction.timeRemaining -= deltaTime;

            if (currentInteraction.timeRemaining <= 0f)
            {
                currentInteraction = default;
                actionLock.isComplete = true;
            }
        }
        // Not in range yet - just let movement system handle it
        // DON'T keep setting target every frame!
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
        if (!occupantLookup.TryGetBuffer(waypoint, out DynamicBuffer<WaypointOccupant> occupants))
            return;

        for (int i = 0; i < occupants.Length; i++)
        {
            if (occupants[i].brain == brain)
                return;
        }

        occupants.Add(new WaypointOccupant { brain = brain });
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