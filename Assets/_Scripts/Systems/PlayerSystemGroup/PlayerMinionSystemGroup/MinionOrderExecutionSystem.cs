using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

/// <summary>
/// Watches units under PlayerControlled and reacts when the minion has arrived
/// at its ordered destination or target.
///
/// Move commands  (PlayerOrder.targetEntity == Null):
///   Unit arrives within ARRIVE_RANGE of destination → release back to AI.
///
/// Interact commands (PlayerOrder.targetEntity != Null):
///   Target gone or dead        → release back to AI.
///   Unit arrives in INTERACT_RANGE → enable Attack + Target; attack system takes over.
///
/// All AI-state and combat components live on the same unit entity.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIActionSystemGroup))]
public partial struct MinionOrderExecutionSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Alive>          aliveLookup;

    private const float ARRIVE_RANGE   = 0.5f;
    private const float INTERACT_RANGE = 1.5f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        aliveLookup     = state.GetComponentLookup<Alive>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        aliveLookup.Update(ref state);

        state.Dependency = new MinionOrderExecutionJob
        {
            transformLookup = transformLookup,
            aliveLookup     = aliveLookup,
            arriveRangeSq   = ARRIVE_RANGE   * ARRIVE_RANGE,
            interactRangeSq = INTERACT_RANGE * INTERACT_RANGE,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}

[BurstCompile]
[WithAll(typeof(PlayerControlled))]
[WithPresent(typeof(AttackRequest), typeof(Target))]
public partial struct MinionOrderExecutionJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Alive>          aliveLookup;
    public            float                           arriveRangeSq;
    public            float                           interactRangeSq;

    public void Execute(
        in PlayerOrder                 order,
        in LocalTransform              transform,
        ref Target                     target,
        EnabledRefRW<AttackRequest>           attackEnabled,
        EnabledRefRW<Target>           targetEnabled,
        EnabledRefRW<PlayerControlled> playerControlledEnabled,
        EnabledRefRW<ActionRequest>      needsActionEnabled)
    {
        // Already executing an attack — attack system owns completion.
        if (attackEnabled.ValueRO)
            return;

        float3 unitPos = transform.Position;

        if (order.targetEntity != Entity.Null)
            HandleInteractOrder(unitPos, order.targetEntity,
                ref target, ref attackEnabled, ref targetEnabled,
                ref playerControlledEnabled, ref needsActionEnabled);
        else
            HandleMoveOrder(unitPos, order.destination,
                ref playerControlledEnabled, ref needsActionEnabled);
    }

    private void HandleInteractOrder(
        float3 unitPos,
        Entity orderTarget,
        ref Target target,
        ref EnabledRefRW<Attack> attackEnabled,
        ref EnabledRefRW<Target> targetEnabled,
        ref EnabledRefRW<PlayerControlled> playerControlledEnabled,
        ref EnabledRefRW<ActionRequest> needsActionEnabled)
    {
        bool targetAlive = aliveLookup.HasComponent(orderTarget) &&
                           aliveLookup.IsComponentEnabled(orderTarget);
        if (!targetAlive)
        {
            ReleaseBrain(ref playerControlledEnabled, ref needsActionEnabled);
            return;
        }

        if (!transformLookup.TryGetComponent(orderTarget, out LocalTransform targetTransform))
        {
            ReleaseBrain(ref playerControlledEnabled, ref needsActionEnabled);
            return;
        }

        float distSq = math.distancesq(unitPos, targetTransform.Position);
        if (distSq > interactRangeSq) return;

        // Arrived — hand off to attack execution system.
        target.entity         = orderTarget;
        targetEnabled.ValueRW = true;
        attackEnabled.ValueRW = true;
        // PlayerControlled stays enabled, NeedsAction stays disabled.
    }

    private void HandleMoveOrder(
        float3 unitPos,
        float3 destination,
        ref EnabledRefRW<PlayerControlled> playerControlledEnabled,
        ref EnabledRefRW<ActionRequest> needsActionEnabled)
    {
        float distSq = math.distancesq(unitPos, destination);
        if (distSq <= arriveRangeSq)
            ReleaseBrain(ref playerControlledEnabled, ref needsActionEnabled);
    }

    private static void ReleaseBrain(
        ref EnabledRefRW<PlayerControlled> playerControlledEnabled,
        ref EnabledRefRW<ActionRequest> needsActionEnabled)
    {
        playerControlledEnabled.ValueRW = false;
        needsActionEnabled.ValueRW      = true;
    }
}
