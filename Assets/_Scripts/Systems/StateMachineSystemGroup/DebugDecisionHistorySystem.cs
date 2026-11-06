using DotsMovementToolkit;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(StateMachineSystemGroup), OrderLast = true)]
public partial struct DebugDecisionHistorySystem : ISystem
{
    Entity rememberedUnit;
    ActionType rememberedAction;
    BehaviorType rememberedBehavior;

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out DebugWatchedUnit watched))
        {
            rememberedUnit = Entity.Null;
            return;
        }

        Entity unit = watched.unit;
        if (unit == Entity.Null
            || !state.EntityManager.Exists(unit)
            || !SystemAPI.HasBuffer<DecisionHistoryEntry>(unit)
            || !SystemAPI.HasComponent<StateMachine>(unit))
        {
            rememberedUnit = Entity.Null;
            return;
        }

        StateMachine stateMachine = SystemAPI.GetComponent<StateMachine>(unit);

        if (unit != rememberedUnit)
        {
            rememberedUnit = unit;
            rememberedAction = stateMachine.action;
            rememberedBehavior = stateMachine.activeBehavior;
            return;
        }

        if (stateMachine.action == rememberedAction && stateMachine.activeBehavior == rememberedBehavior)
            return;

        bool wasPlayerOrdered = stateMachine.activePriority == int.MaxValue;
        float winningUtility = 0f;
        if (SystemAPI.HasBuffer<UtilityActions>(unit))
        {
            DynamicBuffer<UtilityActions> options = SystemAPI.GetBuffer<UtilityActions>(unit);
            for (int optionIndex = 0; optionIndex < options.Length; optionIndex++)
            {
                UtilityActions option = options[optionIndex];
                if (option.actionType != stateMachine.action)
                    continue;
                if (option.isPlayerOrdered)
                    wasPlayerOrdered = true;
                if (option.totalUtility > winningUtility)
                    winningUtility = option.totalUtility;
            }
        }

        DecisionCause cause;
        if (wasPlayerOrdered)
            cause = DecisionCause.PlayerOrder;
        else if (stateMachine.action == ActionType.Flee)
            cause = DecisionCause.Fled;
        else if (SystemAPI.HasComponent<MovementStuck>(unit) && SystemAPI.IsComponentEnabled<MovementStuck>(unit))
            cause = DecisionCause.Stuck;
        else if (stateMachine.action == ActionType.Idle && rememberedAction != ActionType.Idle)
            cause = DecisionCause.Interrupted;
        else
            cause = DecisionCause.WinnerChanged;

        DynamicBuffer<DecisionHistoryEntry> history = SystemAPI.GetBuffer<DecisionHistoryEntry>(unit);
        DecisionHistoryRing.Append(ref history, new DecisionHistoryEntry
        {
            worldSeconds = SystemAPI.Time.ElapsedTime,
            fromAction = rememberedAction,
            toAction = stateMachine.action,
            toBehavior = stateMachine.activeBehavior,
            priority = stateMachine.activePriority,
            winningUtility = winningUtility,
            wasPlayerOrdered = wasPlayerOrdered,
            cause = cause,
        });

        rememberedAction = stateMachine.action;
        rememberedBehavior = stateMachine.activeBehavior;
    }
}
