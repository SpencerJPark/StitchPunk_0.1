using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Scores Behaviour buffer entries for each AI entity and writes ActionOptions.
///
/// Supported BehaviourTypes:
///   Wander      — flat low-priority movement option (always available)
///   Chase       — find nearest entity of hostileFaction via FactionRegistry;
///                 score driven by Safety motivation urgency (or flat if None)
///   MeleeAttack — score based on active CombatTarget in melee range
///   Flee        — score based on SelfPreservation urgency when ThreatEntry buffer has entries
///
/// Runs in AIScoringSystemGroup alongside interaction scoring systems.
/// Uses Schedule (not ScheduleParallel) because FactionRegistry is a shared hashmap.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
public partial struct BehaviourScoringSystem : ISystem
{
    private ComponentLookup<LocalTransform>  transformLookup;
    private ComponentLookup<Alive>           aliveLookup;
    private ComponentLookup<CombatTarget>    combatTargetLookup;
    private ComponentLookup<SafetyMotivation>         safetyLookup;
    private ComponentLookup<SelfPreservationMotivation> selfPreservationLookup;
    private BufferLookup<ThreatEntry>        threatBufferLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FactionRegistry>();

        transformLookup           = state.GetComponentLookup<LocalTransform>(true);
        aliveLookup               = state.GetComponentLookup<Alive>(true);
        combatTargetLookup        = state.GetComponentLookup<CombatTarget>(true);
        safetyLookup              = state.GetComponentLookup<SafetyMotivation>(true);
        selfPreservationLookup    = state.GetComponentLookup<SelfPreservationMotivation>(true);
        threatBufferLookup        = state.GetBufferLookup<ThreatEntry>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        aliveLookup.Update(ref state);
        combatTargetLookup.Update(ref state);
        safetyLookup.Update(ref state);
        selfPreservationLookup.Update(ref state);
        threatBufferLookup.Update(ref state);

        FactionRegistry factionRegistry = SystemAPI.GetSingleton<FactionRegistry>();

        state.Dependency = new BehaviourScoringJob
        {
            transformLookup        = transformLookup,
            aliveLookup            = aliveLookup,
            combatTargetLookup     = combatTargetLookup,
            safetyLookup           = safetyLookup,
            selfPreservationLookup = selfPreservationLookup,
            threatBufferLookup     = threatBufferLookup,
            factionEntities        = factionRegistry.entities,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain))]
public partial struct BehaviourScoringJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform>            transformLookup;
    [ReadOnly] public ComponentLookup<Alive>                     aliveLookup;
    [ReadOnly] public ComponentLookup<CombatTarget>              combatTargetLookup;
    [ReadOnly] public ComponentLookup<SafetyMotivation>          safetyLookup;
    [ReadOnly] public ComponentLookup<SelfPreservationMotivation> selfPreservationLookup;
    [ReadOnly] public BufferLookup<ThreatEntry>                  threatBufferLookup;
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity>   factionEntities;

    public void Execute(
        Entity entity,
        ref DynamicBuffer<ActionOption> options,
        in DynamicBuffer<Behaviour> behaviours,
        in LocalTransform transform,
        in Awareness awareness,
        EnabledRefRO<NeedsAction> needsAction)
    {
        if (!needsAction.ValueRO) return;
        if (behaviours.Length == 0) return;

        float3 unitPos = transform.Position;

        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour behaviour = behaviours[i];

            switch (behaviour.behaviourType)
            {
                case BehaviourType.Wander:
                    ScoreWander(ref options);
                    break;

                case BehaviourType.Chase:
                    ScoreChase(entity, unitPos, awareness.range, in behaviour, ref options);
                    break;

                case BehaviourType.MeleeAttack:
                    ScoreMeleeAttack(entity, unitPos, in behaviour, ref options);
                    break;

                case BehaviourType.Flee:
                    ScoreFlee(entity, unitPos, in behaviour, ref options);
                    break;
            }
        }
    }

    // ── WANDER ───────────────────────────────────────────────────────────────
    // Always available, very low priority so any real need outscores it.

    private static void ScoreWander(ref DynamicBuffer<ActionOption> options)
    {
        options.Add(new ActionOption
        {
            score          = 10f,
            category       = ActionCategory.Wander,
            targetEntity   = Entity.Null,
            targetPosition = float3.zero,
        });
    }

    // ── CHASE ─────────────────────────────────────────────────────────────────
    // Finds the nearest alive entity of the hostile faction within awareness range.
    // Score is driven by Safety motivation urgency; entities without a Safety
    // motivation use the behaviour.value directly as a flat score.

    private void ScoreChase(
        Entity self,
        float3 unitPos,
        float range,
        in Behaviour behaviour,
        ref DynamicBuffer<ActionOption> options)
    {
        byte factionKey = (byte)behaviour.hostileFaction;
        if (factionKey == (byte)FactionType.None) return;

        float rangeSq      = range * range;
        float bestDistSq   = float.MaxValue;
        Entity bestTarget  = Entity.Null;
        float3 bestPos     = float3.zero;

        if (!factionEntities.TryGetFirstValue(factionKey, out Entity candidate, out NativeParallelMultiHashMapIterator<byte> iterator))
            return;

        do
        {
            if (candidate == self) continue;
            if (!aliveLookup.HasComponent(candidate) || !aliveLookup.IsComponentEnabled(candidate))
                continue;
            if (!transformLookup.TryGetComponent(candidate, out LocalTransform targetTransform))
                continue;

            float distSq = math.distancesq(unitPos, targetTransform.Position);
            if (distSq > rangeSq || distSq >= bestDistSq) continue;

            bestDistSq  = distSq;
            bestTarget  = candidate;
            bestPos     = targetTransform.Position;

        } while (factionEntities.TryGetNextValue(out candidate, ref iterator));

        if (bestTarget == Entity.Null) return;

        float score = ComputeMotivationScore(self, behaviour);

        options.Add(new ActionOption
        {
            score          = score,
            category       = ActionCategory.Chase,
            targetEntity   = bestTarget,
            targetPosition = bestPos,
        });
    }

    // ── MELEE ATTACK ──────────────────────────────────────────────────────────
    // Scores if the entity has an active CombatTarget that is alive and in range.

    private void ScoreMeleeAttack(
        Entity self,
        float3 unitPos,
        in Behaviour behaviour,
        ref DynamicBuffer<ActionOption> options)
    {
        if (!combatTargetLookup.HasComponent(self)) return;
        if (!combatTargetLookup.IsComponentEnabled(self)) return;

        CombatTarget combatTarget = combatTargetLookup[self];
        Entity target = combatTarget.targetEntity;
        if (target == Entity.Null) return;

        if (!aliveLookup.HasComponent(target) || !aliveLookup.IsComponentEnabled(target)) return;
        if (!transformLookup.TryGetComponent(target, out LocalTransform targetTransform)) return;

        float distSq   = math.distancesq(unitPos, targetTransform.Position);
        float rangeSq  = behaviour.range * behaviour.range;
        if (distSq > rangeSq) return;

        // Attack is high priority when in range — base 80 + safety urgency bonus.
        float safetyUrgency = GetSafetyUrgency(self);
        float score         = 80f + safetyUrgency * 20f;

        options.Add(new ActionOption
        {
            score          = math.clamp(score, 0f, 100f),
            category       = ActionCategory.Attack,
            targetEntity   = target,
            targetPosition = targetTransform.Position,
        });
    }

    // ── FLEE ──────────────────────────────────────────────────────────────────
    // Scores if the ThreatEntry buffer is non-empty.
    // Target position is a point directly away from the average threat source.

    private void ScoreFlee(
        Entity self,
        float3 unitPos,
        in Behaviour behaviour,
        ref DynamicBuffer<ActionOption> options)
    {
        if (!threatBufferLookup.TryGetBuffer(self, out DynamicBuffer<ThreatEntry> threats)) return;
        if (threats.Length == 0) return;

        // Average all threat source positions.
        float3 threatCenter = float3.zero;
        int    validCount   = 0;

        for (int t = 0; t < threats.Length; t++)
        {
            Entity attacker = threats[t].attackerEntity;
            if (!transformLookup.TryGetComponent(attacker, out LocalTransform attackerTransform)) continue;
            threatCenter += attackerTransform.Position;
            validCount++;
        }

        if (validCount == 0) return;

        threatCenter /= validCount;

        // Flee in the opposite direction.
        float3 fleeDir = unitPos - threatCenter;
        if (math.lengthsq(fleeDir) < 0.001f)
            fleeDir = new float3(1f, 0f, 0f);
        fleeDir = math.normalize(fleeDir);

        const float FLEE_DISTANCE = 15f;
        float3 fleePosition = unitPos + fleeDir * FLEE_DISTANCE;

        float score = ComputeMotivationScore(self, behaviour);

        options.Add(new ActionOption
        {
            score          = score,
            category       = ActionCategory.Flee,
            targetEntity   = Entity.Null,
            targetPosition = fleePosition,
        });
    }

    // ── HELPERS ───────────────────────────────────────────────────────────────

    // Compute a [0, 100] score driven by the behaviour's targetMotivation.
    // When targetMotivation is None, returns behaviour.value as a flat score.
    // Safety and SelfPreservation: urgency increases as value drops (more negative = more urgent).
    private float ComputeMotivationScore(Entity self, in Behaviour behaviour)
    {
        int baseValue = behaviour.value;

        switch (behaviour.targetMotivation)
        {
            case MotivationType.Safety:
                float safety = safetyLookup.HasComponent(self)
                    ? safetyLookup[self].value
                    : -100f; // no component = maximum urgency
                // -100 → score 100, 100 → score 0
                return math.clamp((-safety + 100f) * 0.5f * (baseValue * 0.01f), 0f, 100f);

            case MotivationType.SelfPreservation:
                float selfPreserv = selfPreservationLookup.HasComponent(self)
                    ? selfPreservationLookup[self].value
                    : -100f;
                return math.clamp((-selfPreserv + 100f) * 0.5f * (baseValue * 0.01f), 0f, 100f);

            default:
                // No motivation target — treat value as flat score (0–100 range).
                return math.clamp(baseValue, 0f, 100f);
        }
    }

    private float GetSafetyUrgency(Entity self)
    {
        if (!safetyLookup.HasComponent(self)) return 1f; // no component = max urgency
        float safety = safetyLookup[self].value;
        return math.clamp((-safety + 100f) * 0.01f, 0f, 1f);
    }
}
