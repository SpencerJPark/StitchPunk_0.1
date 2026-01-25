using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(ScoreResetSystem))]
public partial struct ScoreSleepSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreSleepJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ScoreSleepJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs, in Awareness awareness, in CanSleep canSleep)
    {
        if (!awareness.hasBed)
            return;

        // Inverse - low energy means high desire to sleep
        float score = ResponseCurve.Exponential(1f - needs.energy, 2f);

        AIScoreUtil.SetScore(ref scores, ActionType.Sleep, score, true);
    }
}