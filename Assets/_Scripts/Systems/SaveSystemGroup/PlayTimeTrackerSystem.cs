using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Accumulates unscaled delta time into PlayTimeTracker each frame.
/// Runs first in SaveSystemGroup so the time is current before SaveSystem snapshots it.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SaveSystemGroup), OrderFirst = true)]
public partial struct PlayTimeTrackerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<GameDataTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        double deltaTime = SystemAPI.Time.DeltaTime;
        SystemAPI.GetSingletonRW<PlayTimeTracker>().ValueRW.totalSeconds += deltaTime;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
