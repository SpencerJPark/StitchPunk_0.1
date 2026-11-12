using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Minion-side twin of SelfDefenceAwarenessSystem (which needs UtilityBrain, off on minions).
// Player orders run at activePriority int.MaxValue, so this priority-3 option only wins when uncommanded.
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
    public bool   loggingEnabled;
    public double timestamp;
    public EntityCommandBuffer.ParallelWriter ecb;

    // UtilityBrain is disabled on a minion, so unitType comes from UnitData, not the brain.
    public void Execute(
        [EntityIndexInQuery] int          entityIndex,
        Entity                            self,
        in UnitData                       unitData,
        in Awareness                      awareness,
        in LocalTransform                 transform,
        in StateMachine                   stateMachine,
        in DynamicBuffer<ThreatEntry>     threats,
        ref DynamicBuffer<UtilityActions> actions)
    {
        if (threats.IsEmpty) return;

        // Already fighting (or on a player attack order): don't re-emit and churn targets.
        if (stateMachine.action.IsCombatAction()) return;

        // Range gate stops chasing an escaped attacker, since stale threat entries live a few seconds.
        float  rangeSq      = awareness.range * awareness.range;
        Entity topAggressor = Entity.Null;
        float  topThreat    = 0f;

        for (int i = 0; i < threats.Length; i++)
        {
            ThreatEntry threat = threats[i];

            if (threat.reactionDelay > 0f) continue;

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

            // Priority only, not isPlayerOrdered, so a real player order always wins.
            actions.Add(new UtilityActions
            {
                actionType      = actionType,
                actionDefIndex  = defIndex,
                priority        = SelfDefenceAwarenessSystem.SELF_DEFENCE_PRIORITY,
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
