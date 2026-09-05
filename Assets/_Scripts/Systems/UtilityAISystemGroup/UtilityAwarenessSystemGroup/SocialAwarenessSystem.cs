using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Initiator path of the Talk pipeline: units scan their socialFactions (UnitDataBlob, mirrors
// attackFactions) for the nearest available partner and emit a Talk option. Utility comes from
// the Social motivation curve on TalkAction — no context flag is set here; the decayed Social
// value drives the score directly. The full handshake: this option wins → Talk behavior runs
// Approach → RequestSocialResponse (SocialInvite on the partner) → SocialResponseSystem accepts
// or declines next frame.
[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
public partial struct SocialAwarenessSystem : ISystem
{
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<Dead>           _deadLookup;
    private ComponentLookup<StateMachine>   _stateMachineLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<FactionRegistry>();
        state.RequireForUpdate<UnitDataLibrary>();
        state.RequireForUpdate<BrainLibrary>();
        _transformLookup    = state.GetComponentLookup<LocalTransform>(true);
        _deadLookup         = state.GetComponentLookup<Dead>(true);
        _stateMachineLookup = state.GetComponentLookup<StateMachine>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _transformLookup.Update(ref state);
        _deadLookup.Update(ref state);
        _stateMachineLookup.Update(ref state);

        FactionRegistry registry = SystemAPI.GetSingleton<FactionRegistry>();
        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;
        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        state.Dependency = new SocialAwarenessJob
        {
            transformLookup    = _transformLookup,
            deadLookup         = _deadLookup,
            stateMachineLookup = _stateMachineLookup,
            factionEntities    = registry.entities,
            unitLibrary        = unitLibrary,
            aiConfig           = brainLibrary.blob,
            timestamp          = SystemAPI.Time.ElapsedTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct SocialAwarenessJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;
    [ReadOnly] public ComponentLookup<Dead>                    deadLookup;
    [ReadOnly] public ComponentLookup<StateMachine>            stateMachineLookup;
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity> factionEntities;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>      unitLibrary;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>     aiConfig;
    public double timestamp;

    public void Execute(
        Entity                              self,
        in UtilityBrain                     brain,
        in Awareness                        awareness,
        in LocalTransform                   transform,
        in StateMachine                     stateMachine,
        in DynamicBuffer<RecentInteraction> recentInteractions,
        ref DynamicBuffer<UtilityActions>   actions)
    {
        // Already talking or in combat — no new social options.
        if (stateMachine.action == ActionType.Talk) return;
        if (stateMachine.action.IsCombatAction()) return;

        int talkDefIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.Talk);
        if (talkDefIndex < 0) return;

        int unitIndex = unitLibrary.Value.FindByUnitType(brain.unitType);
        if (unitIndex < 0) return;
        ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];
        if (unitBlob.socialFactions.Length == 0) return;

        float3 myPos   = transform.Position;
        float  rangeSq = awareness.range * awareness.range;

        Entity nearestPartner    = Entity.Null;
        float  nearestDistanceSq = float.MaxValue;

        for (int f = 0; f < unitBlob.socialFactions.Length; f++)
        {
            byte factionKey = (byte)unitBlob.socialFactions[f];

            if (!factionEntities.TryGetFirstValue(factionKey, out Entity candidate,
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
                if (distSq > rangeSq || distSq >= nearestDistanceSq)
                    continue;

                // Skip partners that are already talking or fighting — SocialResponseSystem would
                // decline anyway; filtering here avoids wasted approaches.
                if (stateMachineLookup.TryGetComponent(candidate, out StateMachine candidateSm)
                    && (candidateSm.action == ActionType.Talk || candidateSm.action.IsCombatAction()))
                    continue;

                if (IsOnCooldown(candidate, in recentInteractions))
                    continue;

                nearestDistanceSq = distSq;
                nearestPartner    = candidate;

            } while (factionEntities.TryGetNextValue(out candidate, ref iterator));
        }

        if (nearestPartner == Entity.Null) return;

        actions.Add(new UtilityActions
        {
            actionType      = ActionType.Talk,
            actionDefIndex  = talkDefIndex,
            targetEntity    = nearestPartner,
            needsValidation = false,
        });
    }

    // Expiry-aware recency check: an entry only blocks while its cooldown is still running.
    private bool IsOnCooldown(Entity candidate, in DynamicBuffer<RecentInteraction> recentInteractions)
    {
        for (int i = 0; i < recentInteractions.Length; i++)
        {
            if (recentInteractions[i].entity != candidate) continue;
            if (recentInteractions[i].cooldownEndTime > (float)timestamp) return true;
        }
        return false;
    }
}
