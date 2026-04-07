using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

/// <summary>
/// Watches brains under PlayerControlled and reacts when the minion body
/// has arrived at its ordered destination or target.
///
/// Move commands  (PlayerOrder.targetEntity == Null):
///   Body arrives within ARRIVE_RANGE of destination → release brain back to AI.
///
/// Interact commands (PlayerOrder.targetEntity != Null):
///   Target gone or dead        → release brain back to AI.
///   Body arrives in INTERACT_RANGE:
///     Target alive             → enable Attack + Target on body; task execution takes over.
///     (Future: ResourceNode    → harvest; Item → pick up for transport.)
///     No matching task         → release brain back to AI.
///
/// Attack / harvest systems own the brain while their task runs
/// (PlayerControlled stays enabled, NeedsAction stays disabled).
/// They are responsible for releasing PlayerControlled when done.
///
/// Runs in AIExecutionSystemGroup after AISelectionSystemGroup so orders issued
/// this frame by MinionCommandSystem (PlayerEquipmentSystemGroup, before AISystemGroup)
/// are handled within the same tick.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct MinionOrderExecutionSystem : ISystem
{
    private ComponentLookup<LocalTransform>   transformLookup;
    private ComponentLookup<Alive>            aliveLookup;
    private ComponentLookup<Attack>           attackLookup;
    private ComponentLookup<Target>           targetLookup;
    private ComponentLookup<PlayerControlled> playerControlledLookup;
    private ComponentLookup<NeedsAction>      needsActionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        transformLookup        = state.GetComponentLookup<LocalTransform>(true);
        aliveLookup            = state.GetComponentLookup<Alive>(true);
        attackLookup           = state.GetComponentLookup<Attack>(false);
        targetLookup           = state.GetComponentLookup<Target>(false);
        playerControlledLookup = state.GetComponentLookup<PlayerControlled>(false);
        needsActionLookup      = state.GetComponentLookup<NeedsAction>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        aliveLookup.Update(ref state);
        attackLookup.Update(ref state);
        targetLookup.Update(ref state);
        playerControlledLookup.Update(ref state);
        needsActionLookup.Update(ref state);

        state.Dependency = new MinionOrderExecutionJob
        {
            transformLookup        = transformLookup,
            aliveLookup            = aliveLookup,
            attackLookup           = attackLookup,
            targetLookup           = targetLookup,
            playerControlledLookup = playerControlledLookup,
            needsActionLookup      = needsActionLookup,
            arriveRangeSq          = ARRIVE_RANGE  * ARRIVE_RANGE,
            interactRangeSq        = INTERACT_RANGE * INTERACT_RANGE,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }

    private const float ARRIVE_RANGE   = 0.5f;
    private const float INTERACT_RANGE = 1.5f;
}

// Iterates brain entities where PlayerControlled is enabled.
// Cross-entity reads (body transform, target alive) and writes (Attack/Target on body,
// PlayerControlled/NeedsAction on brain) are safe with Schedule (single-threaded).
[BurstCompile]
[WithAll(typeof(PlayerControlled))]
public partial struct MinionOrderExecutionJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform>   transformLookup;
    [ReadOnly] public ComponentLookup<Alive>            aliveLookup;
    public            ComponentLookup<Attack>           attackLookup;
    public            ComponentLookup<Target>           targetLookup;
    public            ComponentLookup<PlayerControlled> playerControlledLookup;
    public            ComponentLookup<NeedsAction>      needsActionLookup;
    public            float                             arriveRangeSq;
    public            float                             interactRangeSq;

    public void Execute(Entity brainEntity, in BodyLink bodyLink, in PlayerOrder order)
    {
        Entity body = bodyLink.body;
        if (body == Entity.Null) return;

        if (!transformLookup.TryGetComponent(body, out LocalTransform bodyTransform))
            return;

        float3 bodyPos = bodyTransform.Position;

        if (order.targetEntity != Entity.Null)
            HandleInteractOrder(brainEntity, body, bodyPos, order.targetEntity);
        else
            HandleMoveOrder(brainEntity, bodyPos, order.destination);
    }

    // ── INTERACT ─────────────────────────────────────────────────────────────

    private void HandleInteractOrder(Entity brain, Entity body, float3 bodyPos, Entity target)
    {
        // Body is already executing an attack — attack system owns completion.
        if (attackLookup.HasComponent(body) && attackLookup.IsComponentEnabled(body))
            return;

        // Target gone or dead → give up.
        bool targetAlive = aliveLookup.HasComponent(target) && aliveLookup.IsComponentEnabled(target);
        if (!targetAlive)
        {
            ReleaseBrain(brain);
            return;
        }

        if (!transformLookup.TryGetComponent(target, out LocalTransform targetTransform))
        {
            ReleaseBrain(brain);
            return;
        }

        // Still walking toward target — nothing to do yet.
        float distSq = math.distancesq(bodyPos, targetTransform.Position);
        if (distSq > interactRangeSq)
            return;

        // Arrived — route to task.
        // Target is alive → hand off to attack execution system.
        if (attackLookup.HasComponent(body) && targetLookup.HasComponent(body))
        {
            targetLookup[body] = new Target { entity = target };
            targetLookup.SetComponentEnabled(body, true);
            attackLookup.SetComponentEnabled(body, true);
            // PlayerControlled stays enabled, NeedsAction stays disabled.
            // AttackExecutionSystem will call ReleaseBrain when the target is dead.
        }
        else
        {
            // Body has no task components for this target type — fall back.
            ReleaseBrain(brain);
        }
    }

    // ── MOVE ─────────────────────────────────────────────────────────────────

    private void HandleMoveOrder(Entity brain, float3 bodyPos, float3 destination)
    {
        float distSq = math.distancesq(bodyPos, destination);
        if (distSq <= arriveRangeSq)
            ReleaseBrain(brain);
    }

    // ── HELPERS ──────────────────────────────────────────────────────────────

    private void ReleaseBrain(Entity brain)
    {
        if (playerControlledLookup.HasComponent(brain))
            playerControlledLookup.SetComponentEnabled(brain, false);

        if (needsActionLookup.HasComponent(brain))
            needsActionLookup.SetComponentEnabled(brain, true);
    }
}
