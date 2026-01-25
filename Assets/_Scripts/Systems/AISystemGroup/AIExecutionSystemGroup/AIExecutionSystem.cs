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
    private ComponentLookup<Interactable> interactableLookup;
    private ComponentLookup<UnitMover> unitMoverLookup;
    private ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;
    private BufferLookup<InteractableAction> interactableActionsLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        interactableLookup = SystemAPI.GetComponentLookup<Interactable>(true);
        unitMoverLookup = SystemAPI.GetComponentLookup<UnitMover>(false);
        pathQueuedLookup = SystemAPI.GetComponentLookup<TargetPositionPathQueued>(false);
        interactableActionsLookup = SystemAPI.GetBufferLookup<InteractableAction>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        interactableLookup.Update(ref state);
        unitMoverLookup.Update(ref state);
        pathQueuedLookup.Update(ref state);
        interactableActionsLookup.Update(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new AIExecutionJob
        {
            deltaTime = deltaTime,
            transformLookup = transformLookup,
            interactableLookup = interactableLookup,
            unitMoverLookup = unitMoverLookup,
            pathQueuedLookup = pathQueuedLookup,
            interactableActionsLookup = interactableActionsLookup
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct AIExecutionJob : IJobEntity
{
    public float deltaTime;

    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Interactable> interactableLookup;
    [ReadOnly] public BufferLookup<InteractableAction> interactableActionsLookup;

    [NativeDisableParallelForRestriction] public ComponentLookup<UnitMover> unitMoverLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;

    public void Execute(
        ref CurrentInteraction currentInteraction,
        ref Needs needs,
        ref SelectedAction selectedAction,
        in BrainLink brainLink,
        in Awareness awareness)
    {
        Entity body = brainLink.body;

        if (!unitMoverLookup.HasComponent(body))
            return;

        if (!transformLookup.TryGetComponent(body, out LocalTransform bodyTransform))
            return;

        // Handle action change
        if (selectedAction.current != selectedAction.previous)
        {
            OnActionChanged(
                ref currentInteraction,
                selectedAction.current,
                body,
                bodyTransform.Position,
                awareness);

            selectedAction.previous = selectedAction.current;
        }

        // Process current interaction
        if (currentInteraction.target != Entity.Null)
        {
            ProcessInteraction(
                ref currentInteraction,
                ref needs,
                ref selectedAction,
                body,
                bodyTransform.Position);
        }
    }

    private void OnActionChanged(
        ref CurrentInteraction currentInteraction,
        ActionType newAction,
        Entity body,
        float3 bodyPosition,
        Awareness awareness)
    {
        currentInteraction = default;

        Entity targetEntity = GetTargetForAction(newAction, awareness);

        if (targetEntity == Entity.Null)
            return;

        if (!interactableLookup.TryGetComponent(targetEntity, out Interactable interactable))
            return;

        // Get approach position
        float3 approachPosition;
        if (interactable.approachPoint != Entity.Null &&
            transformLookup.TryGetComponent(interactable.approachPoint, out LocalTransform approachTransform))
        {
            approachPosition = approachTransform.Position;
        }
        else if (transformLookup.TryGetComponent(targetEntity, out LocalTransform targetTransform))
        {
            approachPosition = targetTransform.Position;
        }
        else
        {
            return;
        }

        // Find matching action data
        if (!interactableActionsLookup.TryGetBuffer(targetEntity, out DynamicBuffer<InteractableAction> actions))
            return;

        InteractableAction matchedAction = default;
        bool foundAction = false;

        for (int i = 0; i < actions.Length; i++)
        {
            if (actions[i].actionType == newAction)
            {
                matchedAction = actions[i];
                foundAction = true;
                break;
            }
        }

        if (!foundAction)
            return;

        // Setup interaction
        currentInteraction.target = targetEntity;
        currentInteraction.action = newAction;
        currentInteraction.animation = matchedAction.animation;
        currentInteraction.timeRemaining = matchedAction.duration;
        currentInteraction.interactionRange = interactable.interactionRange;
        currentInteraction.needAffected = matchedAction.needAffected;
        currentInteraction.needModifier = matchedAction.needModifier;
        currentInteraction.isInRange = false;

        // Queue path to target
        SetMovementTarget(body, approachPosition);
    }

    private void ProcessInteraction(
        ref CurrentInteraction currentInteraction,
        ref Needs needs,
        ref SelectedAction selectedAction,
        Entity body,
        float3 bodyPosition)
    {
        if (!transformLookup.TryGetComponent(currentInteraction.target, out LocalTransform targetTransform))
        {
            // Target no longer exists
            ClearInteraction(ref currentInteraction, ref selectedAction, body, bodyPosition);
            return;
        }

        float distanceSq = math.distancesq(bodyPosition, targetTransform.Position);
        float rangeSq = currentInteraction.interactionRange * currentInteraction.interactionRange;

        if (distanceSq <= rangeSq)
        {
            // In range - perform interaction
            currentInteraction.isInRange = true;

            // Stop movement
            if (unitMoverLookup.TryGetComponent(body, out UnitMover mover))
            {
                mover.targetPosition = bodyPosition;
                unitMoverLookup[body] = mover;
            }

            // Apply need modification over time
            ApplyNeedModification(ref needs, currentInteraction.needAffected, currentInteraction.needModifier);

            // Count down
            currentInteraction.timeRemaining -= deltaTime;

            if (currentInteraction.timeRemaining <= 0f)
            {
                // Interaction complete
                ClearInteraction(ref currentInteraction, ref selectedAction, body, bodyPosition);
            }
        }
        else
        {
            currentInteraction.isInRange = false;
        }
    }

    private void ApplyNeedModification(ref Needs needs, NeedType needType, float modifier)
    {
        float change = modifier * deltaTime;

        switch (needType)
        {
            case NeedType.Hunger:
                needs.hunger = math.saturate(needs.hunger - change);
                break;
            case NeedType.Energy:
                needs.energy = math.saturate(needs.energy + change);
                break;
            case NeedType.Entertainment:
                needs.entertainment = math.saturate(needs.entertainment + change);
                break;
            case NeedType.Social:
                needs.social = math.saturate(needs.social + change);
                break;
        }
    }

    private void ClearInteraction(
        ref CurrentInteraction currentInteraction,
        ref SelectedAction selectedAction,
        Entity body,
        float3 bodyPosition)
    {
        currentInteraction = default;
        selectedAction.current = ActionType.Idle;
        selectedAction.previous = ActionType.Idle;

        if (unitMoverLookup.TryGetComponent(body, out UnitMover mover))
        {
            mover.targetPosition = bodyPosition;
            unitMoverLookup[body] = mover;
        }
    }

    private Entity GetTargetForAction(ActionType action, Awareness awareness)
    {
        switch (action)
        {
            case ActionType.Eat:
                return awareness.nearestFood;
            case ActionType.Sleep:
                return awareness.nearestBed;
            case ActionType.Work:
                return awareness.nearestWork;
            case ActionType.Smoke:
                return awareness.nearestSmokeSpot;
            case ActionType.Drink:
                return awareness.nearestBar;
            case ActionType.SeekEntertainment:
                return awareness.nearestEntertainment;
            default:
                return Entity.Null;
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
}