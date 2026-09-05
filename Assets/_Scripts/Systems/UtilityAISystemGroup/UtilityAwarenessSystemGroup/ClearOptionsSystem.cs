using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup), OrderFirst = true)]
public partial struct ClearOptionsSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ClearOptionsJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
// WithPresent (not WithAll): the UtilityActions buffer must be cleared each frame for player minions
// too (UtilityBrain disabled) — otherwise their player/self-defence options would accumulate.
[WithPresent(typeof(UtilityBrain))]
[WithDisabled(typeof(Dead))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct ClearOptionsJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<UtilityActions> actions)
    {
        actions.Clear();
    }
}
