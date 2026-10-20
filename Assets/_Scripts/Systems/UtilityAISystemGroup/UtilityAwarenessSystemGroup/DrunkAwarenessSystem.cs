using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// LH-D13: heavily intoxicated units offer PassOut (Critical) and, if short-tempered, a Brawl against
// a same-faction neighbour or their conversation partner. Read-only on Intoxication — the brawl
// behaviour start is where brawlCooldown gets applied.
[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
public partial struct DrunkAwarenessSystem : ISystem
{
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<Dead>           _deadLookup;
    private ComponentLookup<CutsceneActor>  _cutsceneActorLookup;
    private ComponentLookup<StateMachine>   _stateMachineLookup;
    private ComponentLookup<Conversation>   _conversationLookup;
    private ComponentLookup<Player>         _playerLookup;
    private ComponentLookup<Minion>         _minionLookup;
    private ComponentLookup<PlayerUnitBrain> _playerUnitBrainLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FactionRegistry>();
        state.RequireForUpdate<BrainLibrary>();
        _transformLookup       = state.GetComponentLookup<LocalTransform>(true);
        _deadLookup            = state.GetComponentLookup<Dead>(true);
        _cutsceneActorLookup   = state.GetComponentLookup<CutsceneActor>(true);
        _stateMachineLookup    = state.GetComponentLookup<StateMachine>(true);
        _conversationLookup    = state.GetComponentLookup<Conversation>(true);
        _playerLookup          = state.GetComponentLookup<Player>(true);
        _minionLookup          = state.GetComponentLookup<Minion>(true);
        _playerUnitBrainLookup = state.GetComponentLookup<PlayerUnitBrain>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _transformLookup.Update(ref state);
        _deadLookup.Update(ref state);
        _cutsceneActorLookup.Update(ref state);
        _stateMachineLookup.Update(ref state);
        _conversationLookup.Update(ref state);
        _playerLookup.Update(ref state);
        _minionLookup.Update(ref state);
        _playerUnitBrainLookup.Update(ref state);

        FactionRegistry registry = SystemAPI.GetSingleton<FactionRegistry>();
        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        state.Dependency = new DrunkAwarenessJob
        {
            transformLookup       = _transformLookup,
            deadLookup            = _deadLookup,
            cutsceneActorLookup   = _cutsceneActorLookup,
            stateMachineLookup    = _stateMachineLookup,
            conversationLookup    = _conversationLookup,
            playerLookup          = _playerLookup,
            minionLookup          = _minionLookup,
            playerUnitBrainLookup = _playerUnitBrainLookup,
            factionEntities       = registry.entities,
            aiConfig              = brainLibrary.blob,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct DrunkAwarenessJob : IJobEntity
{
    private const float PASS_OUT_LEVEL        = 0.9f;
    private const float BRAWL_LEVEL           = 0.5f;
    private const float BRAWL_TEMPER          = 0.6f;
    private const float BRAWL_RANGE           = 4f;
    private const float BRAWL_UTILITY         = 0.95f;

    [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;
    [ReadOnly] public ComponentLookup<Dead>                    deadLookup;
    [ReadOnly] public ComponentLookup<CutsceneActor>           cutsceneActorLookup;
    [ReadOnly] public ComponentLookup<StateMachine>            stateMachineLookup;
    [ReadOnly] public ComponentLookup<Conversation>            conversationLookup;
    [ReadOnly] public ComponentLookup<Player>                  playerLookup;
    [ReadOnly] public ComponentLookup<Minion>                  minionLookup;
    [ReadOnly] public ComponentLookup<PlayerUnitBrain>         playerUnitBrainLookup;
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity> factionEntities;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>     aiConfig;

    // Intoxication is an enableable param, so only currently-intoxicated units are iterated.
    public void Execute(
        Entity                                 self,
        in UtilityBrain                        brain,
        in Intoxication                        intoxication,
        in Faction                             faction,
        in LocalTransform                      transform,
        in StateMachine                        stateMachine,
        in DynamicBuffer<PersonalityAttributes> personality,
        ref DynamicBuffer<UtilityActions>      actions)
    {
        if (intoxication.level >= PASS_OUT_LEVEL)
        {
            int passOutDefIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.PassOut);
            if (passOutDefIndex < 0) return;

            // Self-targeted: the behaviour never moves, it only needs a valid target entity.
            actions.Add(new UtilityActions
            {
                actionType     = ActionType.PassOut,
                actionDefIndex = passOutDefIndex,
                targetEntity   = self,
                priority       = AIPriorityTier.Critical,
                totalUtility   = 1f,
            });
            return;
        }

        if (intoxication.level <= BRAWL_LEVEL) return;
        if (intoxication.brawlCooldown > 0f) return;
        if (stateMachine.action.IsCombatAction()) return;
        if (PersonalityTraitLookup.Get(in personality, PersonalityTypes.Anger) <= BRAWL_TEMPER) return;

        int brawlDefIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.Brawl);
        if (brawlDefIndex < 0) return;

        Entity target = Entity.Null;
        if (conversationLookup.HasComponent(self) && conversationLookup.IsComponentEnabled(self))
        {
            Entity partner = conversationLookup[self].partner;
            if (IsValidBrawlTarget(partner))
                target = partner;
        }

        if (target == Entity.Null)
            target = FindNearestSameFactionTarget(self, faction, transform.Position);

        if (target == Entity.Null) return;

        actions.Add(new UtilityActions
        {
            actionType     = ActionType.Brawl,
            actionDefIndex = brawlDefIndex,
            targetEntity   = target,
            priority       = AIPriorityTier.Ambient,
            totalUtility   = BRAWL_UTILITY,
        });
    }

    private Entity FindNearestSameFactionTarget(Entity self, in Faction faction, float3 myPosition)
    {
        Entity nearest           = Entity.Null;
        float  nearestDistanceSq = BRAWL_RANGE * BRAWL_RANGE;

        byte factionKey = (byte)faction.factionType;
        if (!factionEntities.TryGetFirstValue(factionKey, out Entity candidate,
                out NativeParallelMultiHashMapIterator<byte> iterator))
            return Entity.Null;

        do
        {
            if (candidate == self) continue;
            if (!transformLookup.TryGetComponent(candidate, out LocalTransform candidateTransform)) continue;

            float distanceSq = math.distancesq(myPosition, candidateTransform.Position);
            if (distanceSq > nearestDistanceSq) continue;
            if (!IsValidBrawlTarget(candidate)) continue;

            nearestDistanceSq = distanceSq;
            nearest           = candidate;
        } while (factionEntities.TryGetNextValue(out candidate, ref iterator));

        return nearest;
    }

    private bool IsValidBrawlTarget(Entity candidate)
    {
        if (candidate == Entity.Null) return false;
        if (!transformLookup.HasComponent(candidate)) return false;
        if (deadLookup.HasComponent(candidate) && deadLookup.IsComponentEnabled(candidate)) return false;
        if (cutsceneActorLookup.HasComponent(candidate) && cutsceneActorLookup.IsComponentEnabled(candidate)) return false;
        if (playerLookup.HasComponent(candidate)) return false;
        if (minionLookup.HasComponent(candidate) && minionLookup.IsComponentEnabled(candidate)) return false;
        if (playerUnitBrainLookup.HasComponent(candidate) && playerUnitBrainLookup.IsComponentEnabled(candidate)) return false;
        if (stateMachineLookup.TryGetComponent(candidate, out StateMachine candidateStateMachine)
            && candidateStateMachine.action.IsCombatAction())
            return false;
        return true;
    }
}
