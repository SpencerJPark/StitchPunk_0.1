using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Reads command event components from the player entity and fans orders out to
// every Selected + Minion body.
//
// Supported commands:
//   OnMinionMoveCommand     — move to world position
//   OnMinionInteractCommand — interact with target entity
//   OnMinionAttackCommand   — engage hostile; brain stays under player control even if hit
//   OnMinionDefendCommand   — hold position; auto-attack enemies within radius
//   OnMinionFollowCommand   — continuously path toward player position each frame
//
// Per body (on any new command):
//   • Enables  PlayerControlled on the brain (bypasses ActionSelectionSystem)
//   • Writes   PlayerOrder on the brain (destination + optional target + commandType)
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
        // Runs every frame for brains already in Follow mode (regardless of new command).
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
                foreach ((RefRO<BrainLink> brainLink,
                           RefRO<PathfindingAgent> pathfindingAgent,
                           RefRW<PathRequest> pathRequest,
                           EnabledRefRW<PathRequest> pathRequestEnabled,
                           RefRO<HordeMembership> hordeMembership,
                           Entity bodyEntity) in
                    SystemAPI.Query<
                        RefRO<BrainLink>,
                        RefRO<PathfindingAgent>,
                        RefRW<PathRequest>,
                        EnabledRefRW<PathRequest>,
                        RefRO<HordeMembership>>()
                    .WithAll<Minion>()
                    .WithPresent<PathRequest, HordeMembership>()
                    .WithEntityAccess())
                {
                    Entity brainEntity = brainLink.ValueRO.brain;
                    if (brainEntity == Entity.Null) continue;
                    if (!SystemAPI.IsComponentEnabled<PlayerControlled>(brainEntity)) continue;

                    PlayerOrder currentOrder = SystemAPI.GetComponent<PlayerOrder>(brainEntity);
                    if (currentOrder.commandType != CommandType.Follow) continue;

                    currentOrder.destination = playerPosition;
                    SystemAPI.SetComponent(brainEntity, currentOrder);

                    bool isInHorde = SystemAPI.IsComponentEnabled<HordeMembership>(bodyEntity);
                    if (!isInHorde)
                    {
                        pathRequest.ValueRW.targetPosition = playerPosition;
                        pathRequest.ValueRW.requestedMode  = pathfindingAgent.ValueRO.preferredMode;
                        pathRequestEnabled.ValueRW         = true;
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

        // ── Fan order to every Selected + Minion body ─────────────────────────
        // WithPresent<HordeMembership> includes both grouped (enabled) and
        // ungrouped (disabled) zombie bodies; all zombies have the component
        // after SwapBrainSystem runs.
        foreach ((RefRO<BrainLink> brainLink,
                   RefRO<PathfindingAgent> pathfindingAgent,
                   RefRW<PathRequest> pathRequest,
                   EnabledRefRW<PathRequest> pathRequestEnabled,
                   RefRO<HordeMembership> hordeMembership,
                   Entity bodyEntity) in
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
            Entity brainEntity = brainLink.ValueRO.brain;
            if (brainEntity == Entity.Null) continue;

            SystemAPI.SetComponentEnabled<PlayerControlled>(brainEntity, true);
            SystemAPI.SetComponent(brainEntity, new PlayerOrder
            {
                destination  = destination,
                targetEntity = orderTarget,
                commandType  = activeCommand,
            });
            SystemAPI.SetComponentEnabled<NeedsAction>(brainEntity, false);

            // Stamp commandType onto the body's Selected so SelectedVisualSystem
            // can update the ring color without needing to cross-reference the brain.
            if (selectedLookup.HasComponent(bodyEntity))
            {
                Selected selectedData   = selectedLookup[bodyEntity];
                selectedData.commandType = activeCommand;
                selectedLookup[bodyEntity] = selectedData;
            }

            bool isInHorde = SystemAPI.IsComponentEnabled<HordeMembership>(bodyEntity);
            if (isInHorde)
            {
                dirtyHordes.Add(hordeMembership.ValueRO.hordeEntity);
            }
            else
            {
                pathRequest.ValueRW.targetPosition = destination;
                pathRequest.ValueRW.requestedMode  = pathfindingAgent.ValueRO.preferredMode;
                pathRequestEnabled.ValueRW         = true;
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
