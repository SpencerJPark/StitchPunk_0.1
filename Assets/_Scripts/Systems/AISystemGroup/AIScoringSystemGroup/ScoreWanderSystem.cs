using Unity.Burst;
using Unity.Entities;

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
public partial struct ScoreWanderJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in CanWander canWander)
    {
        // Low priority fallback - always available
        AIScoreUtil.SetScore(ref scores, ActionType.Wander, 0.2f, true);
    }
}