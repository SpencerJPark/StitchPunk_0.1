using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(ScoreResetSystem))]
public partial struct ScoreSocializeSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreSocializeJob().ScheduleParallel();
    }
}

[BurstCompile]
[WithAll(typeof(CanSocialize))]
public partial struct ScoreSocializeJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs)
    {
        float socialNeed = 1f - needs.social;
        float score = math.max(0.05f, ResponseCurve.Exponential(socialNeed, 1.5f) * 0.4f);
        
        AIScoreUtil.SetScore(ref scores, ActionType.Socialize, score, true);
    }
}