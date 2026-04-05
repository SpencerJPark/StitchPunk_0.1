using Unity.Burst;
using Unity.Entities;

// When a player-controlled minion body takes a hit, release PlayerControlled on
// its brain so the AI (SelfPreservation scoring) takes over and counter-attacks.
//
// Must run BEFORE DamageApplicationSystem — that system clears the Hurt buffer,
// so checking for non-empty hits here lets us catch the damage before it's consumed.
[BurstCompile]
[UpdateInGroup(typeof(CombatReactionSystemGroup))]
[UpdateBefore(typeof(DamageApplicationSystem))]
public partial struct MinionAutoCounterSystem : ISystem
{
    private ComponentLookup<PlayerControlled> playerControlledLookup;
    private ComponentLookup<NeedsAction>      needsActionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        playerControlledLookup = state.GetComponentLookup<PlayerControlled>(false);
        needsActionLookup      = state.GetComponentLookup<NeedsAction>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        playerControlledLookup.Update(ref state);
        needsActionLookup.Update(ref state);

        state.Dependency = new MinionAutoCounterJob
        {
            playerControlledLookup = playerControlledLookup,
            needsActionLookup      = needsActionLookup,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}

// Iterates minion bodies that are alive and have incoming hits.
// Cross-entity write to the brain is safe with Schedule (not ScheduleParallel)
// because each body links to a unique brain.
[BurstCompile]
[WithAll(typeof(Minion))]
[WithDisabled(typeof(Dead))]
public partial struct MinionAutoCounterJob : IJobEntity
{
    public ComponentLookup<PlayerControlled> playerControlledLookup;
    public ComponentLookup<NeedsAction>      needsActionLookup;

    public void Execute(RefRO<BrainLink> brainLink, in DynamicBuffer<Hurt> hurtBuffer)
    {
        if (hurtBuffer.Length == 0) return;

        Entity brain = brainLink.ValueRO.brain;
        if (brain == Entity.Null) return;

        if (!playerControlledLookup.HasComponent(brain))       return;
        if (!playerControlledLookup.IsComponentEnabled(brain)) return;

        // Release player control — AI takes over from here.
        playerControlledLookup.SetComponentEnabled(brain, false);

        // Wake the AI scoring pipeline so it reacts immediately.
        if (needsActionLookup.HasComponent(brain))
            needsActionLookup.SetComponentEnabled(brain, true);
    }
}
