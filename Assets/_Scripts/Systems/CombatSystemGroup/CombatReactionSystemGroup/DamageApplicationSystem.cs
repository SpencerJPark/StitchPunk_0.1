using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(CombatReactionSystemGroup))]
public partial struct DamageApplicationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new DamageApplicationJob().ScheduleParallel(state.Dependency);
    }
}

// Drains each entity's Hurt buffer, sums incoming damage, applies it to Health
[BurstCompile]
public partial struct DamageApplicationJob : IJobEntity
{
    public void Execute(ref Health health, ref DynamicBuffer<Hurt> hurtBuffer)
    {
        if (hurtBuffer.Length == 0)
            return;

        int totalDamage = 0;
        for (int i = 0; i < hurtBuffer.Length; i++)
            totalDamage += hurtBuffer[i].damageAmount;

        health.healthAmount -= totalDamage;
        hurtBuffer.Clear();
    }
}
