using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

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
[WithAll(typeof(BrainLink))]
public partial struct ScoreIdleJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs)
    {
        // Idle is more appealing when comfort is low (need to rest)
        float comfortNeed = 1f - needs.comfort;
        float score = math.max(0.05f, comfortNeed * 0.3f);
        
        AIScoreUtil.SetScore(ref scores, ActionType.Idle, score, true);
    }
}