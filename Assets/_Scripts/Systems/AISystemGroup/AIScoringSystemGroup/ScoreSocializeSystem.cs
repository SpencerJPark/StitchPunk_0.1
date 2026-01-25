using Unity.Burst;
using Unity.Entities;

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
public partial struct ScoreSocializeJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs, in CanSocialize canSocialize)
    {
        // Social need drives this - no location requirement for now
        float score = ResponseCurve.Exponential(1f - needs.social, 1.5f) * 0.5f;

        AIScoreUtil.SetScore(ref scores, ActionType.Socialize, score, true);
    }
}