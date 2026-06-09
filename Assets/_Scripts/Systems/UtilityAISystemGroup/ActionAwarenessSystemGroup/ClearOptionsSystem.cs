using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup), OrderFirst = true)]
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
[WithAll(typeof(UtilityBrain))]
[WithDisabled(typeof(Dead))]
public partial struct ClearOptionsJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<UtilityActions> actions)
    {
        actions.Clear();
    }
}
