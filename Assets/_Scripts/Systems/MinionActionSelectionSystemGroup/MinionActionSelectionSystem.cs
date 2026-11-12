using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Runs after UtilityAwarenessSystemGroup cleared the buffer. isPlayerOrdered makes WinnerSelectionSystem skip scoring.
// Commands are one-shot: re-emitting each frame would re-preempt under WinnerSelection's player-retarget exemption.
[BurstCompile]
[UpdateInGroup(typeof(MinionActionSelectionSystemGroup))]
public partial struct MinionActionSelectionSystem : ISystem
{
    private ComponentLookup<OnMinionMoveCommand>     _moveCmdLookup;
    private ComponentLookup<OnMinionAttackCommand>   _attackCmdLookup;
    private ComponentLookup<OnMinionInteractCommand> _interactCmdLookup;
    private ComponentLookup<OnMinionFollowCommand>   _followCmdLookup;
    private ComponentLookup<OnMinionStopCommand>     _stopCmdLookup;
    private ComponentLookup<OnMinionReturnCommand>   _returnCmdLookup;
    private ComponentLookup<ActionInterruptRequest>  _interruptRequestLookup;
    private ComponentLookup<MinionStandingOrder>     _standingOrderLookup;
    private ComponentLookup<MinionGatherOrder>       _gatherOrderLookup;
    private BufferLookup<AvailableAttack>            _availableAttackLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BrainLibrary>();
        _moveCmdLookup           = state.GetComponentLookup<OnMinionMoveCommand>(false);
        _attackCmdLookup         = state.GetComponentLookup<OnMinionAttackCommand>(false);
        _interactCmdLookup       = state.GetComponentLookup<OnMinionInteractCommand>(false);
        _followCmdLookup         = state.GetComponentLookup<OnMinionFollowCommand>(false);
        _stopCmdLookup           = state.GetComponentLookup<OnMinionStopCommand>(false);
        _returnCmdLookup         = state.GetComponentLookup<OnMinionReturnCommand>(false);
        _interruptRequestLookup  = state.GetComponentLookup<ActionInterruptRequest>(false);
        _standingOrderLookup     = state.GetComponentLookup<MinionStandingOrder>(false);
        _gatherOrderLookup       = state.GetComponentLookup<MinionGatherOrder>(false);
        _availableAttackLookup   = state.GetBufferLookup<AvailableAttack>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<Player>()) return;

        _moveCmdLookup.Update(ref state);
        _attackCmdLookup.Update(ref state);
        _interactCmdLookup.Update(ref state);
        _followCmdLookup.Update(ref state);
        _stopCmdLookup.Update(ref state);
        _returnCmdLookup.Update(ref state);
        _interruptRequestLookup.Update(ref state);
        _standingOrderLookup.Update(ref state);
        _gatherOrderLookup.Update(ref state);
        _availableAttackLookup.Update(ref state);

        Entity       playerEntity   = SystemAPI.GetSingletonEntity<Player>();
        BrainLibrary brainLibrary   = SystemAPI.GetSingleton<BrainLibrary>();
        float3       playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.AI) != 0;

        EntityCommandBuffer ecb = loggingEnabled
            ? SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
            : default;

        state.Dependency = new MinionActionWriteJob
        {
            aiConfig               = brainLibrary.blob,
            playerEntity           = playerEntity,
            playerPosition         = playerPosition,
            moveCmdLookup           = _moveCmdLookup,
            attackCmdLookup         = _attackCmdLookup,
            interactCmdLookup       = _interactCmdLookup,
            followCmdLookup         = _followCmdLookup,
            stopCmdLookup           = _stopCmdLookup,
            returnCmdLookup         = _returnCmdLookup,
            interruptRequestLookup  = _interruptRequestLookup,
            standingOrderLookup     = _standingOrderLookup,
            gatherOrderLookup       = _gatherOrderLookup,
            availableAttackLookup   = _availableAttackLookup,
            ecb                     = ecb,
            loggingEnabled          = loggingEnabled,
            timestamp               = SystemAPI.Time.ElapsedTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
// WithPresent(UtilityBrain) keeps brain.unitType readable while UtilityBrain is disabled on player minions.
[WithAll(typeof(PlayerUnitBrain))]
[WithPresent(typeof(UtilityBrain))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct MinionActionWriteJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>    aiConfig;
    [ReadOnly] public BufferLookup<AvailableAttack>           availableAttackLookup;
    // Read-write: commands are disabled once translated.
    public ComponentLookup<OnMinionMoveCommand>     moveCmdLookup;
    public ComponentLookup<OnMinionAttackCommand>   attackCmdLookup;
    public ComponentLookup<OnMinionInteractCommand> interactCmdLookup;
    public ComponentLookup<OnMinionFollowCommand>   followCmdLookup;
    public ComponentLookup<OnMinionStopCommand>     stopCmdLookup;
    public ComponentLookup<OnMinionReturnCommand>   returnCmdLookup;
    public ComponentLookup<ActionInterruptRequest>  interruptRequestLookup;
    // Any explicit order replaces a standing Follow/Hold (MinionStandingOrderSystem runs after this).
    public ComponentLookup<MinionStandingOrder>     standingOrderLookup;
    public ComponentLookup<MinionGatherOrder>       gatherOrderLookup;
    public Entity             playerEntity;
    public float3             playerPosition;
    public EntityCommandBuffer ecb;
    public bool               loggingEnabled;
    public double             timestamp;

    public void Execute(
        Entity                           unit,
        in UtilityBrain                  brain,
        ref DynamicBuffer<UtilityActions> options)
    {
        // Move: Wander toward a raw ground position.
        if (moveCmdLookup.HasComponent(unit) && moveCmdLookup.IsComponentEnabled(unit))
        {
            float3 destination = moveCmdLookup[unit].destination;
            int    defIndex    = BrainBlobUtils.GetActionDefIndex(
                ref aiConfig.Value, brain.unitType, ActionType.Wander);
            options.Add(new UtilityActions
            {
                actionType        = ActionType.Wander,
                targetEntity      = Entity.Null,
                targetPosition    = destination,
                hasTargetPosition = true,
                actionDefIndex    = defIndex,
                isPlayerOrdered   = true,
            });
            moveCmdLookup.SetComponentEnabled(unit, false);
            ClearPersistentOrders(unit);
            // A unit type with no BrainSO has no Wander def, so the order would silently do nothing.
            if (defIndex < 0)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} cannot Move: no Wander action in the brain for {brain.unitType.Name()}", LogLevel.Warning, timestamp, category: LogCategory.AI);
            else if (loggingEnabled)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Move to ({destination.x:G}, {destination.z:G})", LogLevel.Info, timestamp, category: LogCategory.AI);
        }

        // Attack: resolved from the unit's own AvailableAttack buffer; no usable attack logs a refusal.
        if (attackCmdLookup.HasComponent(unit) && attackCmdLookup.IsComponentEnabled(unit))
        {
            Entity     target             = attackCmdLookup[unit].targetEntity;
            bool       attackResolved     = false;
            ActionType orderedAttackType  = ActionType.Idle;
            int        defIndex           = -1;
            if (availableAttackLookup.TryGetBuffer(unit, out DynamicBuffer<AvailableAttack> availableAttacks))
                attackResolved = AIUtils.ResolveOrderedAttack(
                    ref aiConfig.Value, brain.unitType, availableAttacks.AsNativeArray(), out orderedAttackType, out defIndex);

            attackCmdLookup.SetComponentEnabled(unit, false);
            ClearPersistentOrders(unit);
            if (attackResolved)
            {
                options.Add(new UtilityActions
                {
                    actionType      = orderedAttackType,
                    targetEntity    = target,
                    actionDefIndex  = defIndex,
                    isPlayerOrdered = true,
                });
                if (loggingEnabled)
                    LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Attack {target.Index} with {orderedAttackType.Name()}", LogLevel.Info, timestamp, category: LogCategory.AI);
            }
            else if (loggingEnabled)
            {
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} refused Attack {target.Index}: no AvailableAttack entry has an action def for {brain.unitType.Name()}", LogLevel.Warning, timestamp, category: LogCategory.AI);
            }
        }

        if (interactCmdLookup.HasComponent(unit) && interactCmdLookup.IsComponentEnabled(unit))
        {
            Entity target   = interactCmdLookup[unit].targetEntity;
            int    defIndex = BrainBlobUtils.GetActionDefIndex(
                ref aiConfig.Value, brain.unitType, ActionType.Interact);
            options.Add(new UtilityActions
            {
                actionType      = ActionType.Interact,
                targetEntity    = target,
                actionDefIndex  = defIndex,
                isPlayerOrdered = true,
            });
            interactCmdLookup.SetComponentEnabled(unit, false);
            ClearPersistentOrders(unit);
            if (loggingEnabled)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Interact {target.Index}", LogLevel.Info, timestamp, category: LogCategory.AI);
        }

        // Follow: Wander toward the player entity. UnitSelectionManager re-enables it every frame while F is held.
        if (followCmdLookup.HasComponent(unit) && followCmdLookup.IsComponentEnabled(unit))
        {
            int defIndex = BrainBlobUtils.GetActionDefIndex(
                ref aiConfig.Value, brain.unitType, ActionType.Wander);
            options.Add(new UtilityActions
            {
                actionType      = ActionType.Wander,
                targetEntity    = playerEntity,
                actionDefIndex  = defIndex,
                isPlayerOrdered = true,
            });
            followCmdLookup.SetComponentEnabled(unit, false);
            if (loggingEnabled)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Follow player", LogLevel.Info, timestamp, category: LogCategory.AI);
        }

        // ReturnToPlayer: one-shot move to where the player stands now.
        if (returnCmdLookup.HasComponent(unit) && returnCmdLookup.IsComponentEnabled(unit))
        {
            int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.Wander);
            options.Add(new UtilityActions
            {
                actionType        = ActionType.Wander,
                targetEntity      = Entity.Null,
                targetPosition    = playerPosition,
                hasTargetPosition = true,
                actionDefIndex    = defIndex,
                isPlayerOrdered   = true,
            });
            returnCmdLookup.SetComponentEnabled(unit, false);
            ClearPersistentOrders(unit);
            if (loggingEnabled)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Return to player ({playerPosition.x:G}, {playerPosition.z:G})", LogLevel.Info, timestamp, category: LogCategory.AI);
        }

        // Stop: interrupt and drop this frame's options so the unit falls back to Idle.
        if (stopCmdLookup.HasComponent(unit) && stopCmdLookup.IsComponentEnabled(unit))
        {
            options.Clear();
            if (interruptRequestLookup.HasComponent(unit))
                interruptRequestLookup.SetComponentEnabled(unit, true);
            stopCmdLookup.SetComponentEnabled(unit, false);
            ClearPersistentOrders(unit);
            if (loggingEnabled)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Stop", LogLevel.Info, timestamp, category: LogCategory.AI);
        }
    }

    private void ClearPersistentOrders(Entity unit)
    {
        if (!standingOrderLookup.HasComponent(unit)) return;
        MinionStandingOrder standingOrder = standingOrderLookup[unit];
        if (standingOrder.type != StandingOrderType.None)
        {
            standingOrder.type = StandingOrderType.None;
            standingOrderLookup[unit] = standingOrder;
        }

        // An explicit order ends a gather job; MinionGatherSystem's own Wander moves are options, not commands, so they never land here.
        if (!gatherOrderLookup.HasComponent(unit)) return;
        MinionGatherOrder gatherOrder = gatherOrderLookup[unit];
        if (gatherOrder.phase == GatherPhase.None) return;
        gatherOrder.phase = GatherPhase.None;
        gatherOrder.carriedAmount = 0;
        gatherOrderLookup[unit] = gatherOrder;
    }
}
