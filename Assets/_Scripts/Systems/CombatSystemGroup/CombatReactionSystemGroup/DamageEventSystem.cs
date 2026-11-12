using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Drains DamageBus.resolved. DamageResolutionSystem completes its expand job first, so this
// main-thread drain is safe.
// Threat registers only for a hostile attacker source; friendly-fire and sourceless damage never do.
[BurstCompile]
[UpdateInGroup(typeof(CombatReactionSystemGroup))]
public partial struct DamageEventSystem : ISystem
{
    private const float REACTION_TIME = 0.3f;
    private const float THREAT_TTL    = 4f;
    private const float BRAWL_HEALTH_FLOOR_FRACTION = 0.35f;

    private ComponentLookup<Health>       healthLookup;
    private BufferLookup<ThreatEntry>     threatLookup;
    private ComponentLookup<Dead>         deadLookup;
    private ComponentLookup<CutsceneActor> cutsceneActorLookup;
    private ComponentLookup<DodgeInvulnerable> dodgeInvulnerableLookup;
    private ComponentLookup<Faction>      factionLookup;
    private BufferLookup<AttackFaction>   attackFactionLookup;
    private ComponentLookup<StateMachine> stateMachineLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<DamageBus>();

        healthLookup        = state.GetComponentLookup<Health>(false);
        threatLookup        = state.GetBufferLookup<ThreatEntry>(false);
        deadLookup          = state.GetComponentLookup<Dead>(false);
        cutsceneActorLookup = state.GetComponentLookup<CutsceneActor>(true);
        dodgeInvulnerableLookup = state.GetComponentLookup<DodgeInvulnerable>(true);
        factionLookup       = state.GetComponentLookup<Faction>(true);
        attackFactionLookup = state.GetBufferLookup<AttackFaction>(true);
        stateMachineLookup  = state.GetComponentLookup<StateMachine>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        DamageBus bus = SystemAPI.GetSingleton<DamageBus>();

        if (bus.resolved.Count == 0)
            return;

        healthLookup.Update(ref state);
        threatLookup.Update(ref state);
        deadLookup.Update(ref state);
        cutsceneActorLookup.Update(ref state);
        dodgeInvulnerableLookup.Update(ref state);
        factionLookup.Update(ref state);
        attackFactionLookup.Update(ref state);
        stateMachineLookup.Update(ref state);

        bool hasConfig = SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg);
        bool combatLogging = !hasConfig || (loggingCfg.EnabledCategories & (int)LogCategory.Combat) != 0;
        bool healthLogging = !hasConfig || (loggingCfg.EnabledCategories & (int)LogCategory.Health) != 0;
        double timestamp   = SystemAPI.Time.ElapsedTime;

        EntityCommandBuffer ecb = (combatLogging || healthLogging)
            ? SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
            : default;

        int appliedCount = 0;

        while (bus.resolved.TryDequeue(out DamageEvent damageEvent))
        {
            Entity target = damageEvent.targetEntity;

            if (!healthLookup.HasComponent(target))
                continue;

            if (deadLookup.HasComponent(target) && deadLookup.IsComponentEnabled(target))
                continue;

            // A puppeted cutscene actor cannot flee or fight back, so it is immune; an on-camera death
            // must be authored in a narrative action after release.
            bool targetIsPuppetedCutsceneActor = cutsceneActorLookup.HasComponent(target)
                && cutsceneActorLookup.IsComponentEnabled(target);
            if (targetIsPuppetedCutsceneActor)
                continue;

            // Mid-dodge (the player's roll i-frames). Skipped before threat too: a dodged hit never landed.
            if (dodgeInvulnerableLookup.HasComponent(target) && dodgeInvulnerableLookup.IsComponentEnabled(target))
                continue;

            appliedCount++;

            // Faction gate: allies caught in an AOE must not turn on the caster.
            if (SourceHostileToTarget(damageEvent.sourceEntity, target) && threatLookup.HasBuffer(target))
            {
                DynamicBuffer<ThreatEntry> threatBuffer = threatLookup[target];
                bool found = false;
                for (int threatIndex = 0; threatIndex < threatBuffer.Length; threatIndex++)
                {
                    if (threatBuffer[threatIndex].attackerEntity == damageEvent.sourceEntity)
                    {
                        ThreatEntry entry = threatBuffer[threatIndex];
                        entry.threatScore += damageEvent.damageAmount;
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
                        attackerEntity = damageEvent.sourceEntity,
                        threatScore    = damageEvent.damageAmount,
                        reactionDelay  = REACTION_TIME,
                        staleTimer     = THREAT_TTL,
                    });

                    if (combatLogging)
                        LogUtil.Log(ref ecb,
                            $"[ThreatUpdate] Entity {target.Index} gained ThreatEntry for attacker {damageEvent.sourceEntity.Index} (damage {damageEvent.damageAmount})",
                            LogLevel.Info, timestamp, category: LogCategory.Combat);
                }
            }

            Health health = healthLookup[target];
            int healthBefore = health.healthAmount;
            int appliedDamage = ClampBrawlDamageToFloor(damageEvent.sourceEntity, target, health, damageEvent.damageAmount);
            health.healthAmount -= appliedDamage;

            if (healthLogging)
                LogUtil.Log(ref ecb,
                    $"[Damage] Entity {target.Index} took {damageEvent.damageAmount} dmg. HP: {healthBefore}->{health.healthAmount}",
                    LogLevel.Info, timestamp, category: LogCategory.Health);

            if (health.healthAmount <= 0)
            {
                // Captured for RagdollLaunchInitSystem, which reads Health.kill* after death.
                health.killSourcePosition = damageEvent.sourcePosition;
                health.killRagdollForce   = damageEvent.ragdollForce;
                health.killLaunchForceY   = damageEvent.launchForceY;
                health.killLaunchForceX   = damageEvent.launchForceX;
                health.killFlailIntensity = damageEvent.flailIntensity;
                health.killSpin           = damageEvent.spin;
                health.killRestitution    = damageEvent.restitution;
                health.killDamageSource   = damageEvent.damageSource;
                deadLookup.SetComponentEnabled(target, true);

                if (healthLogging)
                    LogUtil.Log(ref ecb,
                        $"[Damage] Entity {target.Index} killed.",
                        LogLevel.Warning, timestamp, category: LogCategory.Health);
            }

            healthLookup[target] = health;
        }

        if (combatLogging && appliedCount > 0)
            LogUtil.Log(ref ecb,
                $"[DamageBus] Applied {appliedCount} damage event(s) this frame.",
                LogLevel.Info, timestamp, category: LogCategory.Combat);
    }

    // Brawls are drunk flavour and must never kill (LH-D14).
    private int ClampBrawlDamageToFloor(Entity source, Entity target, Health health, int damageAmount)
    {
        if (source == Entity.Null || !factionLookup.HasComponent(source) || !factionLookup.HasComponent(target))
            return damageAmount;
        if (factionLookup[source].factionType != factionLookup[target].factionType)
            return damageAmount;
        if (!stateMachineLookup.HasComponent(source) || stateMachineLookup[source].action != ActionType.Brawl)
            return damageAmount;

        int brawlFloor = (int)math.ceil(BRAWL_HEALTH_FLOOR_FRACTION * health.healthAmountMax);
        int allowedDamage = health.healthAmount - brawlFloor;
        return math.clamp(damageAmount, 0, math.max(allowedDamage, 0));
    }

    // Mirrors EnemyAwarenessSystem: hostile means the source's faction is in the target's AttackFaction buffer.
    private bool SourceHostileToTarget(Entity source, Entity target)
    {
        if (source == Entity.Null)
            return false;
        if (!factionLookup.HasComponent(source))
            return false;
        if (!attackFactionLookup.HasBuffer(target))
            return false;

        FactionType sourceFaction = factionLookup[source].factionType;
        DynamicBuffer<AttackFaction> attackFactions = attackFactionLookup[target];
        for (int i = 0; i < attackFactions.Length; i++)
        {
            if (attackFactions[i].faction == sourceFaction)
                return true;
        }
        return false;
    }
}
