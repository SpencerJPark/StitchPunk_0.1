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
///   Unit arrives in INTERACT_RANGE:
///     Target alive             → enable Attack + Target; attack system takes over.
///     No matching task         → release back to AI.
///
/// In the single-entity model, PlayerControlled/NeedsAction/PlayerOrder/LocalTransform/
/// Attack/Target all live on the same unit entity — no BodyLink lookup needed.
///
/// Runs in AIExecutionSystemGroup after AISelectionSystemGroup so orders issued
/// this frame by MinionCommandSystem are handled within the same tick.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct MinionOrderExecutionSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Alive>          aliveLookup;
    private ComponentLookup<Attack>         attackLookup;
    private ComponentLookup<Target>         targetLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        aliveLookup     = state.GetComponentLookup<Alive>(true);
        attackLookup    = state.GetComponentLookup<Attack>(false);
        targetLookup    = state.GetComponentLookup<Target>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        aliveLookup.Update(ref state);
        attackLookup.Update(ref state);
        targetLookup.Update(ref state);

        state.Dependency = new MinionOrderExecutionJob
        {
            transformLookup = transformLookup,
            aliveLookup     = aliveLookup,
            attackLookup    = attackLookup,
            targetLookup    = targetLookup,
            arriveRangeSq   = ARRIVE_RANGE   * ARRIVE_RANGE,
            interactRangeSq = INTERACT_RANGE * INTERACT_RANGE,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }

    private const float ARRIVE_RANGE   = 0.5f;
    private const float INTERACT_RANGE = 1.5f;
}

// Iterates unit entities where PlayerControlled is enabled.
// All AI-state components (PlayerControlled, NeedsAction, PlayerOrder, LocalTransform)
// are on the same entity. Attack/Target are accessed via lookup because not all
// player-controlled units are guaranteed to have combat components.
[BurstCompile]
[WithAll(typeof(PlayerControlled))]
public partial struct MinionOrderExecutionJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Alive>          aliveLookup;
    public            ComponentLookup<Attack>         attackLookup;
    public            ComponentLookup<Target>         targetLookup;
    public            float                           arriveRangeSq;
    public            float                           interactRangeSq;

    public void Execute(
        Entity entity,
        in PlayerOrder order,
        in LocalTransform transform,
        EnabledRefRW<PlayerControlled> playerControlledEnabled,
        EnabledRefRW<NeedsAction> needsActionEnabled)
    {
        // Already executing an attack — attack system owns completion.
        if (attackLookup.HasComponent(entity) && attackLookup.IsComponentEnabled(entity))
            return;

        float3 unitPos = transform.Position;

        if (order.targetEntity != Entity.Null)
            HandleInteractOrder(entity, unitPos, order.targetEntity,
                ref playerControlledEnabled, ref needsActionEnabled);
        else
            HandleMoveOrder(unitPos, order.destination,
                ref playerControlledEnabled, ref needsActionEnabled);
    }

    // ── INTERACT ─────────────────────────────────────────────────────────────

    private void HandleInteractOrder(
        Entity entity,
        float3 unitPos,
        Entity orderTarget,
        ref EnabledRefRW<PlayerControlled> playerControlledEnabled,
        ref EnabledRefRW<NeedsAction> needsActionEnabled)
    {
        // Target gone or dead → give up.
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

        // Still walking toward target — nothing to do yet.
        float distSq = math.distancesq(unitPos, targetTransform.Position);
        if (distSq > interactRangeSq) return;

        // Arrived — target is alive → hand off to attack execution system.
        if (attackLookup.HasComponent(entity) && targetLookup.HasComponent(entity))
        {
            targetLookup[entity] = new Target { entity = orderTarget };
            targetLookup.SetComponentEnabled(entity, true);
            attackLookup.SetComponentEnabled(entity, true);
            // PlayerControlled stays enabled, NeedsAction stays disabled.
            // MinionAttackExecutionSystem releases when the target is dead.
        }
        else
        {
            // No combat components on this entity — fall back.
            ReleaseBrain(ref playerControlledEnabled, ref needsActionEnabled);
        }
    }

    // ── MOVE ─────────────────────────────────────────────────────────────────

    private void HandleMoveOrder(
        float3 unitPos,
        float3 destination,
        ref EnabledRefRW<PlayerControlled> playerControlledEnabled,
        ref EnabledRefRW<NeedsAction> needsActionEnabled)
    {
        float distSq = math.distancesq(unitPos, destination);
        if (distSq <= arriveRangeSq)
            ReleaseBrain(ref playerControlledEnabled, ref needsActionEnabled);
    }

    // ── HELPERS ──────────────────────────────────────────────────────────────

    private static void ReleaseBrain(
        ref EnabledRefRW<PlayerControlled> playerControlledEnabled,
        ref EnabledRefRW<NeedsAction> needsActionEnabled)
    {
        playerControlledEnabled.ValueRW = false;
        needsActionEnabled.ValueRW      = true;
    }
}
