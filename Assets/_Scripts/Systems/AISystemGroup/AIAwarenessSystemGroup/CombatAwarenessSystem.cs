using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Detects hostile units within awareness range and heightens combat motivations.
///
/// Runs after MotivationDecaySystem (which resets contextMultiplier to 1.0) and after
/// FactionRegistrySystem (which rebuilds the faction map). This is the pre-pass for
/// SelfDefence and BloodLust motivations — setting their contextMultiplier so
/// MotivationScoringSystem scores ActionCategory.Attack high enough to win selection.
///
/// Per unit:
///   Hostile found → enable CombatTarget, set targetEntity to nearest hostile,
///                   set SelfDefence contextMultiplier = 3.0, BloodLust = 2.5,
///                   refresh AggressiveState with a 3s linger duration.
///   No hostiles   → disable CombatTarget.
///                   AggressiveState expires via UnitStateExpirySystem after 3s linger.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(FactionRegistrySystem))]
public partial struct CombatAwarenessSystem : ISystem
{
    private ComponentLookup<LocalTransform>  transformLookup;
    private ComponentLookup<Dead>            deadLookup;
    private ComponentLookup<CombatTarget>    combatTargetLookup;
    private ComponentLookup<AggressiveState> aggressiveLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<FactionRegistry>();

        transformLookup    = state.GetComponentLookup<LocalTransform>(true);
        deadLookup         = state.GetComponentLookup<Dead>(true);
        combatTargetLookup = state.GetComponentLookup<CombatTarget>(false);
        aggressiveLookup   = state.GetComponentLookup<AggressiveState>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        deadLookup.Update(ref state);
        combatTargetLookup.Update(ref state);
        aggressiveLookup.Update(ref state);

        FactionRegistry registry = SystemAPI.GetSingleton<FactionRegistry>();

        // Single-threaded: writable lookups for CombatTarget/AggressiveState.
        // Each entity only writes its own components, but DOTS safety requires single-thread
        // when lookup type matches the entity being iterated.
        state.Dependency = new CombatAwarenessJob
        {
            transformLookup    = transformLookup,
            deadLookup         = deadLookup,
            combatTargetLookup = combatTargetLookup,
            aggressiveLookup   = aggressiveLookup,
            factionEntities    = registry.entities,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain))]
[WithPresent(typeof(CombatTarget), typeof(AggressiveState))]
public partial struct CombatAwarenessJob : IJobEntity
{
    private const float AGGRESSIVE_LINGER = 3.0f;

    [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;
    [ReadOnly] public ComponentLookup<Dead>                    deadLookup;
    [NativeDisableParallelForRestriction]
    public            ComponentLookup<CombatTarget>            combatTargetLookup;
    [NativeDisableParallelForRestriction]
    public            ComponentLookup<AggressiveState>         aggressiveLookup;
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity> factionEntities;

    public void Execute(
        Entity                              self,
        in Faction                          faction,
        in Awareness                        awareness,
        in LocalTransform                   transform,
        in AttackData                       attackData,
        ref DynamicBuffer<ActionOption>     options,
        in DynamicBuffer<AttackFaction>     attackFactions,
        EnabledRefRO<NeedsAction>           needsAction)
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
            // Update combat target
            if (combatTargetLookup.HasComponent(self))
            {
                CombatTarget ct = combatTargetLookup[self];
                ct.targetEntity   = nearestHostile;
                ct.selectedAttack = attackData.attackType; // placeholder — multi-attack scorer will write here later
                combatTargetLookup[self] = ct;
                combatTargetLookup.SetComponentEnabled(self, true);
            }

            // Refresh aggressive state linger timer
            if (aggressiveLookup.HasComponent(self))
            {
                AggressiveState agg = aggressiveLookup[self];
                agg.duration = AGGRESSIVE_LINGER;
                aggressiveLookup[self] = agg;
                aggressiveLookup.SetComponentEnabled(self, true);
            }

            // Directly inject Attack option — bypasses scoring curves (no curve needed for combat).
            // Score 1000 ensures Attack wins against all other motivation-driven options.
            // targetEntity = nearestHostile so FilterPreviousEntity can distinguish targets and
            // doesn't filter this option on the initial selection (where selectedAction.targetEntity = Null).
            // CombatAttackExecutionSystem reads CombatTarget.targetEntity for execution anyway.
            if (needsAction.ValueRO)
            {
                options.Add(new ActionOption
                {
                    actionType = ActionType.Punch,
                    motivationType = MotivationType.BloodLust,
                    utilityScore        = 1000f,
                    interaction =  false,
                    targetEntity = nearestHostile,
                });
            }
        }
        else
        {
            // Only disable CombatTarget for units that actively scan for hostiles.
            // Units with no attackFactions rely on FightOrFlightSystem for reactive combat;
            // clearing their target here would cancel the fight-back response every frame.
            if (attackFactions.Length > 0 && combatTargetLookup.HasComponent(self))
                combatTargetLookup.SetComponentEnabled(self, false);
            // AggressiveState expires via UnitStateExpirySystem once its duration runs out
        }
    }

}
