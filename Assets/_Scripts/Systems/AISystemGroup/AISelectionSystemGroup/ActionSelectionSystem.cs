using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AISelectionSystemGroup))]
public partial struct ActionSelectionSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        uint seed = (uint)(SystemAPI.Time.ElapsedTime * 10000) + 1;

        new ActionSelectionJob
        {
            seed = seed,
            topNCount = 3
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ActionSelectionJob : IJobEntity
{
    public uint seed;
    public int topNCount;

    public void Execute(
        ref SelectedAction selected,
        ref ActionLock actionLock,
        ref DynamicBuffer<ActionScore> scores,
        [EntityIndexInQuery] int index)
    {
        // If action is locked and not complete, keep it
        if (actionLock.lockedAction != ActionType.None && !actionLock.isComplete)
        {
            bool actionStillValid = IsActionValid(ref scores, actionLock.lockedAction);

            if (actionStillValid)
            {
                selected.previous = selected.current;
                selected.current = actionLock.lockedAction;
                return;
            }
            else
            {
                ClearLock(ref actionLock);
            }
        }

        // Action completed or timed out, clear lock
        if (actionLock.isComplete)
        {
            ClearLock(ref actionLock);
        }

        // Select new action
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(seed + (uint)index + 1);

        NativeList<ActionScore> topActions = new NativeList<ActionScore>(topNCount, Allocator.Temp);

        for (int i = 0; i < scores.Length; i++)
        {
            ActionScore score = scores[i];
            if (!score.isValid || score.score <= 0f)
                continue;

            InsertSorted(ref topActions, score);
        }

        if (topActions.Length == 0)
        {
            selected.previous = selected.current;
            selected.current = ActionType.Idle;
            topActions.Dispose();
            return;
        }

        float totalWeight = 0f;
        for (int i = 0; i < topActions.Length; i++)
        {
            totalWeight += topActions[i].score;
        }

        float roll = random.NextFloat(0f, totalWeight);
        float cumulative = 0f;

        ActionType chosen = topActions[0].actionType;
        for (int i = 0; i < topActions.Length; i++)
        {
            cumulative += topActions[i].score;
            if (roll <= cumulative)
            {
                chosen = topActions[i].actionType;
                break;
            }
        }

        topActions.Dispose();

        selected.previous = selected.current;
        selected.current = chosen;

        // Lock actions that need completion
        if (RequiresCompletion(chosen))
        {
            actionLock.lockedAction = chosen;
            actionLock.isComplete = false;
            actionLock.timer = 0f;
            actionLock.stuckTimer = 0f;
        }
    }

    private void ClearLock(ref ActionLock actionLock)
    {
        actionLock.lockedAction = ActionType.None;
        actionLock.isComplete = false;
        actionLock.timer = 0f;
        actionLock.stuckTimer = 0f;
    }

    private bool IsActionValid(ref DynamicBuffer<ActionScore> scores, ActionType action)
    {
        for (int i = 0; i < scores.Length; i++)
        {
            if (scores[i].actionType == action && scores[i].isValid && scores[i].score > 0f)
            {
                return true;
            }
        }
        return false;
    }

    private bool RequiresCompletion(ActionType action)
    {
        return action == ActionType.Roam ||
               action == ActionType.Eat ||
               action == ActionType.Sleep ||
               action == ActionType.Work ||
               action == ActionType.Smoke ||
               action == ActionType.Drink;
    }

    private void InsertSorted(ref NativeList<ActionScore> list, ActionScore newScore)
    {
        int insertIndex = list.Length;
        for (int i = 0; i < list.Length; i++)
        {
            if (newScore.score > list[i].score)
            {
                insertIndex = i;
                break;
            }
        }

        if (insertIndex < topNCount)
        {
            if (list.Length < topNCount)
            {
                list.Add(default);
            }

            for (int i = list.Length - 1; i > insertIndex; i--)
            {
                list[i] = list[i - 1];
            }

            list[insertIndex] = newScore;
        }
    }
}