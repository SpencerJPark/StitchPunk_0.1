using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Detects hostiles within awareness range. When a hostile is found:
///   - Sets CombatTarget to the nearest hostile
///   - Sets BloodLust motivation value to 100 so MotivationScoringSystem scores attack options appropriately
///   - Injects one ActionOption per AvailableAttack entry, scored by range fit vs current distance
///     (attacks whose range matches the actual distance score highest; out-of-range options score lower)
///   - Refreshes AggressiveState linger timer
///
/// ActionPrioritySystem applies a flat +1 tier bonus to all BloodLust options, pushing them
/// above civilian interaction scores (0-1) without hardcoding magic numbers.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(FactionRegistrySystem))]
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

        state.Dependency = new CombatAwarenessJob
        {
            transformLookup    = transformLookup,
            deadLookup         = deadLookup,
            factionEntities    = registry.entities,
            attackLibrary      = attackLibrary,
            unitLibrary        = unitLibrary,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
[WithPresent(typeof(CombatTarget))]
public partial struct CombatAwarenessJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Dead> deadLookup;
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity> factionEntities;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob> attackLibrary;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>   unitLibrary;

    public void Execute(
        Entity self,
        ref CombatTarget combatTarget,
        in Awareness awareness,
        in LocalTransform transform,
        in UnitData                      unitData,
        ref DynamicBuffer<Motivation>    motivations,
        ref DynamicBuffer<ActionOption>  options,
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
            combatTarget.targetEntity = nearestHostile;

            // Set BloodLust motivation to max urgency so MotivationScoringSystem
            // scores attack options at full weight via the BloodLust curve.
            AIUtils.SetMotivationValue(ref motivations, MotivationType.BloodLust, 100f);

            float nearestDist = math.sqrt(nearestDistanceSq);

            int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
            if (unitIndex < 0) return;
            ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];

            for (int a = 0; a < unitBlob.attacks.Length; a++)
            {
                ActionType actionType = unitBlob.attacks[a].action;
                AttackType attackType = unitBlob.attacks[a].attack;
                int        attackIndex = (int)attackType;
                if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length)
                    continue;
                float attackRange = attackLibrary.Value.attacks[attackIndex].range;

                float rangeScore = AIUtils.AttackRangeScore(nearestDist, attackRange);

                options.Add(new ActionOption
                {
                    actionType     = actionType,
                    motivationType = MotivationType.BloodLust,
                    utilityScore   = rangeScore,
                    interaction    = false,
                    targetEntity   = nearestHostile,
                });
            }
        }
    }
}
