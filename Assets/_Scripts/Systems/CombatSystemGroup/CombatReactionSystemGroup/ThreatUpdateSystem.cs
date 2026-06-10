using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(CombatReactionSystemGroup))]
[UpdateBefore(typeof(DamageApplicationSystem))]
public partial struct ThreatUpdateSystem : ISystem
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
            || (loggingCfg.EnabledCategories & (int)LogCategory.Combat) != 0;

        EntityCommandBuffer.ParallelWriter ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();

        state.Dependency = new ThreatUpdateJob
        {
            ecb            = ecb,
            loggingEnabled = loggingEnabled,
            timestamp      = SystemAPI.Time.ElapsedTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithDisabled(typeof(Dead))]
public partial struct ThreatUpdateJob : IJobEntity
{
    private const float REACTION_TIME = 0.3f;
    private const float THREAT_TTL    = 4f;

    public EntityCommandBuffer.ParallelWriter ecb;
    public bool   loggingEnabled;
    public double timestamp;

    public void Execute(
        Entity                       entity,
        [EntityIndexInQuery] int     entityIndex,
        in DynamicBuffer<Hurt>       hurtBuffer,
        ref DynamicBuffer<ThreatEntry> threatBuffer)
    {
        if (hurtBuffer.Length == 0)
            return;

        for (int hurtIndex = 0; hurtIndex < hurtBuffer.Length; hurtIndex++)
        {
            Hurt hurt = hurtBuffer[hurtIndex];

            if (hurt.attackerEntity == Entity.Null)
                continue;

            bool found = false;
            for (int threatIndex = 0; threatIndex < threatBuffer.Length; threatIndex++)
            {
                if (threatBuffer[threatIndex].attackerEntity == hurt.attackerEntity)
                {
                    ThreatEntry entry = threatBuffer[threatIndex];
                    entry.threatScore += hurt.damageAmount;
                    entry.staleTimer   = THREAT_TTL;
                    threatBuffer[threatIndex] = entry;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                threatBuffer.Add(new ThreatEntry
                {
                    attackerEntity = hurt.attackerEntity,
                    threatScore    = hurt.damageAmount,
                    reactionDelay  = REACTION_TIME,
                    staleTimer     = THREAT_TTL,
                });

                if (loggingEnabled)
                    LogUtil.Log(ref ecb, entityIndex,
                        $"[ThreatUpdate] Entity {entity.Index} gained ThreatEntry for attacker {hurt.attackerEntity.Index} (damage {hurt.damageAmount})",
                        LogLevel.Info, timestamp, category: LogCategory.Combat);
            }
        }
    }
}
