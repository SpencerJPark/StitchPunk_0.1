using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(EnemyAwarenessSystem))]
public partial struct SelfDefenceAwarenessSystem : ISystem
{
    private ComponentLookup<Dead>            deadLookup;
    private ComponentLookup<LocalTransform>  transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<UnitDataLibrary>();

        deadLookup         = state.GetComponentLookup<Dead>(true);
        transformLookup    = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        deadLookup.Update(ref state);
        transformLookup.Update(ref state);

        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;

        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        state.Dependency = new FightOrFlightJob
        {
            deadLookup         = deadLookup,
            transformLookup    = transformLookup,
            attackLibrary      = attackLibrary,
            unitLibrary        = unitLibrary,
            deltaTime          = SystemAPI.Time.DeltaTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AIBrain))]
[WithDisabled(typeof(Dead))]
[WithPresent(typeof(ActionInterruptRequest))]
public partial struct FightOrFlightJob : IJobEntity
{
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
        in UnitData                          unitData,
        in LocalTransform                    transform,
        in Health                            health,
        in Personality                       personality,
        ref DynamicBuffer<Motivation>        behaviours,
        ref DynamicBuffer<ActionOption>      options,
        ref DynamicBuffer<ThreatEntry>       threats,
        in CurrentAction                     currentAction,
        EnabledRefRW<ActionInterruptRequest> interruptRequest)
    {
        if (threats.Length == 0)
            return;

        bool inCombat = currentAction.actionType.IsCombatAction();

        Entity topAggressor = Entity.Null;
        float  topThreat    = 0f;
        float  totalThreat  = 0f;
        for (int i = 0; i < threats.Length; i++)
        {
            ThreatEntry threat = threats[i];

            totalThreat += threat.threatScore;

            if (threat.reactionDelay > 0f)
            {
                threat.reactionDelay -= deltaTime;
                threats[i]            = threat;
                continue;
            }

            if (threat.threatScore <= topThreat)
                continue;

            if (deadLookup.IsComponentEnabled(threat.attackerEntity))
                continue;

            topThreat    = threat.threatScore;
            topAggressor = threat.attackerEntity;
        }

        if (topAggressor == Entity.Null)
            return;

        int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
        if (unitIndex < 0)
            return;

        ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];

        float aggressorDist = 0f;
        if (transformLookup.TryGetComponent(topAggressor, out LocalTransform aggressorTransform))
            aggressorDist = math.distance(transform.Position, aggressorTransform.Position);

        // Desperation rises as cumulative received damage approaches 75% of max health.
        // At full desperation, even cowards are pushed toward fighting back.
        float desperationThreshold = math.max(1f, health.healthAmountMax * 0.75f);
        float desperation          = math.saturate(totalThreat / desperationThreshold);
        float fightWillingness     = personality.bravery + desperation * (1f - personality.bravery);

        for (int a = 0; a < unitBlob.attacks.Length; a++)
        {
            ActionType actionType  = unitBlob.attacks[a].action;
            AttackType attackType  = unitBlob.attacks[a].attack;
            int        attackIndex = (int)attackType;
            if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length)
                continue;
            float attackRange   = attackLibrary.Value.attacks[attackIndex].range;
            float rangeScore    = AIUtils.AttackRangeScore(aggressorDist, attackRange);
            float attackUtility = rangeScore * fightWillingness;

            options.Add(new ActionOption
            {
                actionType     = actionType,
                motivationType = MotivationType.SelfDefence,
                priority       = 2,
                utilityScore   = attackUtility,
                needsValidation    = false,
                targetEntity   = topAggressor,
            });
        }

        // Don't interrupt ongoing combat — let the existing melee action continue.
        // HealthAwareness will interrupt if health drops critically.
        if (!inCombat)
            interruptRequest.ValueRW = true;
    }
}
