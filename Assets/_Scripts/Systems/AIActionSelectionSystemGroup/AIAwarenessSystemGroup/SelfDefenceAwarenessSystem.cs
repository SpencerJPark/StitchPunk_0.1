using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Pre-pass: when a unit has attacker data in its ThreatEntry buffer, decides
/// whether to fight or flee based on health and available behaviours.
///
/// Fight path  (SelfDefence behaviour present, health > FIGHT_THRESHOLD):
///   Boosts SelfDefence contextMultiplier, sets CombatTarget to the top attacker,
///   and injects one ActionOption per available attack (from UnitLibraryBlob),
///   scored by range fit vs actual distance to the aggressor.
///
/// Flight path (SelfPreservation or Safety behaviour present, health < FLEE_THRESHOLD):
///   Boosts their contextMultipliers so BehaviourScoringSystem scores flee highly.
///
/// When health falls between the two thresholds both paths activate; the scoring
/// system's output determines which action wins.
///
/// CombatTarget and AggressiveState are always updated when a valid aggressor exists,
/// even in pure-flee mode, so flee systems know who to run from.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(EnemyAwarenessSystem))]
public partial struct SelfDefenceAwarenessSystem : ISystem
{
    private ComponentLookup<CombatTarget>    combatTargetLookup;
    private ComponentLookup<AggressiveState> aggressiveLookup;
    private ComponentLookup<Dead>            deadLookup;
    private ComponentLookup<LocalTransform>  transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<UnitDataLibrary>();

        combatTargetLookup = state.GetComponentLookup<CombatTarget>(false);
        aggressiveLookup   = state.GetComponentLookup<AggressiveState>(false);
        deadLookup         = state.GetComponentLookup<Dead>(true);
        transformLookup    = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        combatTargetLookup.Update(ref state);
        aggressiveLookup.Update(ref state);
        deadLookup.Update(ref state);
        transformLookup.Update(ref state);

        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;

        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        state.Dependency = new FightOrFlightJob
        {
            combatTargetLookup = combatTargetLookup,
            aggressiveLookup   = aggressiveLookup,
            deadLookup         = deadLookup,
            transformLookup    = transformLookup,
            attackLibrary      = attackLibrary,
            unitLibrary        = unitLibrary,
            deltaTime          = SystemAPI.Time.DeltaTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
[WithPresent(typeof(CombatTarget), typeof(AggressiveState))]
public partial struct FightOrFlightJob : IJobEntity
{
    private const float FIGHT_THRESHOLD   = 0.4f;
    private const float FLEE_THRESHOLD    = 0.6f;
    private const float FIGHT_MULTIPLIER  = 3.0f;
    private const float FLEE_MULTIPLIER   = 2.5f;
    private const float AGGRESSIVE_LINGER = 3.0f;

    [NativeDisableParallelForRestriction]
    public ComponentLookup<CombatTarget>    combatTargetLookup;
    [NativeDisableParallelForRestriction]
    public ComponentLookup<AggressiveState> aggressiveLookup;
    [ReadOnly]
    public ComponentLookup<Dead>            deadLookup;
    [ReadOnly]
    public ComponentLookup<LocalTransform>  transformLookup;
    [ReadOnly]
    public BlobAssetReference<AttackLibraryBlob> attackLibrary;
    [ReadOnly]
    public BlobAssetReference<UnitLibraryBlob>   unitLibrary;
    public float                            deltaTime;

    public void Execute(
        Entity                          self,
        in Health                       health,
        in UnitData                     unitData,
        in LocalTransform               transform,
        ref DynamicBuffer<Motivation>    behaviours,
        ref DynamicBuffer<ActionOption> options,
        ref DynamicBuffer<ThreatEntry>  threats,
        EnabledRefRO<ActionRequest> actionRequest)
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

        // Always track the aggressor so flee systems know who to run from.
        if (combatTargetLookup.HasComponent(self))
        {
            CombatTarget ct = combatTargetLookup[self];
            ct.targetEntity = topAggressor;
            combatTargetLookup[self] = ct;
        }

        if (aggressiveLookup.HasComponent(self))
        {
            AggressiveState agg = aggressiveLookup[self];
            agg.duration = AGGRESSIVE_LINGER;
            aggressiveLookup[self] = agg;
            aggressiveLookup.SetComponentEnabled(self, true);
        }

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

        if (!fightActivated || !actionRequest.ValueRO)
            return;

        int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
        if (unitIndex < 0)
            return;

        ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];

        float aggressorDist = 0f;
        if (transformLookup.TryGetComponent(topAggressor, out LocalTransform aggressorTransform))
            aggressorDist = math.distance(transform.Position, aggressorTransform.Position);

        for (int a = 0; a < unitBlob.attacks.Length; a++)
        {
            ActionType actionType  = unitBlob.attacks[a].action;
            AttackType attackType  = unitBlob.attacks[a].attack;
            int        attackIndex = (int)attackType;
            if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length)
                continue;
            float attackRange = attackLibrary.Value.attacks[attackIndex].range;
            float rangeScore  = AIUtils.AttackRangeScore(aggressorDist, attackRange);

            options.Add(new ActionOption
            {
                actionType     = actionType,
                motivationType = MotivationType.SelfDefence,
                utilityScore   = rangeScore,
                interaction    = false,
                targetEntity   = topAggressor,
            });
        }
    }
}
