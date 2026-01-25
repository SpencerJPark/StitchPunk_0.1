using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(ScoreResetSystem))]
public partial struct ScoreWorkSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreWorkJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ScoreWorkJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs, in Awareness awareness, in CanWork canWork)
    {
        if (!awareness.hasWork)
            return;

        // Work when needs are satisfied - bell curve peaks when needs are moderate
        float needsSatisfaction = (needs.energy + (1f - needs.hunger)) * 0.5f;
        float score = ResponseCurve.Bell(needsSatisfaction, 0.7f, 0.3f) * 0.6f;

        AIScoreUtil.SetScore(ref scores, ActionType.Work, score, true);
    }
}