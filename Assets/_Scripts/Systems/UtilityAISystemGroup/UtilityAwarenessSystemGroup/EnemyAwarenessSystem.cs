using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Detects hostiles within awareness range. When a hostile is found:
///   - Sets CombatTarget to the nearest hostile
///   - Sets BloodLust motivation value to 100 so MotivationScoringSystem scores attack actions appropriately
///   - Injects one ActionOption per AvailableAttack entry, scored by range fit vs current distance
///     (attacks whose range matches the actual distance score highest; out-of-range actions score lower)
///   - Refreshes AggressiveState linger timer
///
/// ActionPrioritySystem applies a flat +1 tier bonus to all BloodLust actions, pushing them
/// above civilian interaction scores (0-1) without hardcoding magic numbers.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
public partial struct EnemyAwarenessSystem : ISystem
{
    private ComponentLookup<LocalTransform>  transformLookup;
    private ComponentLookup<Dead>            deadLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<FactionRegistry>();
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<UnitDataLibrary>();
        state.RequireForUpdate<BrainLibrary>();

        transformLookup    = state.GetComponentLookup<LocalTransform>(true);
        deadLookup         = state.GetComponentLookup<Dead>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        deadLookup.Update(ref state);

        FactionRegistry registry = SystemAPI.GetSingleton<FactionRegistry>();

        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;

        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.AI) != 0;

        EntityCommandBuffer ecb = loggingEnabled
            ? SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
            : default;

        state.Dependency = new CombatAwarenessJob
        {
            transformLookup    = transformLookup,
            deadLookup         = deadLookup,
            factionEntities    = registry.entities,
            attackLibrary      = attackLibrary,
            unitLibrary        = unitLibrary,
            aiConfig           = brainLibrary.blob,
            ecb                = ecb,
            loggingEnabled     = loggingEnabled,
            timestamp          = SystemAPI.Time.ElapsedTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
public partial struct CombatAwarenessJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;
    [ReadOnly] public ComponentLookup<Dead>                    deadLookup;
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity> factionEntities;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob>    attackLibrary;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>      unitLibrary;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>     aiConfig;
    public EntityCommandBuffer ecb;
    public bool                loggingEnabled;
    public double              timestamp;

    public void Execute(
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

        for (int f = 0; f < attackFactions.Length; f++)
        {
            byte fKey = (byte)attackFactions[f].faction;

            if (!factionEntities.TryGetFirstValue(fKey, out Entity candidate,
                    out NativeParallelMultiHashMapIterator<byte> iterator))
                continue;

            do
            {
                if (candidate == self)
                    continue;

                if (deadLookup.HasComponent(candidate) && deadLookup.IsComponentEnabled(candidate))
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
            for (int a = 0; a < unitBlob.attacks.Length; a++)
            {
                ActionType   actionType   = unitBlob.attacks[a].action;
                DamageSource damageSource = unitBlob.attacks[a].attack;
                int          attackIndex  = (int)damageSource;
                if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length)
                    continue;
                float attackRange = attackLibrary.Value.attacks[attackIndex].range;

                int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, actionType);
                if (defIndex < 0)
                    continue;

                actions.Add(new UtilityActions
                {
                    actionType      = actionType,
                    actionDefIndex  = defIndex,
                    priority = 3,
                    needsValidation = false,
                    targetEntity    = nearestHostile,
                });
                addedCount++;
            }

            if (loggingEnabled)
                LogUtil.Log(ref ecb,
                    $"[EnemyAwareness] Entity {self.Index} found hostile {nearestHostile.Index} at dist {math.round(nearestDist * 100f) / 100f}. Actions added: {addedCount}",
                    LogLevel.Info, timestamp, category: LogCategory.AI);
        }
    }
}
