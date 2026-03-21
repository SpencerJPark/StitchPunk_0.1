using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
public partial struct DeathSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new DeathJob().ScheduleParallel(state.Dependency);
    }
}

// Runs only on living entities. When health hits zero: disables Alive, enables Dead, sets UnitState.
// Entity is NOT destroyed — corpse persists for animation, revival, or gameplay use.
[BurstCompile]
[WithAll(typeof(Alive))]
public partial struct DeathJob : IJobEntity
{
    public void Execute(
        in Health health,
        ref UnitStateData unitState,
        EnabledRefRW<Alive> aliveEnabled,
        EnabledRefRW<Dead> deadEnabled)
    {
        if (health.healthAmount > 0)
            return;

        unitState.state = UnitState.Dead;
        aliveEnabled.ValueRW = false;
        deadEnabled.ValueRW = true;
    }
}
