using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(ScoreResetSystem))]
public partial struct ScoreIdleSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreIdleJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ScoreIdleJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores)
    {
        // Idle is always valid but low priority
        AIScoreUtil.SetScore(ref scores, ActionType.Idle, 0.1f, true);
    }
}