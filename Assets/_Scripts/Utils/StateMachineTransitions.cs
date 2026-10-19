using Unity.Entities;
using Unity.Mathematics;

// Every write that starts, queues, swaps or ends a behaviour goes through here (AF-D13), so a new
// StateMachine field is reset in one place instead of five.
public static class StateMachineTransitions
{
    public static void StartBehavior(
        ref StateMachine stateMachine,
        ActionType       action,
        BehaviorType     behavior,
        Entity           targetEntity,
        float3           targetPosition,
        bool             hasTargetPosition,
        int              priority)
    {
        stateMachine.action            = action;
        stateMachine.activeBehavior    = behavior;
        stateMachine.targetEntity      = targetEntity;
        stateMachine.targetPosition    = targetPosition;
        stateMachine.hasTargetPosition = hasTargetPosition;
        stateMachine.activePriority    = priority;
        RestartCommandSequence(ref stateMachine);
    }

    // BehaviorInterruptSystem performs the swap later the same frame, after running the live behaviour's cleanup.
    public static void SetPending(
        ref StateMachine stateMachine,
        ActionType       action,
        BehaviorType     behavior,
        Entity           targetEntity,
        float3           targetPosition,
        bool             hasTargetPosition,
        int              priority)
    {
        stateMachine.pendingAction            = action;
        stateMachine.pendingBehavior          = behavior;
        stateMachine.pendingTarget            = targetEntity;
        stateMachine.pendingTargetPosition    = targetPosition;
        stateMachine.pendingHasTargetPosition = hasTargetPosition;
        stateMachine.pendingPriority          = priority;
    }

    public static void ClearPending(ref StateMachine stateMachine)
    {
        stateMachine.pendingAction            = ActionType.Idle;
        stateMachine.pendingBehavior          = BehaviorType.None;
        stateMachine.pendingTarget            = Entity.Null;
        stateMachine.pendingTargetPosition    = default;
        stateMachine.pendingHasTargetPosition = false;
        stateMachine.pendingPriority          = 0;
    }

    public static void PromotePendingToActive(ref StateMachine stateMachine)
    {
        stateMachine.action            = stateMachine.pendingAction;
        stateMachine.activeBehavior    = stateMachine.pendingBehavior;
        stateMachine.targetEntity      = stateMachine.pendingTarget;
        stateMachine.targetPosition    = stateMachine.pendingTargetPosition;
        stateMachine.hasTargetPosition = stateMachine.pendingHasTargetPosition;
        stateMachine.activePriority    = stateMachine.pendingPriority;
        ClearPending(ref stateMachine);
        RestartCommandSequence(ref stateMachine);
        // A swapped-in behaviour re-sets stance via its own Approach/FleeFromTarget on its first tick.
        stateMachine.currentStance = StanceType.Normal;
    }

    // Leaves the pending fields alone; callers that also cancel a queued preemption call ClearPending.
    public static void ResetToIdle(ref StateMachine stateMachine)
    {
        stateMachine.action            = ActionType.Idle;
        stateMachine.activeBehavior    = BehaviorType.None;
        stateMachine.targetEntity      = Entity.Null;
        stateMachine.targetPosition    = default;
        stateMachine.hasTargetPosition = false;
        stateMachine.activePriority    = 0;
        stateMachine.currentStance     = StanceType.Normal;
        RestartCommandSequence(ref stateMachine);
    }

    private static void RestartCommandSequence(ref StateMachine stateMachine)
    {
        stateMachine.currentPhase        = BehaviorPhase.Execute;
        stateMachine.CurrentCommandIndex = 0;
        stateMachine.CommandTimer        = 0f;
        stateMachine.LoopTimer           = 0f;
        stateMachine.LoopIterations      = 0;
    }
}
