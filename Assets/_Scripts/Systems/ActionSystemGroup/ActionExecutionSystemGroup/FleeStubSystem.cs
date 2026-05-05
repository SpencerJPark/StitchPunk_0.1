using Unity.Burst;
using Unity.Entities;

// Placeholder until FleeActionSystem is implemented.
// Immediately terminates the flee action so the unit cycles back to selection
// rather than freezing with FleeAction enabled and ActionRequest stuck disabled.
[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup))]
public partial struct FleeStubSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new FleeStubJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(FleeAction))]
[WithDisabled(typeof(ActionRequest), typeof(Dead))]
public partial struct FleeStubJob : IJobEntity
{
    public void Execute(
        EnabledRefRW<FleeAction>     fleeEnabled,
        EnabledRefRW<ActionRequest>  actionRequest)
    {
        fleeEnabled.ValueRW    = false;
        actionRequest.ValueRW  = true;
    }
}
