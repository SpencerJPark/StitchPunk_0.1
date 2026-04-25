using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Reads command event components from the player entity and fans orders out to
// every Selected + Minion entity.
//
// Supported commands:
//   OnMinionMoveCommand     — move to world position
//   OnMinionInteractCommand — interact with target entity
//   OnMinionAttackCommand   — engage hostile; stays under player control even if hit
//   OnMinionDefendCommand   — hold position; auto-attack enemies within radius
//   OnMinionFollowCommand   — continuously path toward player position each frame
//
// Per unit (on any new command):
//   • Enables  PlayerControlled (bypasses ActionSelectionSystem)
//   • Writes   PlayerOrder (destination + optional target + commandType)
//   • Disables NeedsAction (prevents AI from picking a new action mid-command)
//   • Enables  PathRequest (kicks off pathfinding toward destination)
//
// In the single-entity model, PlayerControlled/PlayerOrder/NeedsAction/PathRequest
// all live on the same unit entity — no BrainLink cross-reference needed.
//
// Runs in PlayerEquipmentSystemGroup (OrderLast inside PlayerSystemGroup),
// so commands written by UnitSelectionManager this frame are acted on before
// AISystemGroup runs.
[BurstCompile]
[UpdateInGroup(typeof(PlayerEquipmentSystemGroup))]
public partial struct MinionCommandSystem : ISystem
{
    private ComponentLookup<Selected>        selectedLookup;
    private ComponentLookup<LocalTransform> localTransformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        selectedLookup       = state.GetComponentLookup<Selected>(false);
        localTransformLookup = state.GetComponentLookup<LocalTransform>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // ── Read commands off the player ──────────────────────────────────────
        bool        hasNewCommand   = false;
        CommandType activeCommand   = CommandType.Move;
        float3      destination     = float3.zero;
        Entity      orderTarget     = Entity.Null;

        foreach ((RefRO<OnMinionMoveCommand> moveCommand, EnabledRefRW<OnMinionMoveCommand> moveCommandEnabled) in
            SystemAPI.Query<RefRO<OnMinionMoveCommand>, EnabledRefRW<OnMinionMoveCommand>>()
            .WithAll<Player>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!moveCommandEnabled.ValueRO) continue;
            hasNewCommand           = true;
            activeCommand           = CommandType.Move;
            destination             = moveCommand.ValueRO.destination;
            moveCommandEnabled.ValueRW = false;
        }

        foreach ((RefRO<OnMinionInteractCommand> interactCommand, EnabledRefRW<OnMinionInteractCommand> interactCommandEnabled) in
            SystemAPI.Query<RefRO<OnMinionInteractCommand>, EnabledRefRW<OnMinionInteractCommand>>()
            .WithAll<Player>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!interactCommandEnabled.ValueRO) continue;
            hasNewCommand           = true;
            activeCommand           = CommandType.Interact;
            orderTarget             = interactCommand.ValueRO.targetEntity;
            if (orderTarget != Entity.Null && SystemAPI.HasComponent<LocalTransform>(orderTarget))
                destination = SystemAPI.GetComponent<LocalTransform>(orderTarget).Position;
            interactCommandEnabled.ValueRW = false;
        }

        foreach ((RefRO<OnMinionAttackCommand> attackCommand, EnabledRefRW<OnMinionAttackCommand> attackCommandEnabled) in
            SystemAPI.Query<RefRO<OnMinionAttackCommand>, EnabledRefRW<OnMinionAttackCommand>>()
            .WithAll<Player>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!attackCommandEnabled.ValueRO) continue;
            hasNewCommand           = true;
            activeCommand           = CommandType.Attack;
            orderTarget             = attackCommand.ValueRO.targetEntity;
            if (orderTarget != Entity.Null && SystemAPI.HasComponent<LocalTransform>(orderTarget))
                destination = SystemAPI.GetComponent<LocalTransform>(orderTarget).Position;
            attackCommandEnabled.ValueRW = false;
        }

        foreach ((RefRO<OnMinionDefendCommand> defendCommand, EnabledRefRW<OnMinionDefendCommand> defendCommandEnabled) in
            SystemAPI.Query<RefRO<OnMinionDefendCommand>, EnabledRefRW<OnMinionDefendCommand>>()
            .WithAll<Player>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!defendCommandEnabled.ValueRO) continue;
            hasNewCommand           = true;
            activeCommand           = CommandType.Defend;
            destination             = defendCommand.ValueRO.position;
            defendCommandEnabled.ValueRW = false;
        }

        foreach ((RefRO<OnMinionFollowCommand> followCommand, EnabledRefRW<OnMinionFollowCommand> followCommandEnabled) in
            SystemAPI.Query<RefRO<OnMinionFollowCommand>, EnabledRefRW<OnMinionFollowCommand>>()
            .WithAll<Player>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!followCommandEnabled.ValueRO) continue;
            hasNewCommand               = true;
            activeCommand               = CommandType.Follow;
            followCommandEnabled.ValueRW = false;
        }

        selectedLookup.Update(ref state);
        localTransformLookup.Update(ref state);

        // ── Continuous follow: update destination to current player position ───
        // Runs every frame for units already in Follow mode (regardless of new command).
        if (!hasNewCommand || activeCommand != CommandType.Follow)
        {
            float3 playerPosition  = float3.zero;
            bool   foundPlayer     = false;
            foreach (RefRO<LocalTransform> playerTransform in
                SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Player>())
            {
                playerPosition = playerTransform.ValueRO.Position;
                foundPlayer    = true;
            }

            if (foundPlayer)
            {
                foreach ((RefRW<PathRequest> pathRequest,
                           EnabledRefRW<PathRequest> pathRequestEnabled,
                           RefRW<Movement> movement,
                           RefRO<HordeMembership> hordeMembership,
                           Entity unitEntity) in
                    SystemAPI.Query<
                        RefRW<PathRequest>,
                        EnabledRefRW<PathRequest>,
                        RefRW<Movement>,
                        RefRO<HordeMembership>>()
                    .WithAll<Minion>()
                    .WithPresent<PathRequest, HordeMembership, PlayerControlled>()
                    .WithEntityAccess())
                {
                    if (!SystemAPI.IsComponentEnabled<PlayerControlled>(unitEntity)) continue;

                    PlayerOrder currentOrder = SystemAPI.GetComponent<PlayerOrder>(unitEntity);
                    if (currentOrder.commandType != CommandType.Follow) continue;

                    currentOrder.destination = playerPosition;
                    SystemAPI.SetComponent(unitEntity, currentOrder);

                    bool isInHorde = SystemAPI.IsComponentEnabled<HordeMembership>(unitEntity);
                    if (!isInHorde)
                    {
                        AIUtils.BeginPathRequest(ref pathRequest.ValueRW, pathRequestEnabled, playerPosition);
                    }
                }
            }
        }

        if (!hasNewCommand) return;

        // For follow commands, resolve destination as the current player position.
        if (activeCommand == CommandType.Follow)
        {
            foreach (RefRO<LocalTransform> playerTransform in
                SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Player>())
            {
                destination = playerTransform.ValueRO.Position;
            }
        }

        // Collect unique horde entities touched by this command so we update
        // their shared target in one pass rather than once per member.
        NativeHashSet<Entity> dirtyHordes = new NativeHashSet<Entity>(8, Allocator.Temp);

        // ── Fan order to every Selected + Minion entity ───────────────────────
        // WithPresent<HordeMembership> includes both grouped (enabled) and
        // ungrouped (disabled) zombie entities.
        foreach ((RefRW<PathfindingAgent> pathfindingAgent,
                   RefRW<PathRequest> pathRequest,
                   EnabledRefRW<PathRequest> pathRequestEnabled,
                   RefRW<Movement> movement,
                   RefRO<HordeMembership> hordeMembership,
                   Entity unitEntity) in
            SystemAPI.Query<
                RefRW<PathfindingAgent>,
                RefRW<PathRequest>,
                EnabledRefRW<PathRequest>,
                RefRW<Movement>,
                RefRO<HordeMembership>>()
            .WithAll<Selected, Minion>()
            .WithPresent<PathRequest, HordeMembership>()
            .WithEntityAccess())
        {
            SystemAPI.SetComponentEnabled<PlayerControlled>(unitEntity, true);
            SystemAPI.SetComponent(unitEntity, new PlayerOrder
            {
                destination  = destination,
                targetEntity = orderTarget,
                commandType  = activeCommand,
            });
            SystemAPI.SetComponentEnabled<NeedsAction>(unitEntity, false);

            // Stamp commandType onto Selected so SelectedVisualSystem can update
            // the ring color without a cross-reference.
            if (selectedLookup.HasComponent(unitEntity))
            {
                Selected selectedData    = selectedLookup[unitEntity];
                selectedData.commandType = activeCommand;
                selectedLookup[unitEntity] = selectedData;
            }

            bool isInHorde = SystemAPI.IsComponentEnabled<HordeMembership>(unitEntity);
            if (isInHorde)
            {
                dirtyHordes.Add(hordeMembership.ValueRO.hordeEntity);
            }
            else
            {
                AIUtils.BeginPathRequest(ref pathRequest.ValueRW, pathRequestEnabled, destination);
            }
        }

        // ── Update each affected horde's target and move its destination marker ──
        foreach (Entity hordeEntity in dirtyHordes)
        {
            if (!SystemAPI.HasComponent<Horde>(hordeEntity)) continue;
            Horde hordeData           = SystemAPI.GetComponent<Horde>(hordeEntity);
            hordeData.targetPosition  = destination;
            hordeData.targetEntity    = orderTarget;
            hordeData.needsPathUpdate = true;
            SystemAPI.SetComponent(hordeEntity, hordeData);

            // Snap the horde's marker scene entity to the destination and show it.
            Entity markerEntity = hordeData.markerEntity;
            if (markerEntity != Entity.Null && localTransformLookup.HasComponent(markerEntity))
            {
                LocalTransform markerTransform   = localTransformLookup[markerEntity];
                markerTransform.Position         = destination;
                markerTransform.Scale            = 1f;
                localTransformLookup[markerEntity] = markerTransform;
            }
        }

        dirtyHordes.Dispose();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
