using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(ScoreResetSystem))]
public partial struct ScoreSmokeSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreSmokeJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ScoreSmokeJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs, in Awareness awareness, in CanSmoke canSmoke)
    {
        if (!awareness.hasSmokeSpot)
            return;

        // Smoking satisfies entertainment but less effectively
        float score = ResponseCurve.Linear(1f - needs.entertainment) * 0.5f;

        AIScoreUtil.SetScore(ref scores, ActionType.Smoke, score, true);
    }
}