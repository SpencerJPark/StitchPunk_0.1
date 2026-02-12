using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Picks the best action from the options buffer using weighted random selection.
/// 
/// KEY BEHAVIORS:
/// 1. Only runs when the NPC is unlocked and decision timer has expired, or action is complete.
/// 2. When an action completes: saves current waypoint → previousWaypoint, clears current, unlocks.
/// 3. Excludes previousWaypoint from selection to prevent ping-pong.
/// 4. Uses weighted random from top 3 scored options for variety.
/// 5. Locks the NPC onto the chosen waypoint action until it completes.
/// </summary>
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
        ref ActionLock selectedAction,
        ref ChosenActionOption chosenOption,
        in DynamicBuffer<ActionOption> options,
        [EntityIndexInQuery] int index)
    {
        // -------------------------------------------------------
        // STEP 1: Handle completed actions
        // Save current waypoint as previous BEFORE clearing
        // -------------------------------------------------------
        if (selectedAction.isComplete)
        {
            // Save the waypoint we just finished so we don't immediately go back
            if (chosenOption.waypoint != Entity.Null)
            {
                chosenOption.previousWaypoint = chosenOption.waypoint;
            }

            // Clear the current waypoint and unlock
            chosenOption.waypoint = Entity.Null;
            selectedAction.lockedAction = ActionType.None;
            selectedAction.isComplete = false;
            selectedAction.timer = 0f;
            selectedAction.stuckTimer = 0f;
            // Reset decision timer so we pick a new action immediately
            selectedAction.decisionTimer = 0f;
        }

        // -------------------------------------------------------
        // STEP 2: If still locked and not complete, do nothing
        // -------------------------------------------------------
        if (selectedAction.lockedAction != ActionType.None)
            return;

        // -------------------------------------------------------
        // STEP 3: If unlocked but decision timer hasn't expired, wait
        // -------------------------------------------------------
        if (selectedAction.decisionTimer > 0f)
            return;

        // -------------------------------------------------------
        // STEP 4: Decision timer expired - reset it and pick a new action
        // -------------------------------------------------------
        selectedAction.decisionTimer = selectedAction.decisionInterval;

        if (options.Length == 0)
        {
            selected.previous = selected.current;
            selected.current = ActionType.Idle;
            return;
        }

        // -------------------------------------------------------
        // STEP 5: Weighted random selection from top 3, excluding previous waypoint
        // -------------------------------------------------------
        Entity excludeWaypoint = chosenOption.previousWaypoint;
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(seed + (uint)index + 1);

        int best1 = -1;
        int best2 = -1;
        int best3 = -1;
        float score1 = 0f;
        float score2 = 0f;
        float score3 = 0f;

        for (int i = 0; i < options.Length; i++)
        {
            Entity optionWaypoint = options[i].interactableEntity;

            // Skip if this is the waypoint we just came from
            if (optionWaypoint != Entity.Null && optionWaypoint == excludeWaypoint)
                continue;

            // Add slight randomness to score for variety
            float scoredValue = options[i].score * random.NextFloat(0.9f, 1.1f);

            if (scoredValue > score1)
            {
                score3 = score2;
                best3 = best2;
                score2 = score1;
                best2 = best1;
                score1 = scoredValue;
                best1 = i;
            }
            else if (scoredValue > score2)
            {
                score3 = score2;
                best3 = best2;
                score2 = scoredValue;
                best2 = i;
            }
            else if (scoredValue > score3)
            {
                score3 = scoredValue;
                best3 = i;
            }
        }

        // If nothing survived the exclusion filter, allow any option
        if (best1 < 0)
        {
            for (int i = 0; i < options.Length; i++)
            {
                float scoredValue = options[i].score;
                if (scoredValue > score1)
                {
                    score1 = scoredValue;
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

        // Weighted random from the top candidates
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

        // -------------------------------------------------------
        // STEP 6: Commit to the chosen action
        // -------------------------------------------------------
        selected.previous = selected.current;
        selected.current = chosen.actionType;

        chosenOption.waypoint = chosen.interactableEntity;
        chosenOption.actionType = chosen.actionType;
        chosenOption.animation = chosen.animation;
        chosenOption.duration = chosen.duration;
        chosenOption.needModifiers = chosen.needModifiers;
        chosenOption.position = chosen.position;
        chosenOption.interactionRange = chosen.interactionRange;

        // Lock onto waypoint actions so the NPC stays committed
        if (chosen.interactableEntity != Entity.Null)
        {
            selectedAction.lockedAction = chosen.actionType;
            selectedAction.isComplete = false;
            selectedAction.timer = 0f;
            selectedAction.stuckTimer = 0f;
        }
    }
}