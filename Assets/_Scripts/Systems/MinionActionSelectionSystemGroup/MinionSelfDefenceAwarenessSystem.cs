using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Self-defence for player-controlled minions. A revived minion is driven by player commands
// (PlayerUnitBrain) with its utility AI off (UtilityBrain disabled), so the normal
// SelfDefenceAwarenessSystem — gated on UtilityBrain — never fires for it. This is the minion-side
// equivalent: when the minion has taken hits (ThreatEntry, written by ThreatUpdateSystem which is
// not gated on any brain), emit combat options against the highest-threat attacker at
// SELF_DEFENCE_PRIORITY (3).
//
// "Self-defend unless commanded": a player order runs at activePriority = int.MaxValue, so
// WinnerSelection never lets a priority-3 self-defence option preempt a live player behavior.
// Self-defence therefore only takes effect when the minion is idle / uncommanded; issuing an order
// overrides it. No explicit "has command" test is needed — the priority gate handles it.
//
// Runs in MinionActionSelectionSystemGroup (after UtilityAISystemGroup cleared the buffer, before
// StateMachine selection), alongside MinionActionSelectionSystem. Gated [WithDisabled(UtilityBrain)]
// so it is mutually exclusive with the UtilityBrain-gated SelfDefenceAwarenessSystem.
[BurstCompile]
[UpdateInGroup(typeof(MinionActionSelectionSystemGroup))]
public partial struct MinionSelfDefenceAwarenessSystem : ISystem
{
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

        state.Dependency = new MinionSelfDefenceAwarenessJob
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
[WithAll(typeof(PlayerUnitBrain))]
[WithDisabled(typeof(UtilityBrain))]
[WithDisabled(typeof(Dead))]
public partial struct MinionSelfDefenceAwarenessJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<Dead>                 deadLookup;
    [ReadOnly] public ComponentLookup<LocalTransform>       transformLookup;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>   unitLibrary;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>  aiConfig;
    public float  deltaTime;
    public bool   loggingEnabled;
    public double timestamp;
    public EntityCommandBuffer.ParallelWriter ecb;

    // UtilityBrain is disabled on a minion, so read unitType from UnitData (kept in sync by the
    // baker and SwapBrainSystem) rather than the brain.
    public void Execute(
        [EntityIndexInQuery] int          entityIndex,
        Entity                            self,
        in UnitData                       unitData,
        in Awareness                      awareness,
        in LocalTransform                 transform,
        in StateMachine                   stateMachine,
        ref DynamicBuffer<ThreatEntry>    threats,
        ref DynamicBuffer<UtilityActions> actions)
    {
        if (threats.IsEmpty) return;

        // No-reactivation guard: already fighting (or executing a player attack order) — don't
        // re-emit and churn targets.
        if (stateMachine.action.IsCombatAction()) return;

        // Tick the flinch delay and pick the highest-threat attacker that is past it, still alive,
        // and within awareness range (range gate prevents chasing an attacker that has escaped,
        // since stale entries live for a few seconds).
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

        int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
        if (unitIndex < 0) return;
        ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];

        int addedCount = 0;
        for (int a = 0; a < unitBlob.attacks.Length; a++)
        {
            ActionType actionType = unitBlob.attacks[a].action;

            int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, unitData.unitType, actionType);
            if (defIndex < 0)
                continue;

            // priority only — not isPlayerOrdered. A real player order (int.MaxValue) always wins
            // over this, which is exactly the "self-defend unless commanded" rule.
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
                $"[MinionSelfDefence] Minion {self.Index} fighting back against attacker {topAggressor.Index} (threat {math.round(topThreat * 10f) / 10f}). Actions added: {addedCount}",
                LogLevel.Info, timestamp, category: LogCategory.AI);
    }
}
