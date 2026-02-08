using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(ScoreResetSystem))]
public partial struct ScoreSitSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreSitJob().ScheduleParallel();
    }
}

[BurstCompile]
[WithAll(typeof(BrainLink))]
public partial struct ScoreSitJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs, in Awareness awareness)
    {
        if (!awareness.hasSeat)
        {
            AIScoreUtil.SetScore(ref scores, ActionType.Sit, 0f, false);
            return;
        }

        // Sit when comfort is low and energy is moderate
        float comfortNeed = 1f - needs.comfort;
        float energyFactor = ResponseCurve.Bell(needs.energy, 0.4f, 0.3f);
        
        float score = comfortNeed * energyFactor * 0.4f;
        
        AIScoreUtil.SetScore(ref scores, ActionType.Sit, score, true);
    }
}