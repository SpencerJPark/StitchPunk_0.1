using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(ScoreResetSystem))]
public partial struct ScoreUseBathroomSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ScoreUseBathroomJob().ScheduleParallel();
    }
}

[BurstCompile]
[WithAll(typeof(BrainLink))]
public partial struct ScoreUseBathroomJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in Needs needs, in Awareness awareness)
    {
        if (!awareness.hasBathroom)
        {
            AIScoreUtil.SetScore(ref scores, ActionType.UseBathroom, 0f, false);
            return;
        }

        // Exponential urgency as bladder fills
        float score = ResponseCurve.Exponential(needs.bladder, 2.5f) * 0.9f;
        
        AIScoreUtil.SetScore(ref scores, ActionType.UseBathroom, score, true);
    }
}