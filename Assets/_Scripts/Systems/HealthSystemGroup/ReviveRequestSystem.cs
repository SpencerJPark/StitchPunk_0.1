using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
public partial struct ReviveRequestSystem : ISystem
{
    private ComponentLookup<AIBrain>               aiBrainLookup;
    private ComponentLookup<ActionInterruptRequest> interruptLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        aiBrainLookup   = state.GetComponentLookup<AIBrain>(false);
        interruptLookup = state.GetComponentLookup<ActionInterruptRequest>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        aiBrainLookup.Update(ref state);
        interruptLookup.Update(ref state);

        state.Dependency = new ReviveJob
        {
            aiBrainLookup   = aiBrainLookup,
            interruptLookup = interruptLookup,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithDisabled(typeof(Alive))]
[WithPresent(typeof(Undead))]
public partial struct ReviveJob : IJobEntity
{
    public ComponentLookup<AIBrain>               aiBrainLookup;
    public ComponentLookup<ActionInterruptRequest> interruptLookup;

    public void Execute(
        Entity entity,
        ref Health health,
        EnabledRefRW<ReviveRequest> reviveEnabled,
        EnabledRefRW<Undead>        undeadEnabled,
        EnabledRefRW<Dead>          deadEnabled,
        EnabledRefRW<Alive>         aliveEnabled)
    {
        health.healthAmount   = health.healthAmountMax;
        reviveEnabled.ValueRW = false;
        undeadEnabled.ValueRW = true;
        deadEnabled.ValueRW   = false;
        aliveEnabled.ValueRW  = true;

        // Re-enable AI brain and fire an interrupt so the action system tears down
        // DeathAction and re-enters action selection on the next frame.
        if (aiBrainLookup.HasComponent(entity))
            aiBrainLookup.SetComponentEnabled(entity, true);
        if (interruptLookup.HasComponent(entity))
            interruptLookup.SetComponentEnabled(entity, true);
    }
}
