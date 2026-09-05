using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Fight-back half of fight-or-flight: when a unit has taken hits (ThreatEntry written by
// DamageEventSystem), emit combat options targeting the highest-threat attacker at
// SELF_DEFENCE_PRIORITY — above normal combat (2) and wander (1) — so WinnerSelection preempts
// the current behavior via the pending path. Deliberately NOT gated on ActionRequest: the
// override must fire mid-behavior. Does not enable ActionInterruptRequest — the interrupt path
// resets to Idle and would lose the chosen action; the pending path carries it.
// IsCombatAction() is the no-reactivation guard: once fighting (or fleeing), stop re-emitting.
// Flee branch + bravery choice are Phase 3a.
[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
[UpdateAfter(typeof(EnemyAwarenessSystem))]
public partial struct SelfDefenceAwarenessSystem : ISystem
{
    public const int SELF_DEFENCE_PRIORITY = 3;

    private ComponentLookup<Dead>           _deadLookup;
    private ComponentLookup<LocalTransform> _transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<UnitDataLibrary>();
        state.RequireForUpdate<BrainLibrary>();
        _deadLookup      = state.GetComponentLookup<Dead>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _deadLookup.Update(ref state);
        _transformLookup.Update(ref state);

        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;
        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.AI) != 0;

        EntityCommandBuffer.ParallelWriter ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();

        state.Dependency = new SelfDefenceAwarenessJob
        {
            deadLookup      = _deadLookup,
            transformLookup = _transformLookup,
            unitLibrary     = unitLibrary,
            aiConfig        = brainLibrary.blob,
            deltaTime       = SystemAPI.Time.DeltaTime,
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
public partial struct SelfDefenceAwarenessJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<Dead>                 deadLookup;
    [ReadOnly] public ComponentLookup<LocalTransform>       transformLookup;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>   unitLibrary;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>  aiConfig;
    public float  deltaTime;
    public bool   loggingEnabled;
    public double timestamp;
    public EntityCommandBuffer.ParallelWriter ecb;

    public void Execute(
        [EntityIndexInQuery] int          entityIndex,
        Entity                            self,
        in UtilityBrain                   brain,
        in Awareness                      awareness,
        in LocalTransform                 transform,
        in StateMachine                   stateMachine,
        ref DynamicBuffer<ThreatEntry>    threats,
        ref DynamicBuffer<Motivation>     motivations,
        ref DynamicBuffer<UtilityActions> actions)
    {
        if (threats.IsEmpty) return;

        // No-reactivation guard: already fighting (or fleeing) — don't re-emit and churn targets.
        if (stateMachine.action.IsCombatAction()) return;

        // Tick the flinch delay and pick the highest-threat attacker that is past it,
        // still alive, and within awareness range (range gate prevents chase ping-pong
        // after the attacker escapes, since stale entries live for 4s).
        float  rangeSq      = awareness.range * awareness.range;
        Entity topAggressor = Entity.Null;
        float  topThreat    = 0f;

        for (int i = 0; i < threats.Length; i++)
        {
            ThreatEntry threat = threats[i];

            if (threat.reactionDelay > 0f)
            {
                threat.reactionDelay -= deltaTime;
                threats[i]            = threat;
                // Evaluate with the POST-tick value: FleeAwareness runs after this system and
                // sees the updated buffer, so skipping the crossing frame here would let flee
                // fire one frame before fight — flee would win tier 3 unopposed and lock in
                // (Flee is a combat action, which then early-outs this system forever).
                if (threat.reactionDelay > 0f)
                    continue;
            }

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

        int unitIndex = unitLibrary.Value.FindByUnitType(brain.unitType);
        if (unitIndex < 0) return;
        ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];

        // Match EnemyAwareness scoring context so the BloodLust curve scores these at full weight.
        AIUtils.SetMotivationValue(ref motivations, NeedType.BloodLust, 100f);

        int addedCount = 0;
        for (int a = 0; a < unitBlob.attacks.Length; a++)
        {
            ActionType actionType = unitBlob.attacks[a].action;

            int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, actionType);
            if (defIndex < 0)
                continue;

            actions.Add(new UtilityActions
            {
                actionType      = actionType,
                actionDefIndex  = defIndex,
                priority        = SelfDefenceAwarenessSystem.SELF_DEFENCE_PRIORITY,
                needsValidation = false,
                targetEntity    = topAggressor,
            });
            addedCount++;
        }

        if (addedCount > 0 && loggingEnabled)
            LogUtil.Log(ref ecb, entityIndex,
                $"[SelfDefence] Entity {self.Index} fighting back against attacker {topAggressor.Index} (threat {math.round(topThreat * 10f) / 10f}). Actions added: {addedCount}",
                LogLevel.Info, timestamp, category: LogCategory.AI);
    }
}
