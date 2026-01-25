using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup), OrderLast = true)]
public partial struct TraitScoreModifierSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new WorkaholicModifierJob().ScheduleParallel();
        new SocialModifierJob().ScheduleParallel();
        new LonerModifierJob().ScheduleParallel();
        new GluttonModifierJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct WorkaholicModifierJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in IsWorkaholic workaholic)
    {
        AIScoreUtil.MultiplyScore(ref scores, ActionType.Work, 1.5f);
    }
}

[BurstCompile]
public partial struct SocialModifierJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in IsSocial social)
    {
        AIScoreUtil.MultiplyScore(ref scores, ActionType.Socialize, 1.5f);
        AIScoreUtil.MultiplyScore(ref scores, ActionType.Drink, 1.3f);
    }
}

[BurstCompile]
public partial struct LonerModifierJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in IsLoner loner)
    {
        AIScoreUtil.MultiplyScore(ref scores, ActionType.Socialize, 0.3f);
        AIScoreUtil.MultiplyScore(ref scores, ActionType.Wander, 1.5f);
    }
}

[BurstCompile]
public partial struct GluttonModifierJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionScore> scores, in IsGlutton glutton)
    {
        AIScoreUtil.MultiplyScore(ref scores, ActionType.Eat, 1.5f);
    }
}