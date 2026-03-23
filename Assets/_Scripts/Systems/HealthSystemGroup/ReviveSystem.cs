using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
public partial struct ReviveSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ReviveJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithDisabled(typeof(Alive))]
[WithPresent(typeof(Undead))]
public partial struct ReviveJob : IJobEntity
{
    public void Execute(ref Health health, EnabledRefRW<Revive> reviveEnabled, EnabledRefRW<Undead> undeadEnabled, EnabledRefRW<Dead> deadEnabled, EnabledRefRW<Alive> aliveEnabled)
    {
        health.healthAmount = health.healthAmountMax;
        reviveEnabled.ValueRW = false;
        undeadEnabled.ValueRW = true; 
        deadEnabled.ValueRW = false;
        aliveEnabled.ValueRW = true;
    }
}