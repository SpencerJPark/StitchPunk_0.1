using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(CrowdLifeSystemGroup), OrderLast = true)]
public partial struct EmoteDecaySystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new EmoteDecayJob
        {
            deltaTime = SystemAPI.Time.DeltaTime
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct EmoteDecayJob : IJobEntity
{
    public float deltaTime;

    public void Execute(ref EmoteState emoteState)
    {
        if (emoteState.icon == EmoteIcon.None)
            return;

        emoteState.remaining -= deltaTime;
        if (emoteState.remaining <= 0f)
            emoteState = default;
    }
}
