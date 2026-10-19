using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Finds the nearest live hostile in awareness range from the faction registry and offers each of the unit's attacks against it at the action's authored tier.
// Sets BloodLust to 100 so the attack curves score it (a stopgap until the perception cache, AF-P6).
[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
public partial struct EnemyAwarenessSystem : ISystem
{
    private ComponentLookup<LocalTransform>  transformLookup;
    private ComponentLookup<Dead>            deadLookup;
    private ComponentLookup<CutsceneActor>   cutsceneActorLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FactionRegistry>();
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<UnitDataLibrary>();
        state.RequireForUpdate<BrainLibrary>();

        transformLookup    = state.GetComponentLookup<LocalTransform>(true);
        deadLookup         = state.GetComponentLookup<Dead>(true);
        cutsceneActorLookup = state.GetComponentLookup<CutsceneActor>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        deadLookup.Update(ref state);
        cutsceneActorLookup.Update(ref state);

        FactionRegistry registry = SystemAPI.GetSingleton<FactionRegistry>();

        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;

        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.AI) != 0;

        EntityCommandBuffer.ParallelWriter ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();

        state.Dependency = new CombatAwarenessJob
        {
            transformLookup    = transformLookup,
            deadLookup         = deadLookup,
            cutsceneActorLookup = cutsceneActorLookup,
            factionEntities    = registry.entities,
            attackLibrary      = attackLibrary,
            unitLibrary        = unitLibrary,
            aiConfig           = brainLibrary.blob,
            ecb                = ecb,
            loggingEnabled     = loggingEnabled,
            timestamp          = SystemAPI.Time.ElapsedTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct CombatAwarenessJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;
    [ReadOnly] public ComponentLookup<Dead>                    deadLookup;
    [ReadOnly] public ComponentLookup<CutsceneActor>           cutsceneActorLookup;
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity> factionEntities;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob>    attackLibrary;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>      unitLibrary;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>     aiConfig;
    public EntityCommandBuffer.ParallelWriter ecb;
    public bool                loggingEnabled;
    public double              timestamp;

    public void Execute(
        [EntityIndexInQuery] int entityIndex,
        Entity self,
        in UtilityBrain                  brain,
        in Awareness                     awareness,
        in LocalTransform                transform,
        in UnitData                      unitData,
        ref DynamicBuffer<Motivation>    motivations,
        ref DynamicBuffer<UtilityActions> actions,
        in DynamicBuffer<AttackFaction>  attackFactions)
    {
        float3 myPos   = transform.Position;
        
        float  rangeSq = awareness.range * awareness.range;
        Entity nearestHostile    = Entity.Null;
        float  nearestDistanceSq = float.MaxValue;

        for (int attackFactionIndex = 0; attackFactionIndex < attackFactions.Length; attackFactionIndex++)
        {
            byte factionKey = (byte)attackFactions[attackFactionIndex].faction;

            if (!factionEntities.TryGetFirstValue(factionKey, out Entity candidate,
                    out NativeParallelMultiHashMapIterator<byte> iterator))
                continue;

            do
            {
                if (candidate == self)
                    continue;

                if (deadLookup.HasComponent(candidate) && deadLookup.IsComponentEnabled(candidate))
                    continue;

                // DamageEventSystem makes a puppeted actor immune, so committing to one would leave
                // this unit swinging at an invulnerable target forever — and on camera at that.
                // Skipping it here is what makes the immunity invisible instead of a stalemate.
                if (cutsceneActorLookup.HasComponent(candidate) && cutsceneActorLookup.IsComponentEnabled(candidate))
                    continue;

                if (!transformLookup.TryGetComponent(candidate, out LocalTransform candidateTransform))
                    continue;

                float distSq = math.distancesq(myPos, candidateTransform.Position);
                if (distSq > rangeSq)
                    continue;

                if (distSq < nearestDistanceSq)
                {
                    nearestDistanceSq = distSq;
                    nearestHostile    = candidate;
                }

            } while (factionEntities.TryGetNextValue(out candidate, ref iterator));
        }

        if (nearestHostile != Entity.Null)
        {
            // Set BloodLust motivation to max urgency so MotivationScoringSystem
            // scores attack actions at full weight via the BloodLust curve.
            AIUtils.SetMotivationValue(ref motivations, NeedType.BloodLust, 100f);

            float nearestDist = math.sqrt(nearestDistanceSq);

            int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
            if (unitIndex < 0) return;
            ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];

            int addedCount = 0;
            for (int attackEntryIndex = 0; attackEntryIndex < unitBlob.attacks.Length; attackEntryIndex++)
            {
                ActionType   actionType   = unitBlob.attacks[attackEntryIndex].action;
                DamageSource damageSource = unitBlob.attacks[attackEntryIndex].attack;
                int          attackIndex  = (int)damageSource;
                if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length)
                    continue;

                int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, actionType);
                if (defIndex < 0)
                    continue;

                actions.Add(new UtilityActions
                {
                    actionType      = actionType,
                    actionDefIndex  = defIndex,
                    targetEntity    = nearestHostile,
                });
                addedCount++;
            }

            if (loggingEnabled)
                LogUtil.Log(ref ecb, entityIndex,
                    $"[EnemyAwareness] Entity {self.Index} found hostile {nearestHostile.Index} at dist {math.round(nearestDist * 100f) / 100f}. Actions added: {addedCount}",
                    LogLevel.Info, timestamp, category: LogCategory.AI);
        }
    }
}
