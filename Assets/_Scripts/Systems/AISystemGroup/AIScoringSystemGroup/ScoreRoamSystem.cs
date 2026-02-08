using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

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
[WithAll(typeof(CanRoam))]
public partial struct ScoreRoamJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs)
    {
        float entertainmentNeed = 1f - needs.entertainment;
        float energyFactor = ResponseCurve.SmoothStep(needs.energy, 0.1f, 0.5f);
        
        float score = math.max(0.08f, entertainmentNeed * energyFactor * 0.25f);
        
        AIScoreUtil.SetScore(ref scores, ActionType.Roam, score, true);
    }
}