using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AISelectionSystemGroup))]
public partial struct ActionSelectionSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        uint seed = (uint)(SystemAPI.Time.ElapsedTime * 10000) + 1;

        state.Dependency = new ActionSelectionJob
        {
            deltaTime = deltaTime,
            seed = seed
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct ActionSelectionJob : IJobEntity
{
    public float deltaTime;
    public uint seed;

    public void Execute(
        ref SelectedAction selected,
        ref ActionLock actionLock,
        ref ChosenActionOption chosenOption,
        in DynamicBuffer<ActionOption> options,
        in CurrentInteraction currentInteraction,
        [EntityIndexInQuery] int index)
    {
        // Update decision timer
        actionLock.decisionTimer -= deltaTime;

        // If locked and not complete, keep current
        if (actionLock.decisionTimer > 0f && 
            actionLock.lockedAction != ActionType.None && 
            !actionLock.isComplete)
        {
            return;
        }

        // Clear completed lock
        if (actionLock.isComplete)
        {
            // Only update previousWaypoint if we actually completed an interaction
            // (currentInteraction.target will be null after AIExecutionSystem clears it)
            // So we use chosenOption.waypoint which still has the value
            if (chosenOption.waypoint != Entity.Null)
            {
                chosenOption.previousWaypoint = chosenOption.waypoint;
                chosenOption.waypoint = Entity.Null;  // Clear it here
            }

            actionLock.lockedAction = ActionType.None;
            actionLock.isComplete = false;
            actionLock.timer = 0f;
            actionLock.stuckTimer = 0f;
        }

        // Only decide when timer expires
        if (actionLock.decisionTimer > 0f)
            return;

        actionLock.decisionTimer = actionLock.decisionInterval;

        if (options.Length == 0)
        {
            selected.previous = selected.current;
            selected.current = ActionType.Idle;
            return;
        }

        Entity excludeWaypoint = chosenOption.previousWaypoint;

        // Weighted random from top 3, excluding previous waypoint
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(seed + (uint)index + 1);

        int best1 = -1, best2 = -1, best3 = -1;
        float score1 = 0f, score2 = 0f, score3 = 0f;

        for (int i = 0; i < options.Length; i++)
        {
            Entity optionWaypoint = options[i].waypoint;

            // Skip if this is the previous waypoint
            if (optionWaypoint != Entity.Null && optionWaypoint == excludeWaypoint)
                continue;

            float s = options[i].score * random.NextFloat(0.9f, 1.1f);

            if (s > score1)
            {
                score3 = score2; best3 = best2;
                score2 = score1; best2 = best1;
                score1 = s; best1 = i;
            }
            else if (s > score2)
            {
                score3 = score2; best3 = best2;
                score2 = s; best2 = i;
            }
            else if (s > score3)
            {
                score3 = s; best3 = i;
            }
        }

        // If nothing found after filtering, allow any option (edge case)
        if (best1 < 0)
        {
            for (int i = 0; i < options.Length; i++)
            {
                float s = options[i].score;
                if (s > score1)
                {
                    score1 = s;
                    best1 = i;
                }
            }
        }

        if (best1 < 0)
        {
            selected.previous = selected.current;
            selected.current = ActionType.Idle;
            return;
        }

        float total = score1 + score2 + score3;
        int chosenIdx = best1;

        if (total > 0f)
        {
            float roll = random.NextFloat(0f, total);
            if (roll < score1)
                chosenIdx = best1;
            else if (roll < score1 + score2 && best2 >= 0)
                chosenIdx = best2;
            else if (best3 >= 0)
                chosenIdx = best3;
        }

        ActionOption chosen = options[chosenIdx];

        selected.previous = selected.current;
        selected.current = chosen.actionType;

        chosenOption.waypoint = chosen.waypoint;
        chosenOption.actionType = chosen.actionType;
        chosenOption.animation = chosen.animation;
        chosenOption.duration = chosen.duration;
        chosenOption.needModifiers = chosen.needModifiers;
        chosenOption.position = chosen.position;
        chosenOption.interactionRange = chosen.interactionRange;
        // DON'T touch previousWaypoint here!

        // Lock waypoint actions
        if (chosen.waypoint != Entity.Null)
        {
            actionLock.lockedAction = chosen.actionType;
            actionLock.isComplete = false;
            actionLock.timer = 0f;
            actionLock.stuckTimer = 0f;
        }
    }
}