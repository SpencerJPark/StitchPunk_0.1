using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup), OrderFirst = true)]
public partial struct ActionTimerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        state.Dependency = new ActionTimerJob { deltaTime = deltaTime }
            .ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct ActionTimerJob : IJobEntity
{
    public float deltaTime;

    public void Execute(ref ActionTimer actionTimer)
    {
        if (actionTimer.time > 0f)
            actionTimer.time = math.max(0f, actionTimer.time - deltaTime);
    }
}