using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(ScoreResetSystem))]
public partial struct ScoreFleeSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreFleeJob().ScheduleParallel();
    }
}

[BurstCompile]
[WithAll(typeof(BrainLink))]
public partial struct ScoreFleeJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs)
    {
        // Flee becomes urgent when safety is low
        float danger = 1f - needs.safety;
        
        if (danger < 0.3f)
        {
            AIScoreUtil.SetScore(ref scores, ActionType.Flee, 0f, false);
            return;
        }
        
        // Exponential urgency - overrides everything when very scared
        float score = ResponseCurve.Exponential(danger, 2f) * 1.2f;
        
        AIScoreUtil.SetScore(ref scores, ActionType.Flee, score, true);
    }
}