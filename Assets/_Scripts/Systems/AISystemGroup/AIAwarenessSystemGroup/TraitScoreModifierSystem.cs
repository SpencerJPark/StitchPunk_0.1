using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(WaypointQuerySystem))]
public partial struct TraitScoreModifierSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new WorkaholicModifierJob().ScheduleParallel(state.Dependency);
        state.Dependency = new SocialModifierJob().ScheduleParallel(state.Dependency);
        state.Dependency = new LonerModifierJob().ScheduleParallel(state.Dependency);
        state.Dependency = new GluttonModifierJob().ScheduleParallel(state.Dependency);
        state.Dependency = new LazyModifierJob().ScheduleParallel(state.Dependency);
        state.Dependency = new NervousModifierJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct WorkaholicModifierJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionOption> options, in IsWorkaholic trait)
    {
        for (int i = 0; i < options.Length; i++)
        {
            var opt = options[i];
            if (opt.actionType == ActionType.Work)
                opt.score *= 1.5f;
            else if (opt.actionType == ActionType.Idle)
                opt.score *= 0.5f;
            options[i] = opt;
        }
    }
}

[BurstCompile]
public partial struct SocialModifierJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionOption> options, in IsSocial trait)
    {
        for (int i = 0; i < options.Length; i++)
        {
            var opt = options[i];
            if (opt.actionType == ActionType.Socialize)
                opt.score *= 1.5f;
            else if (opt.actionType == ActionType.Drink)
                opt.score *= 1.3f;
            else if (opt.actionType == ActionType.Wander)
                opt.score *= 0.8f;
            options[i] = opt;
        }
    }
}

[BurstCompile]
public partial struct LonerModifierJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionOption> options, in IsLoner trait)
    {
        for (int i = 0; i < options.Length; i++)
        {
            var opt = options[i];
            if (opt.actionType == ActionType.Socialize)
                opt.score *= 0.3f;
            else if (opt.actionType == ActionType.Wander)
                opt.score *= 1.5f;
            else if (opt.actionType == ActionType.Idle)
                opt.score *= 1.3f;
            options[i] = opt;
        }
    }
}

[BurstCompile]
public partial struct GluttonModifierJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionOption> options, in IsGlutton trait)
    {
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i].actionType == ActionType.Eat)
            {
                var opt = options[i];
                opt.score *= 1.5f;
                options[i] = opt;
            }
        }
    }
}

[BurstCompile]
public partial struct LazyModifierJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionOption> options, in IsLazy trait)
    {
        for (int i = 0; i < options.Length; i++)
        {
            var opt = options[i];
            if (opt.actionType == ActionType.Work)
                opt.score *= 0.5f;
            else if (opt.actionType == ActionType.Sleep)
                opt.score *= 1.5f;
            else if (opt.actionType == ActionType.Sit)
                opt.score *= 1.4f;
            else if (opt.actionType == ActionType.Idle)
                opt.score *= 1.3f;
            options[i] = opt;
        }
    }
}

[BurstCompile]
public partial struct NervousModifierJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionOption> options, in IsNervous trait)
    {
        for (int i = 0; i < options.Length; i++)
        {
            var opt = options[i];
            if (opt.actionType == ActionType.Smoke)
                opt.score *= 1.5f;
            else if (opt.actionType == ActionType.Socialize)
                opt.score *= 0.7f;
            options[i] = opt;
        }
    }
}