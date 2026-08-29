using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Single combat-reaction consumer (v2) — drains the recycled DamageBus.resolved queue. Producers
// (AttackRequestSystem, ThrownItemHitSystem, HazardZoneSystem) Enqueue into DamageBus.raw;
// DamageResolutionSystem (just before this) expands AOE into single-target events in `resolved` and
// completes that job, so this drain is a safe Burst MAIN-THREAD loop. No entity create/destroy — the
// queue is recycled each frame by DamageBusSystem. Cost is O(hits-that-happened), not O(all units).
//
// Per event: skip already-Dead victims; register threat ONLY when the source is a hostile attacker
// (v2 faction gate — friendly-fire and environmental/sourceless damage never create a ThreatEntry);
// apply damage to Health; on the lethal event capture killing-blow knockback into Health.kill* and
// enable Dead (read later by RagdollLaunchInitSystem).
[BurstCompile]
[UpdateInGroup(typeof(CombatReactionSystemGroup))]
public partial struct DamageEventSystem : ISystem
{
    // Same constants as the retired ThreatUpdateSystem.
    private const float REACTION_TIME = 0.3f;
    private const float THREAT_TTL    = 4f;

    private ComponentLookup<Health>       healthLookup;
    private BufferLookup<ThreatEntry>     threatLookup;
    private ComponentLookup<Dead>         deadLookup;
    private ComponentLookup<Faction>      factionLookup;
    private BufferLookup<AttackFaction>   attackFactionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<DamageBus>();

        healthLookup        = state.GetComponentLookup<Health>(false);
        threatLookup        = state.GetBufferLookup<ThreatEntry>(false);
        deadLookup          = state.GetComponentLookup<Dead>(false);
        factionLookup       = state.GetComponentLookup<Faction>(true);
        attackFactionLookup = state.GetBufferLookup<AttackFaction>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        DamageBus bus = SystemAPI.GetSingleton<DamageBus>();

        // resolved was fully written + its expand job completed by DamageResolutionSystem, so Count
        // and the drain below are safe on the main thread. Empty on frames with no hits.
        if (bus.resolved.Count == 0)
            return;

        healthLookup.Update(ref state);
        threatLookup.Update(ref state);
        deadLookup.Update(ref state);
        factionLookup.Update(ref state);
        attackFactionLookup.Update(ref state);

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

            // Already-dead victim (e.g. killed by an earlier event this frame) — avoid double-kill.
            if (deadLookup.HasComponent(target) && deadLookup.IsComponentEnabled(target))
                continue;

            appliedCount++;

            // Threat — v2 faction gate. Only a hostile attacker source registers: sourceless
            // (environmental) damage and friendly-fire (source not in the victim's AttackFaction
            // list) never create a ThreatEntry, so allies caught in an AOE don't turn on the caster.
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

            // Damage — apply to Health and, on the lethal event, capture killing-blow knockback + die.
            Health health = healthLookup[target];
            int healthBefore = health.healthAmount;
            health.healthAmount -= damageEvent.damageAmount;

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

        // Debug counter — the recycled bus has no inspectable event entities, so surface throughput.
        if (combatLogging && appliedCount > 0)
            LogUtil.Log(ref ecb,
                $"[DamageBus] Applied {appliedCount} damage event(s) this frame.",
                LogLevel.Info, timestamp, category: LogCategory.Combat);
    }

    // v2 hostility test — mirrors EnemyAwarenessSystem: a source is hostile to the target when the
    // source has a Faction whose type appears in the target's AttackFaction buffer. Null / factionless
    // sources (thrown items, hazards) and non-hostile sources (friendly fire) are never hostile.
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
