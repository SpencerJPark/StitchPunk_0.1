using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup), OrderFirst = true)]
public partial struct ScoreResetSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreResetJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ScoreResetJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores)
    {
        for (int i = 0; i < scores.Length; i++)
        {
            ActionScore score = scores[i];
            score.score = 0f;
            score.isValid = false;
            scores[i] = score;
        }
    }
}