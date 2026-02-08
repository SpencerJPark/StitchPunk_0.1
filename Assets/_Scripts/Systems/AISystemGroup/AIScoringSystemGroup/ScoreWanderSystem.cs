using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(ScoreResetSystem))]
public partial struct ScoreWanderSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreWanderJob().ScheduleParallel();
    }
}

[BurstCompile]
[WithAll(typeof(CanWander))]
public partial struct ScoreWanderJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs)
    {
        float energyFactor = ResponseCurve.Bell(needs.energy, 0.5f, 0.4f);
        float hungerPenalty = needs.hunger * 0.3f;
        
        float score = math.max(0.1f, energyFactor * (1f - hungerPenalty) * 0.3f);
        
        AIScoreUtil.SetScore(ref scores, ActionType.Wander, score, true);
    }
}