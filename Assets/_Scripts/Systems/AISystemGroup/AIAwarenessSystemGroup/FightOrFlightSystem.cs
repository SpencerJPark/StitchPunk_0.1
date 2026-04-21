using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

/// <summary>
/// Pre-pass: when a unit has attacker data in its ThreatEntry buffer, decides
/// whether to fight or flee based on health and available behaviours.
///
/// Fight path  (SelfDefence behaviour present, health > FIGHT_THRESHOLD):
///   Boosts SelfDefence contextMultiplier, sets CombatTarget to the top attacker,
///   and injects an Attack ActionOption naming the aggressor as the target entity.
///
/// Flight path (SelfPreservation or Safety behaviour present, health < FLEE_THRESHOLD):
///   Boosts their contextMultipliers so BehaviourScoringSystem scores flee highly.
///
/// When health falls between the two thresholds both paths activate; the scoring
/// system's output determines which action wins.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(MotivationDecaySystem))]
public partial struct FightOrFlightSystem : ISystem
{
    private ComponentLookup<CombatTarget>    combatTargetLookup;
    private ComponentLookup<AggressiveState> aggressiveLookup;
    private ComponentLookup<Dead>            deadLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();

        combatTargetLookup = state.GetComponentLookup<CombatTarget>(false);
        aggressiveLookup   = state.GetComponentLookup<AggressiveState>(false);
        deadLookup         = state.GetComponentLookup<Dead>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        combatTargetLookup.Update(ref state);
        aggressiveLookup.Update(ref state);
        deadLookup.Update(ref state);

        state.Dependency = new FightOrFlightJob
        {
            combatTargetLookup = combatTargetLookup,
            aggressiveLookup   = aggressiveLookup,
            deadLookup         = deadLookup,
            deltaTime          = SystemAPI.Time.DeltaTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain))]
[WithDisabled(typeof(Dead))]
[WithPresent(typeof(CombatTarget), typeof(AggressiveState))]
public partial struct FightOrFlightJob : IJobEntity
{
    private const float FIGHT_THRESHOLD   = 0.4f;   // health ratio above which fight activates
    private const float FLEE_THRESHOLD    = 0.6f;   // health ratio below which flee activates
    private const float FIGHT_MULTIPLIER  = 3.0f;
    private const float FLEE_MULTIPLIER   = 2.5f;
    private const float FIGHT_SCORE       = 750f;
    private const float AGGRESSIVE_LINGER = 3.0f;

    [NativeDisableParallelForRestriction]
    public ComponentLookup<CombatTarget>    combatTargetLookup;
    [NativeDisableParallelForRestriction]
    public ComponentLookup<AggressiveState> aggressiveLookup;
    [ReadOnly]
    public ComponentLookup<Dead>            deadLookup;
    public float                            deltaTime;

    public void Execute(
        Entity                          self,
        in Health                       health,
        in AttackData                   attackData,
        ref DynamicBuffer<Motivation>    behaviours,
        ref DynamicBuffer<ActionOption> options,
        ref DynamicBuffer<ThreatEntry>  threats,
        EnabledRefRO<NeedsAction>       needsAction)
    {
        if (threats.Length == 0)
            return;

        if (health.healthAmountMax <= 0)
            return;

        float healthRatio = (float)health.healthAmount / health.healthAmountMax;

        Entity topAggressor = Entity.Null;
        float  topThreat    = 0f;
        for (int i = 0; i < threats.Length; i++)
        {
            ThreatEntry threat = threats[i];

            if (threat.reactionDelay > 0f)
            {
                threat.reactionDelay -= deltaTime;
                threats[i]            = threat;
                continue;
            }

            if (threat.threatScore <= topThreat)
                continue;

            if (deadLookup.HasComponent(threat.attackerEntity) &&
                deadLookup.IsComponentEnabled(threat.attackerEntity))
                continue;

            topThreat    = threat.threatScore;
            topAggressor = threat.attackerEntity;
        }

        if (topAggressor == Entity.Null)
            return;

        bool canFight = healthRatio > FIGHT_THRESHOLD;
        bool canFlee  = healthRatio < FLEE_THRESHOLD;

        bool fightActivated = false;

        for (int i = 0; i < behaviours.Length; i++)
        {
            Motivation b = behaviours[i];

            if (b.motivationType == MotivationType.SelfDefence && canFight)
            {
                b.contextMultiplier = FIGHT_MULTIPLIER;
                behaviours[i]       = b;
                fightActivated      = true;
            }

            if ((b.motivationType == MotivationType.SelfPreservation ||
                 b.motivationType == MotivationType.Safety) && canFlee)
            {
                b.contextMultiplier = FLEE_MULTIPLIER;
                behaviours[i]       = b;
            }
        }

        if (!fightActivated || !needsAction.ValueRO)
            return;

        options.Add(new ActionOption
        {
            utilityScore        = FIGHT_SCORE,
            category     = ActionCategory.Attack,
            targetEntity = topAggressor,
        });

        if (combatTargetLookup.HasComponent(self))
        {
            CombatTarget ct = combatTargetLookup[self];
            ct.targetEntity   = topAggressor;
            ct.selectedAttack = attackData.attackType;
            combatTargetLookup[self] = ct;
            combatTargetLookup.SetComponentEnabled(self, true);
        }

        if (aggressiveLookup.HasComponent(self))
        {
            AggressiveState agg = aggressiveLookup[self];
            agg.duration = AGGRESSIVE_LINGER;
            aggressiveLookup[self] = agg;
            aggressiveLookup.SetComponentEnabled(self, true);
        }
    }
}
