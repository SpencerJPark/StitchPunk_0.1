using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Flight half of fight-or-flight: when a unit has taken hits (ThreatEntry), offer a Flee option
// targeting the highest-threat attacker. Normally emitted at the actionDef's priority (3 — same
// tier as self-defence fight options) so health/bravery consideration curves decide fight vs flee
// at decision points. When health is critical AND the unit is cowardly enough (legacy formula
// (1 − healthRatio) × (1 − bravery)), the entry is bumped to FLEE_CRITICAL_PRIORITY so it outranks
// an active fight (activePriority 3) and breaks off mid-combat via the pending/preemption path.
// Deliberately NOT gated on ActionRequest — the override must fire mid-behavior.
// IsCombatAction() in SelfDefence covers Flee, so neither system re-emits while fleeing.
[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
[UpdateAfter(typeof(SelfDefenceAwarenessSystem))]
public partial struct FleeAwarenessSystem : ISystem
{
    public const int   FLEE_CRITICAL_PRIORITY = 4;     // above SELF_DEFENCE_PRIORITY (3)
    public const float CRITICAL_HEALTH        = 0.3f;  // break-off considered below this ratio
    public const float BREAK_OFF_THRESHOLD    = 0.35f; // (1 − health) × (1 − bravery) must exceed
    public const float LOW_HEALTH_THRESHOLD   = 0.6f;  // flee is never offered above this ratio (legacy low bracket)

    private ComponentLookup<Dead>           _deadLookup;
    private ComponentLookup<LocalTransform> _transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BrainLibrary>();
        _deadLookup      = state.GetComponentLookup<Dead>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _deadLookup.Update(ref state);
        _transformLookup.Update(ref state);

        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.AI) != 0;

        EntityCommandBuffer.ParallelWriter ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();

        state.Dependency = new FleeAwarenessJob
        {
            deadLookup      = _deadLookup,
            transformLookup = _transformLookup,
            aiConfig        = brainLibrary.blob,
            ecb             = ecb,
            loggingEnabled  = loggingEnabled,
            timestamp       = SystemAPI.Time.ElapsedTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain))]
[WithDisabled(typeof(Dead))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct FleeAwarenessJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<Dead>                deadLookup;
    [ReadOnly] public ComponentLookup<LocalTransform>      transformLookup;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;
    public bool   loggingEnabled;
    public double timestamp;
    public EntityCommandBuffer.ParallelWriter ecb;

    public void Execute(
        [EntityIndexInQuery] int                  entityIndex,
        Entity                                    self,
        in UtilityBrain                           brain,
        in Awareness                              awareness,
        in LocalTransform                         transform,
        in Health                                 health,
        in StateMachine                           stateMachine,
        in DynamicBuffer<PersonalityAttributes>   personality,
        in DynamicBuffer<ThreatEntry>             threats,
        ref DynamicBuffer<Motivation>             motivations,
        ref DynamicBuffer<UtilityActions>         actions)
    {
        if (threats.IsEmpty) return;

        // Already running away — don't re-emit and churn flee destinations.
        if (stateMachine.action == ActionType.Flee) return;

        // Healthy units never consider fleeing — fight-back (SelfDefence) is the only
        // threat response above this ratio. Mirrors the legacy 30–60% "low" bracket.
        float healthRatio = health.healthAmountMax > 0
            ? (float)health.healthAmount / health.healthAmountMax
            : 1f;
        if (healthRatio >= FleeAwarenessSystem.LOW_HEALTH_THRESHOLD) return;

        // Same candidate rules as SelfDefence: past the flinch delay (SelfDefence owns ticking it
        // and runs first this frame), still alive, within awareness range. Highest threat wins.
        float  rangeSq      = awareness.range * awareness.range;
        Entity topAggressor = Entity.Null;
        float  topThreat    = 0f;

        for (int i = 0; i < threats.Length; i++)
        {
            ThreatEntry threat = threats[i];

            if (threat.reactionDelay > 0f)
                continue;

            if (threat.threatScore <= topThreat)
                continue;

            if (!deadLookup.HasComponent(threat.attackerEntity)
                || deadLookup.IsComponentEnabled(threat.attackerEntity))
                continue;

            if (!transformLookup.TryGetComponent(threat.attackerEntity, out LocalTransform attackerTransform)
                || math.distancesq(transform.Position, attackerTransform.Position) > rangeSq)
                continue;

            topThreat    = threat.threatScore;
            topAggressor = threat.attackerEntity;
        }

        if (topAggressor == Entity.Null) return;

        int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.Flee);
        if (defIndex < 0) return;

        // SelfPreservation drives the scoring curve — mirrors EnemyAwareness setting BloodLust.
        AIUtils.SetMotivationValue(ref motivations, NeedType.SelfPreservation, 100f);

        float bravery = 0.5f; // same default as ConsiderationScoringSystem.GetTraitValue
        for (int i = 0; i < personality.Length; i++)
        {
            if (personality[i].personality != PersonalityTypes.Bravery) continue;
            bravery = math.saturate(personality[i].value);
            break;
        }

        // priority 0 → scoring promotes to the FleeAction actionDef's tier (3); the critical
        // break-off bump outranks an active fight so a hurt coward disengages mid-combat.
        int priority = 0;
        bool breakOff = healthRatio < FleeAwarenessSystem.CRITICAL_HEALTH
            && (1f - healthRatio) * (1f - bravery) > FleeAwarenessSystem.BREAK_OFF_THRESHOLD;
        if (breakOff)
            priority = FleeAwarenessSystem.FLEE_CRITICAL_PRIORITY;

        actions.Add(new UtilityActions
        {
            actionType      = ActionType.Flee,
            actionDefIndex  = defIndex,
            targetEntity    = topAggressor,
            priority        = priority,
            needsValidation = false,
        });

        if (loggingEnabled)
        {
            int breakOffFlag = breakOff ? 1 : 0; // hoisted: Burst forbids control flow inside format arguments (BC1352)
            LogUtil.Log(ref ecb, entityIndex,
                $"[FleeAwareness] Entity {self.Index} offered Flee from attacker {topAggressor.Index} (health {math.round(healthRatio * 100f) / 100f}, bravery {math.round(bravery * 100f) / 100f}, breakOff: {breakOffFlag})",
                LogLevel.Info, timestamp, category: LogCategory.AI);
        }
    }
}
