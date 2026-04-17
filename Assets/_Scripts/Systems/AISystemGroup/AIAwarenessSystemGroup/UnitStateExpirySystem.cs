using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Ticks duration on all active behavioral state components.
/// When duration reaches 0 the component is disabled.
///
/// Runs before CombatAwarenessSystem so that:
///   - Expired states are cleared first
///   - CombatAwarenessSystem can then re-enable AggressiveState (refreshing its duration)
///     for units that still have hostiles in range
///
/// duration == -1 means permanent — never expires.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateBefore(typeof(CombatAwarenessSystem))]
public partial struct UnitStateExpirySystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new AggressiveStateExpiryJob { deltaTime = deltaTime }.ScheduleParallel(state.Dependency);
        state.Dependency = new DrunkStateExpiryJob      { deltaTime = deltaTime }.ScheduleParallel(state.Dependency);
        state.Dependency = new StunnedStateExpiryJob    { deltaTime = deltaTime }.ScheduleParallel(state.Dependency);
        state.Dependency = new PanickedStateExpiryJob   { deltaTime = deltaTime }.ScheduleParallel(state.Dependency);
        state.Dependency = new BerserkStateExpiryJob    { deltaTime = deltaTime }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AggressiveState))]
public partial struct AggressiveStateExpiryJob : IJobEntity
{
    public float deltaTime;
    public void Execute(ref AggressiveState state, EnabledRefRW<AggressiveState> enabled)
    {
        if (state.duration < 0f) return;
        state.duration -= deltaTime;
        if (state.duration <= 0f) enabled.ValueRW = false;
    }
}

[BurstCompile]
[WithAll(typeof(DrunkState))]
public partial struct DrunkStateExpiryJob : IJobEntity
{
    public float deltaTime;
    public void Execute(ref DrunkState state, EnabledRefRW<DrunkState> enabled)
    {
        if (state.duration < 0f) return;
        state.duration -= deltaTime;
        if (state.duration <= 0f) enabled.ValueRW = false;
    }
}

[BurstCompile]
[WithAll(typeof(StunnedState))]
public partial struct StunnedStateExpiryJob : IJobEntity
{
    public float deltaTime;
    public void Execute(ref StunnedState state, EnabledRefRW<StunnedState> enabled)
    {
        if (state.duration < 0f) return;
        state.duration -= deltaTime;
        if (state.duration <= 0f) enabled.ValueRW = false;
    }
}

[BurstCompile]
[WithAll(typeof(PanickedState))]
public partial struct PanickedStateExpiryJob : IJobEntity
{
    public float deltaTime;
    public void Execute(ref PanickedState state, EnabledRefRW<PanickedState> enabled)
    {
        if (state.duration < 0f) return;
        state.duration -= deltaTime;
        if (state.duration <= 0f) enabled.ValueRW = false;
    }
}

[BurstCompile]
[WithAll(typeof(BerserkState))]
public partial struct BerserkStateExpiryJob : IJobEntity
{
    public float deltaTime;
    public void Execute(ref BerserkState state, EnabledRefRW<BerserkState> enabled)
    {
        if (state.duration < 0f) return;
        state.duration -= deltaTime;
        if (state.duration <= 0f) enabled.ValueRW = false;
    }
}
