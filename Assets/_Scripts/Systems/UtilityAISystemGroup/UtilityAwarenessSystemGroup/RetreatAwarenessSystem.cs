using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// A badly hurt, outnumbered unit that is not a coward walks back to the nearest ally who is safer than it is, else to its guard post.
// Reaching allies ends "outnumbered", which is the hysteresis: the unit stops retreating once it stands among them.
[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
[UpdateAfter(typeof(EnemyAwarenessSystem))]
[UpdateAfter(typeof(SelfDefenceAwarenessSystem))]
[UpdateAfter(typeof(FleeAwarenessSystem))]
[UpdateBefore(typeof(GuardPostAwarenessSystem))]
public partial struct RetreatAwarenessSystem : ISystem
{
    public const int   RETREAT_PRIORITY     = AIPriorityTier.Critical;
    public const float RETREAT_HEALTH_RATIO = 0.3f;
    public const float OUTNUMBERED_RADIUS   = 10f;
    public const float SAFER_BY_METERS      = 3f;
    public const int   RETREAT_SLOT_COUNT   = 6;

    private UnitStateLookups unitStateLookups;
    private ComponentLookup<GuardPost>      guardPostLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FactionRegistry>();
        state.RequireForUpdate<BrainLibrary>();

        unitStateLookups = UnitStateLookups.Create(ref state);
        guardPostLookup = state.GetComponentLookup<GuardPost>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        unitStateLookups.Update(ref state);
        guardPostLookup.Update(ref state);

        FactionRegistry registry     = SystemAPI.GetSingleton<FactionRegistry>();
        BrainLibrary    brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        state.Dependency = new RetreatAwarenessJob
        {
            unitStateLookups = unitStateLookups,
            guardPostLookup = guardPostLookup,
            factionEntities = registry.entities,
            aiConfig        = brainLibrary.blob,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct RetreatAwarenessJob : IJobEntity
{
    public UnitStateLookups unitStateLookups;
    [ReadOnly] public ComponentLookup<GuardPost>               guardPostLookup;
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity> factionEntities;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>     aiConfig;

    public void Execute(
        Entity self,
        in UtilityBrain                         brain,
        in Awareness                            awareness,
        in LocalTransform                       transform,
        in Health                               health,
        in StateMachine                         stateMachine,
        in Faction                              faction,
        in DynamicBuffer<PersonalityAttributes> personality,
        in DynamicBuffer<ThreatEntry>           threats,
        in DynamicBuffer<AttackFaction>         attackFactions,
        ref DynamicBuffer<UtilityActions>       actions)
    {
        if (stateMachine.action == ActionType.Retreat) return;

        float healthRatio = health.healthAmountMax > 0
            ? (float)health.healthAmount / health.healthAmountMax
            : 1f;
        if (healthRatio >= RetreatAwarenessSystem.RETREAT_HEALTH_RATIO) return;

        bool inDanger = threats.Length > 0;
        for (int optionIndex = 0; optionIndex < actions.Length && !inDanger; optionIndex++)
        {
            ActionType optionType = actions[optionIndex].actionType;
            inDanger = optionType.IsCombatAction() && optionType != ActionType.Flee;
        }
        if (!inDanger) return;

        float bravery = 0.5f; // same default as ConsiderationScoringSystem.GetTraitValue
        for (int personalityIndex = 0; personalityIndex < personality.Length; personalityIndex++)
        {
            if (personality[personalityIndex].personality != PersonalityTypes.Bravery) continue;
            bravery = math.saturate(personality[personalityIndex].value);
            break;
        }

        // Cowards flee instead, so two Critical options never compete.
        if (FleeAwarenessSystem.WouldBreakOffToFlee(healthRatio, bravery)) return;

        int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.Retreat);
        if (defIndex < 0) return;

        float3 myPos        = transform.Position;
        float  outnumberedSq = RetreatAwarenessSystem.OUTNUMBERED_RADIUS * RetreatAwarenessSystem.OUTNUMBERED_RADIUS;

        int    hostileCount    = 0;
        float  nearestHostileDistance = float.MaxValue;
        float3 nearestHostilePosition = float3.zero;
        bool   foundHostile    = false;

        for (int attackFactionIndex = 0; attackFactionIndex < attackFactions.Length; attackFactionIndex++)
        {
            byte factionKey = (byte)attackFactions[attackFactionIndex].faction;

            if (!factionEntities.TryGetFirstValue(factionKey, out Entity candidate,
                    out NativeParallelMultiHashMapIterator<byte> iterator))
                continue;

            do
            {
                if (candidate == self) continue;
                if (!unitStateLookups.TryGetTransformUnlessDead(candidate, out LocalTransform candidateTransform)) continue;

                float distance = math.distance(myPos, candidateTransform.Position);
                if (distance * distance <= outnumberedSq)
                    hostileCount++;

                if (distance <= awareness.range && distance < nearestHostileDistance)
                {
                    nearestHostileDistance = distance;
                    nearestHostilePosition = candidateTransform.Position;
                    foundHostile           = true;
                }
            } while (factionEntities.TryGetNextValue(out candidate, ref iterator));
        }

        if (!foundHostile) return;

        int    allyCount             = 0;
        bool   foundRetreatCandidate = false;
        float  candidateDistanceToSelf = float.MaxValue;
        float3 retreatCandidatePosition = float3.zero;

        if (factionEntities.TryGetFirstValue((byte)faction.factionType, out Entity ally,
                out NativeParallelMultiHashMapIterator<byte> allyIterator))
        {
            do
            {
                if (ally == self) continue;
                if (!unitStateLookups.TryGetTransformUnlessDead(ally, out LocalTransform allyTransform)) continue;

                float distanceToSelf = math.distance(myPos, allyTransform.Position);
                if (distanceToSelf * distanceToSelf <= outnumberedSq)
                    allyCount++;

                if (distanceToSelf > awareness.range || distanceToSelf >= candidateDistanceToSelf) continue;

                float allyDistanceToHostile = math.distance(allyTransform.Position, nearestHostilePosition);
                if (allyDistanceToHostile < nearestHostileDistance + RetreatAwarenessSystem.SAFER_BY_METERS) continue;

                candidateDistanceToSelf  = distanceToSelf;
                retreatCandidatePosition = allyTransform.Position;
                foundRetreatCandidate    = true;
            } while (factionEntities.TryGetNextValue(out ally, ref allyIterator));
        }

        if (hostileCount <= allyCount) return;

        float3 destination;
        if (foundRetreatCandidate)
        {
            int slotIndex = 1 + (int)((uint)self.Index % (uint)RetreatAwarenessSystem.RETREAT_SLOT_COUNT);
            destination = retreatCandidatePosition
                + FormationSlots.RoundedSlotOffset(slotIndex, FormationSlots.DefaultSpacing);
        }
        else if (guardPostLookup.TryGetComponent(self, out GuardPost guardPost)
                 && math.distance(guardPost.anchorPosition, nearestHostilePosition) > nearestHostileDistance)
        {
            destination = guardPost.anchorPosition;
        }
        else
        {
            return;
        }

        actions.Add(new UtilityActions
        {
            actionType        = ActionType.Retreat,
            actionDefIndex    = defIndex,
            priority          = RetreatAwarenessSystem.RETREAT_PRIORITY,
            targetPosition    = destination,
            hasTargetPosition = true,
        });
    }
}
