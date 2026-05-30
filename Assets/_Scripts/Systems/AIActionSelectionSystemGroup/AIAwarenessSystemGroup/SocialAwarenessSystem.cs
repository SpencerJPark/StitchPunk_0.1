using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Initiator path: units seeking social interaction append a Talk ActionOption.
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
public partial struct SocialAwarenessSystem : ISystem
{
    private ComponentLookup<LocalTransform>  transformLookup;
    private ComponentLookup<Dead>            deadLookup;
    private ComponentLookup<SocialAvailable> socialAvailableLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<FactionRegistry>();
        state.RequireForUpdate<UnitDataLibrary>();
        transformLookup       = state.GetComponentLookup<LocalTransform>(true);
        deadLookup            = state.GetComponentLookup<Dead>(true);
        socialAvailableLookup = state.GetComponentLookup<SocialAvailable>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        deadLookup.Update(ref state);
        socialAvailableLookup.Update(ref state);

        FactionRegistry registry = SystemAPI.GetSingleton<FactionRegistry>();
        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        // .Schedule not .ScheduleParallel — FactionRegistry NativeParallelMultiHashMap is
        // unsafe to read in parallel while writers may exist in the same update group.
        state.Dependency = new SocialAwarenessJob
        {
            transformLookup       = transformLookup,
            deadLookup            = deadLookup,
            socialAvailableLookup = socialAvailableLookup,
            factionEntities       = registry.entities,
            unitLibrary           = unitLibrary,
            elapsedTime           = (float)SystemAPI.Time.ElapsedTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AIBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead), typeof(TalkAction))]
partial struct SocialAwarenessJob : IJobEntity
{
    private const float SOCIAL_MOTIVATION_THRESHOLD = -20f;

    [ReadOnly] public ComponentLookup<LocalTransform>  transformLookup;
    [ReadOnly] public ComponentLookup<Dead>            deadLookup;
    [ReadOnly] public ComponentLookup<SocialAvailable> socialAvailableLookup;
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity> factionEntities;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>      unitLibrary;
    public float elapsedTime;

    void Execute(
        Entity                                   self,
        in LocalTransform                        transform,
        in UnitData                              unitData,
        in Awareness                             awareness,
        in DynamicBuffer<Motivation>             motivations,
        in DynamicBuffer<RecentInteraction>      recentInteractions,
        ref DynamicBuffer<ActionOption>          options)
    {
        float socialValue = 0f;
        for (int m = 0; m < motivations.Length; m++)
        {
            if (motivations[m].needType == NeedType.Social)
            {
                socialValue = motivations[m].value;
                break;
            }
        }
        if (socialValue < SOCIAL_MOTIVATION_THRESHOLD) return;

        int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
        if (unitIndex < 0) return;
        ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];
        if (unitBlob.socialFactions.Length == 0) return;

        float3 myPos   = transform.Position;
        float  rangeSq = awareness.range * awareness.range;
        Entity bestCandidate = Entity.Null;
        float  bestDistSq    = float.MaxValue;

        for (int f = 0; f < unitBlob.socialFactions.Length; f++)
        {
            byte fKey = (byte)unitBlob.socialFactions[f];
            if (!factionEntities.TryGetFirstValue(fKey, out Entity candidate,
                    out NativeParallelMultiHashMapIterator<byte> it)) continue;

            do
            {
                if (candidate == self) continue;

                if (deadLookup.HasComponent(candidate) && deadLookup.IsComponentEnabled(candidate)) continue;

                // SocialAvailable is disabled when candidate is in combat, fleeing, or already
                // locked in a conversation — one flag replaces the old TalkAction + IsCombatAction checks.
                if (!socialAvailableLookup.HasComponent(candidate) || !socialAvailableLookup.IsComponentEnabled(candidate)) continue;

                if (IsOnCooldown(recentInteractions, candidate, elapsedTime)) continue;

                if (!transformLookup.TryGetComponent(candidate, out LocalTransform candidateTransform)) continue;

                float distSq = math.distancesq(myPos, candidateTransform.Position);
                if (distSq > rangeSq || distSq >= bestDistSq) continue;

                bestDistSq    = distSq;
                bestCandidate = candidate;
            }
            while (factionEntities.TryGetNextValue(out candidate, ref it));
        }

        if (bestCandidate == Entity.Null) return;

        options.Add(new ActionOption
        {
            actionType      = ActionType.Talk,
            needType        = NeedType.Social,
            priority        = 1,
            utilityScore    = 1f - math.saturate(bestDistSq / rangeSq),
            advertisedDelta = 40f,
            needsValidation = true,
            targetEntity    = bestCandidate,
        });
    }

    private static bool IsOnCooldown(
        in DynamicBuffer<RecentInteraction> recent,
        Entity candidate,
        float time)
    {
        for (int i = 0; i < recent.Length; i++)
        {
            if (recent[i].entity == candidate && recent[i].cooldownEndTime > time)
                return true;
        }
        return false;
    }
}



