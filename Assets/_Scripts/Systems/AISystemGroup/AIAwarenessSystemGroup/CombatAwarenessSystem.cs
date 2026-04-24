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
public partial struct CombatAwarenessSystem : ISystem
{
    private ComponentLookup<LocalTransform>  transformLookup;
    private ComponentLookup<Dead>            deadLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<FactionRegistry>();
        state.RequireForUpdate<AttackLibrary>();

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

        state.Dependency = new CombatAwarenessJob
        {
            transformLookup    = transformLookup,
            deadLookup         = deadLookup,
            factionEntities    = registry.entities,
            attackLibrary      = attackLibrary,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain), typeof(NeedsAction))]
[WithPresent(typeof(CombatTarget))]
public partial struct CombatAwarenessJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Dead> deadLookup;
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity> factionEntities;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob>    attackLibrary;

    public void Execute(
        Entity self,
        ref CombatTarget combatTarget,
        in Awareness awareness,
        in LocalTransform transform,
        ref DynamicBuffer<Motivation>    motivations,
        ref DynamicBuffer<ActionOption>  options,
        in DynamicBuffer<AttackFaction>  attackFactions,
        in DynamicBuffer<AvailableAttack> availableAttacks)
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
            for (int m = 0; m < motivations.Length; m++)
            {
                Motivation motivation = motivations[m];
                if (motivation.motivationType == MotivationType.BloodLust)
                {
                    motivation.value = 100f;
                    motivations[m] = motivation;
                    break;
                }
            }

            float nearestDist = math.sqrt(nearestDistanceSq);

            for (int a = 0; a < availableAttacks.Length; a++)
            {
                AttackType attackType = availableAttacks[a].attackType;
                ActionType actionType = attackLibrary.Value.attacks[(int)attackType].actionType;
                float attackRange = attackLibrary.Value.attacks[(int)attackType].range;

                // Score = 1.0 if target is at or within range; decays as target moves farther out.
                // Longer-reach attacks score higher when target is at distance, encouraging
                // the NPC to lead with the appropriate strike for its current position.
                float rangeScore = nearestDist <= attackRange
                    ? 1.0f
                    : attackRange / nearestDist;

                options.Add(new ActionOption
                {
                    actionType     = actionType,
                    motivationType = MotivationType.BloodLust,
                    attackType     = attackType,
                    utilityScore   = rangeScore,
                    interaction    = false,
                    targetEntity   = nearestHostile,
                });
            }
        }
    }
}
