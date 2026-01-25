using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(ScoreResetSystem))]
public partial struct ScoreEatSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreEatJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ScoreEatJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs, in Awareness awareness, in CanEat canEat)
    {
        if (!awareness.hasFood)
            return;

        // Exponential curve - urgency increases dramatically as hunger rises
        float score = ResponseCurve.Exponential(needs.hunger, 2f);

        AIScoreUtil.SetScore(ref scores, ActionType.Eat, score, true);
    }
}