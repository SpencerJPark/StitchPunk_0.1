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
        uint seed = (uint)SystemAPI.Time.ElapsedTime * 1000 + 1;

        new ActionSelectionJob
        {
            baseSeed = seed,
            topNCount = 3
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ActionSelectionJob : IJobEntity
{
    public uint baseSeed;
    public int topNCount;

    public void Execute(ref SelectedAction selected, ref DynamicBuffer<ActionScore> scores, [EntityIndexInQuery] int index)
    {
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(baseSeed + (uint)index + 1);

        // Find top N valid scores
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

        // Weighted random selection from top N
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

        selected.previous = selected.current;
        selected.current = chosen;

        topActions.Dispose();
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