using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(ScoreResetSystem))]
public partial struct ScoreRoamSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreRoamJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ScoreRoamJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs, in CanRoam canRoam)
    {
        // Roam when entertainment is low - exploring the city is stimulating
        float score = ResponseCurve.Linear(1f - needs.entertainment) * 0.4f;

        AIScoreUtil.SetScore(ref scores, ActionType.Roam, score, true);
    }
}