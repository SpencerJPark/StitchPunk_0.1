using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Drives the sustained attack loop for player-commanded minion entities.
///
/// After MinionOrderExecutionSystem enables Attack + Target on a minion,
/// this system keeps the loop running:
///   • Re-enables Attack each tick once AttackCooldown reaches 0.
///   • Monitors the target for death — cleans up and releases back to AI.
///
/// AttackResolutionSystem (CombatResolutionSystemGroup) deals damage and
/// DISABLES Attack after each hit. This system re-enables it for the next hit.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct MinionAttackExecutionSystem : ISystem
{
    private ComponentLookup<Alive> aliveLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        aliveLookup = state.GetComponentLookup<Alive>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        aliveLookup.Update(ref state);

        state.Dependency = new MinionAttackExecutionJob
        {
            aliveLookup = aliveLookup,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}

[BurstCompile]
[WithAll(typeof(Minion), typeof(Target), typeof(PlayerControlled))]
[WithPresent(typeof(Attack), typeof(NeedsAction))]
public partial struct MinionAttackExecutionJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<Alive> aliveLookup;

    public void Execute(
        in AttackCooldown              cooldown,
        in Target                      target,
        EnabledRefRW<Attack>           attackEnabled,
        EnabledRefRW<Target>           targetEnabled,
        EnabledRefRW<PlayerControlled> playerControlledEnabled,
        EnabledRefRW<NeedsAction>      needsActionEnabled)
    {
        Entity targetEntity = target.entity;

        bool targetAlive = aliveLookup.HasComponent(targetEntity) &&
                           aliveLookup.IsComponentEnabled(targetEntity);

        if (!targetAlive)
        {
            targetEnabled.ValueRW           = false;
            attackEnabled.ValueRW           = false;
            playerControlledEnabled.ValueRW = false;
            needsActionEnabled.ValueRW      = true;
            return;
        }

        if (cooldown.timer <= 0f)
            attackEnabled.ValueRW = true;
    }
}
