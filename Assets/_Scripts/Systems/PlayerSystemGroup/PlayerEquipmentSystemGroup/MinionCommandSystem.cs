using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Reads OnMinionMoveCommand / OnMinionInteractCommand from the player entity and
// fans the order out to every Selected + Minion body.
//
// Per body:
//   • Enables  PlayerControlled on the brain (bypasses ActionSelectionSystem)
//   • Writes   PlayerOrder on the brain (destination + optional target)
//   • Disables NeedsAction on the brain (prevents AI from picking a new action mid-command)
//   • Enables  PathRequest on the body  (kicks off pathfinding toward destination)
//
// Runs in PlayerEquipmentSystemGroup (OrderLast inside PlayerSystemGroup),
// so commands written by UnitSelectionManager this frame are acted on before
// AISystemGroup runs.
[BurstCompile]
[UpdateInGroup(typeof(PlayerEquipmentSystemGroup))]
public partial struct MinionCommandSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // ── Read commands off the player ──────────────────────────────────────
        bool   hasMoveCmd      = false;
        bool   hasInteractCmd  = false;
        float3 destination     = float3.zero;
        Entity interactTarget  = Entity.Null;

        foreach (var (moveCmd, moveCmdEnabled) in
            SystemAPI.Query<RefRO<OnMinionMoveCommand>, EnabledRefRW<OnMinionMoveCommand>>()
            .WithAll<Player>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!moveCmdEnabled.ValueRO) continue;
            hasMoveCmd        = true;
            destination       = moveCmd.ValueRO.destination;
            moveCmdEnabled.ValueRW = false;
        }

        foreach (var (interactCmd, interactCmdEnabled) in
            SystemAPI.Query<RefRO<OnMinionInteractCommand>, EnabledRefRW<OnMinionInteractCommand>>()
            .WithAll<Player>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!interactCmdEnabled.ValueRO) continue;
            hasInteractCmd        = true;
            interactTarget        = interactCmd.ValueRO.targetEntity;
            interactCmdEnabled.ValueRW = false;
        }

        if (!hasMoveCmd && !hasInteractCmd) return;

        // For interact commands use the target's world position as path destination.
        if (hasInteractCmd && interactTarget != Entity.Null
            && SystemAPI.HasComponent<LocalTransform>(interactTarget))
        {
            destination = SystemAPI.GetComponent<LocalTransform>(interactTarget).Position;
        }

        Entity orderTarget = hasInteractCmd ? interactTarget : Entity.Null;

        // ── Fan order to every Selected + Minion body ─────────────────────────
        foreach (var (brainLink, agent, pathRequest, pathRequestEnabled) in
            SystemAPI.Query<
                RefRO<BrainLink>,
                RefRO<PathfindingAgent>,
                RefRW<PathRequest>,
                EnabledRefRW<PathRequest>>()
            .WithAll<Selected, Minion>()
            .WithPresent<PathRequest>())
        {
            Entity brain = brainLink.ValueRO.brain;
            if (brain == Entity.Null) continue;

            // Take control of the brain.
            SystemAPI.SetComponentEnabled<PlayerControlled>(brain, true);
            SystemAPI.SetComponent(brain, new PlayerOrder
            {
                destination  = destination,
                targetEntity = orderTarget,
            });
            // Prevent the AI from overwriting SelectedAction while player-controlled.
            SystemAPI.SetComponentEnabled<NeedsAction>(brain, false);

            // Start pathfinding on the body.
            pathRequest.ValueRW.targetPosition = destination;
            pathRequest.ValueRW.requestedMode  = agent.ValueRO.preferredMode;
            pathRequestEnabled.ValueRW = true;
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
