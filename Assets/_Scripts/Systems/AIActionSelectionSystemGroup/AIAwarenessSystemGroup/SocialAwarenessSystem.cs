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
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Dead>           deadLookup;
    private ComponentLookup<SocialEngaged>  socialEngagedLookup;
    private ComponentLookup<CurrentAction>  currentActionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<FactionRegistry>();
        state.RequireForUpdate<UnitDataLibrary>();
        transformLookup     = state.GetComponentLookup<LocalTransform>(true);
        deadLookup          = state.GetComponentLookup<Dead>(true);
        socialEngagedLookup = state.GetComponentLookup<SocialEngaged>(true);
        currentActionLookup = state.GetComponentLookup<CurrentAction>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        deadLookup.Update(ref state);
        socialEngagedLookup.Update(ref state);
        currentActionLookup.Update(ref state);

        FactionRegistry registry = SystemAPI.GetSingleton<FactionRegistry>();
        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        // .Schedule not .ScheduleParallel — FactionRegistry NativeParallelMultiHashMap is
        // unsafe to read in parallel while writers may exist in the same update group.
        state.Dependency = new SocialAwarenessJob
        {
            transformLookup     = transformLookup,
            deadLookup          = deadLookup,
            socialEngagedLookup = socialEngagedLookup,
            currentActionLookup = currentActionLookup,
            factionEntities     = registry.entities,
            unitLibrary         = unitLibrary,
            elapsedTime         = (float)SystemAPI.Time.ElapsedTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AIBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead), typeof(SocialEngaged))]
partial struct SocialAwarenessJob : IJobEntity
{
    private const float SOCIAL_MOTIVATION_THRESHOLD = 20f;

    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Dead>           deadLookup;
    [ReadOnly] public ComponentLookup<SocialEngaged>  socialEngagedLookup;
    [ReadOnly] public ComponentLookup<CurrentAction>  currentActionLookup;
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
            if (motivations[m].motivationType == MotivationType.Social)
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

                if (socialEngagedLookup.HasComponent(candidate) && socialEngagedLookup.IsComponentEnabled(candidate)) continue;

                if (IsOnCooldown(recentInteractions, candidate, elapsedTime)) continue;

                if (currentActionLookup.TryGetComponent(candidate, out CurrentAction candidateAction)
                    && candidateAction.actionType.IsCombatAction()) continue;

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
            actionType     = ActionType.Talk,
            motivationType = MotivationType.Social,
            priority       = 1,
            utilityScore   = 1f - math.saturate(bestDistSq / rangeSq),
            interaction    = true,
            targetEntity   = bestCandidate,
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

// Responder path: a unit whose SocialEngaged was set by ValidateSocialJob re-enters
// selection after an ActionInterruptRequest and needs to pick TalkAction immediately.
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(SocialAwarenessSystem))]
public partial struct SocialRespondAwarenessSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<GameSceneTag>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new SocialRespondJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AIBrain), typeof(ActionRequest), typeof(SocialEngaged))]
[WithDisabled(typeof(Dead))]
partial struct SocialRespondJob : IJobEntity
{
    void Execute(in ConversationContext context, ref DynamicBuffer<ActionOption> options)
    {
        if (context.conversationPartner == Entity.Null || !context.isResponder) return;

        // interaction=false skips ValidateSocialJob — locking already happened.
        // Priority 2 beats normal social initiations (1) but yields to self-defence (3).
        options.Add(new ActionOption
        {
            actionType     = ActionType.Talk,
            motivationType = MotivationType.Social,
            priority       = 2,
            utilityScore   = 100f,
            interaction    = false,
            targetEntity   = context.conversationPartner,
        });
    }
}
