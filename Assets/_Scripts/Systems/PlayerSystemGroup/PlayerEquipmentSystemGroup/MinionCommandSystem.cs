using Unity.Burst;
using Unity.Collections;
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

        // Collect unique horde entities touched by this command so we can update
        // their shared target in one pass rather than once per member.
        NativeHashSet<Entity> dirtyHordes = new NativeHashSet<Entity>(8, Allocator.Temp);

        // ── Fan order to every Selected + Minion body ─────────────────────────
        // WithPresent<HordeMembership> includes both grouped (enabled) and
        // ungrouped (disabled) zombie bodies; all zombies have the component
        // after SwapBrainSystem runs.
        foreach (var (brainLink, agent, pathRequest, pathRequestEnabled, membership, entity) in
            SystemAPI.Query<
                RefRO<BrainLink>,
                RefRO<PathfindingAgent>,
                RefRW<PathRequest>,
                EnabledRefRW<PathRequest>,
                RefRO<HordeMembership>>()
            .WithAll<Selected, Minion>()
            .WithPresent<PathRequest, HordeMembership>()
            .WithEntityAccess())
        {
            Entity brain = brainLink.ValueRO.brain;
            if (brain == Entity.Null) continue;

            // Take control of the brain (same for all selected minions).
            SystemAPI.SetComponentEnabled<PlayerControlled>(brain, true);
            SystemAPI.SetComponent(brain, new PlayerOrder
            {
                destination  = destination,
                targetEntity = orderTarget,
            });
            SystemAPI.SetComponentEnabled<NeedsAction>(brain, false);

            bool inHorde = SystemAPI.IsComponentEnabled<HordeMembership>(entity);
            if (inHorde)
            {
                // HordeSystem will generate a shared flow field once we update the
                // horde target — do not issue an individual PathRequest here.
                dirtyHordes.Add(membership.ValueRO.hordeEntity);
            }
            else
            {
                // Ungrouped minion: individual pathfinding.
                pathRequest.ValueRW.targetPosition = destination;
                pathRequest.ValueRW.requestedMode  = agent.ValueRO.preferredMode;
                pathRequestEnabled.ValueRW = true;
            }
        }

        // ── Update each affected horde's target (triggers new flow field) ──────
        foreach (Entity hordeEntity in dirtyHordes)
        {
            if (!SystemAPI.HasComponent<Horde>(hordeEntity)) continue;
            Horde horde          = SystemAPI.GetComponent<Horde>(hordeEntity);
            horde.targetPosition = destination;
            horde.targetEntity   = orderTarget;
            horde.needsPathUpdate = true;
            SystemAPI.SetComponent(hordeEntity, horde);
        }

        dirtyHordes.Dispose();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
