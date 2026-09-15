using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Translates active player commands into high-priority UtilityActions buffer entries for
// player-controlled units. Runs AFTER UtilityAwarenessSystemGroup (buffer already cleared this frame)
// and BEFORE StateMachineSystemGroup. isPlayerOrdered = true causes WinnerSelectionSystem to
// skip scoring and pick this entry unconditionally.
//
// Commands are consumed one-shot: disabled the frame they are translated into an option. The
// order lives on in StateMachine after WinnerSelection — no per-frame re-emit needed (and
// re-emitting would re-preempt every frame under WinnerSelection's player-retarget exemption).
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
            availableAttackLookup   = _availableAttackLookup,
            ecb                     = ecb,
            loggingEnabled          = loggingEnabled,
            timestamp               = SystemAPI.Time.ElapsedTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
// Gate on PlayerUnitBrain only (player decisions). WithPresent(UtilityBrain) keeps brain.unitType
// readable while UtilityBrain is disabled — a player minion's utility AI no longer drives it.
[WithAll(typeof(PlayerUnitBrain))]
[WithPresent(typeof(UtilityBrain))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct MinionActionWriteJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>    aiConfig;
    [ReadOnly] public BufferLookup<AvailableAttack>           availableAttackLookup;
    // Read-write: commands are disabled (consumed) after being translated into an option.
    public ComponentLookup<OnMinionMoveCommand>     moveCmdLookup;
    public ComponentLookup<OnMinionAttackCommand>   attackCmdLookup;
    public ComponentLookup<OnMinionInteractCommand> interactCmdLookup;
    public ComponentLookup<OnMinionFollowCommand>   followCmdLookup;
    public ComponentLookup<OnMinionStopCommand>     stopCmdLookup;
    public ComponentLookup<OnMinionReturnCommand>   returnCmdLookup;
    public ComponentLookup<ActionInterruptRequest>  interruptRequestLookup;
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
        // Move — Wander behavior toward a raw ground position (no target entity).
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
            if (loggingEnabled)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Move to ({destination.x:G}, {destination.z:G})", LogLevel.Info, timestamp, category: LogCategory.AI);
        }

        // Attack — resolve the ordered attack from the unit's own AvailableAttack buffer; a unit with no usable attack refuses loudly.
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

        // Interact — path to target entity.
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
            if (loggingEnabled)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Interact {target.Index}", LogLevel.Info, timestamp, category: LogCategory.AI);
        }

        // Follow — shadow the player by using Wander toward the player entity.
        // UnitSelectionManager re-enables this every frame while F is held.
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

        // ReturnToPlayer — one-shot move to where the player stands now (Follow stays the continuous verb).
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
            if (loggingEnabled)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Return to player ({playerPosition.x:G}, {playerPosition.z:G})", LogLevel.Info, timestamp, category: LogCategory.AI);
        }

        // Stop — interrupt the running behavior and drop this frame's options so the unit falls back to Idle.
        if (stopCmdLookup.HasComponent(unit) && stopCmdLookup.IsComponentEnabled(unit))
        {
            options.Clear();
            if (interruptRequestLookup.HasComponent(unit))
                interruptRequestLookup.SetComponentEnabled(unit, true);
            stopCmdLookup.SetComponentEnabled(unit, false);
            if (loggingEnabled)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Stop", LogLevel.Info, timestamp, category: LogCategory.AI);
        }
    }
}
