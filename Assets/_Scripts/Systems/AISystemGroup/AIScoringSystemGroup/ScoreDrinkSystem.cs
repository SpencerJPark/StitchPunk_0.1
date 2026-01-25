using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(ScoreResetSystem))]
public partial struct ScoreDrinkSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreDrinkJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ScoreDrinkJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs, in Awareness awareness, in CanDrink canDrink)
    {
        if (!awareness.hasBar)
            return;

        // Drinking satisfies entertainment and social
        float entertainmentNeed = 1f - needs.entertainment;
        float socialNeed = 1f - needs.social;
        float score = ResponseCurve.Linear((entertainmentNeed + socialNeed) * 0.5f) * 0.6f;

        AIScoreUtil.SetScore(ref scores, ActionType.Drink, score, true);
    }
}