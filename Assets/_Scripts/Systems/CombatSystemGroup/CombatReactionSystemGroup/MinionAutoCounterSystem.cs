using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

// When a player-controlled minion takes a hit, release PlayerControlled so the
// AI (SelfPreservation scoring) takes over and counter-attacks.
//
// Must run BEFORE DamageApplicationSystem — that system clears the Hurt buffer,
// so checking for non-empty hits here lets us catch the damage before it's consumed.
[BurstCompile]
[UpdateInGroup(typeof(CombatReactionSystemGroup))]
[UpdateBefore(typeof(DamageApplicationSystem))]
public partial struct MinionAutoCounterSystem : ISystem
{
    private ComponentLookup<PlayerOrder> playerOrderLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        playerOrderLookup = state.GetComponentLookup<PlayerOrder>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        playerOrderLookup.Update(ref state);

        state.Dependency = new MinionAutoCounterJob
        {
            playerOrderLookup = playerOrderLookup,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}

// Iterates minion entities that are alive and have incoming hits.
// In the single-entity model, PlayerControlled and NeedsAction live directly
// on the unit entity — no cross-entity BrainLink lookup needed.
[BurstCompile]
[WithAll(typeof(Minion))]
[WithDisabled(typeof(Dead))]
[WithPresent(typeof(PlayerControlled))]
[WithPresent(typeof(NeedsAction))]
public partial struct MinionAutoCounterJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<PlayerOrder> playerOrderLookup;

    public void Execute(
        Entity entity,
        in DynamicBuffer<Hurt> hurtBuffer,
        EnabledRefRW<PlayerControlled> playerControlledEnabled,
        EnabledRefRW<NeedsAction> needsActionEnabled)
    {
        if (hurtBuffer.Length == 0) return;
        if (!playerControlledEnabled.ValueRO) return;

        // Attack orders intentionally keep control even while taking damage —
        // the minion is supposed to fight through it.
        if (playerOrderLookup.HasComponent(entity) &&
            playerOrderLookup[entity].commandType == CommandType.Attack)
            return;

        // Release player control — AI takes over and counter-attacks.
        playerControlledEnabled.ValueRW = false;

        // Wake the AI scoring pipeline so it reacts immediately.
        needsActionEnabled.ValueRW = true;
    }
}
