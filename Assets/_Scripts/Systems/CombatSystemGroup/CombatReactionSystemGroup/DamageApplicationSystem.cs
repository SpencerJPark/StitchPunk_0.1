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
        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.Health) != 0;

        EntityCommandBuffer.ParallelWriter ecb = loggingEnabled
            ? SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
                .AsParallelWriter()
            : default;

        state.Dependency = new DamageApplicationJob
        {
            ecb            = ecb,
            loggingEnabled = loggingEnabled,
            timestamp      = SystemAPI.Time.ElapsedTime,
        }.ScheduleParallel(state.Dependency);
    }
}

// Drains each entity's Hurt buffer, sums incoming damage, applies it to Health
[BurstCompile]
[WithDisabled(typeof(Dead))]
public partial struct DamageApplicationJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter ecb;
    public bool   loggingEnabled;
    public double timestamp;

    public void Execute(
        Entity                       entity,
        [EntityIndexInQuery] int     entityIndex,
        ref Health                   health,
        ref DynamicBuffer<Hurt>      hurtBuffer,
        EnabledRefRW<Dead>           deadEnabled)
    {
        if (hurtBuffer.Length == 0)
            return;

        int totalDamage = 0;
        for (int i = 0; i < hurtBuffer.Length; i++)
            totalDamage += hurtBuffer[i].damageAmount;

        // Capture the killing blow data before clearing — used by Ragdoll2DInitSystem
        // after the buffer is gone.
        Hurt killingBlow        = hurtBuffer[hurtBuffer.Length - 1];
        health.killSourceX      = killingBlow.hitSourceX;
        health.killRagdollForce = killingBlow.ragdollForce;
        health.killLaunchForceY = killingBlow.launchForceY;
        health.killLaunchForceX = killingBlow.launchForceX;
        health.killAttackType   = killingBlow.attackType;
        hurtBuffer.Clear();

        int healthBefore = health.healthAmount;
        health.healthAmount -= totalDamage;

        if (loggingEnabled)
            LogUtil.Log(ref ecb, entityIndex,
                $"[Damage] Entity {entity.Index} took {totalDamage} dmg. HP: {healthBefore}->{health.healthAmount}",
                LogLevel.Info, timestamp, category: LogCategory.Health);

        if (health.healthAmount <= 0)
        {
            deadEnabled.ValueRW = true;
            if (loggingEnabled)
                LogUtil.Log(ref ecb, entityIndex,
                    $"[Damage] Entity {entity.Index} killed.",
                    LogLevel.Warning, timestamp, category: LogCategory.Health);
        }
    }
}
