using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateBefore(typeof(DeathSystem))]
public partial struct HealRequestSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new HealJob().ScheduleParallel(state.Dependency);
    }
}

// Applies healing to entities with Heal enabled, clamps to max, then disables the component
[BurstCompile]
[WithAll(typeof(HealRequest))]
public partial struct HealJob : IJobEntity
{
    public void Execute(ref Health health, ref HealRequest healRequest, EnabledRefRW<HealRequest> healEnabled)
    {
        health.healthAmount = math.min(health.healthAmount + healRequest.healAmount, health.healthAmountMax);
        healRequest.healAmount = 0;
        healEnabled.ValueRW = false;
    }
}
