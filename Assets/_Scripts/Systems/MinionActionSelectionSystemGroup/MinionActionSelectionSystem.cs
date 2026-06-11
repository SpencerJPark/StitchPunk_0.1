using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Translates active player commands into high-priority UtilityActions buffer entries for
// player-controlled units. Runs AFTER AIAwarenessSystemGroup (buffer already cleared this frame)
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

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BrainLibrary>();
        _moveCmdLookup     = state.GetComponentLookup<OnMinionMoveCommand>(false);
        _attackCmdLookup   = state.GetComponentLookup<OnMinionAttackCommand>(false);
        _interactCmdLookup = state.GetComponentLookup<OnMinionInteractCommand>(false);
        _followCmdLookup   = state.GetComponentLookup<OnMinionFollowCommand>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<Player>()) return;

        _moveCmdLookup.Update(ref state);
        _attackCmdLookup.Update(ref state);
        _interactCmdLookup.Update(ref state);
        _followCmdLookup.Update(ref state);

        Entity       playerEntity  = SystemAPI.GetSingletonEntity<Player>();
        BrainLibrary brainLibrary  = SystemAPI.GetSingleton<BrainLibrary>();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.AI) != 0;

        EntityCommandBuffer ecb = loggingEnabled
            ? SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
            : default;

        state.Dependency = new MinionActionWriteJob
        {
            aiConfig           = brainLibrary.blob,
            playerEntity       = playerEntity,
            moveCmdLookup      = _moveCmdLookup,
            attackCmdLookup    = _attackCmdLookup,
            interactCmdLookup  = _interactCmdLookup,
            followCmdLookup    = _followCmdLookup,
            ecb                = ecb,
            loggingEnabled     = loggingEnabled,
            timestamp          = SystemAPI.Time.ElapsedTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(PlayerUnitBrain))]
public partial struct MinionActionWriteJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>    aiConfig;
    // Read-write: commands are disabled (consumed) after being translated into an option.
    public ComponentLookup<OnMinionMoveCommand>     moveCmdLookup;
    public ComponentLookup<OnMinionAttackCommand>   attackCmdLookup;
    public ComponentLookup<OnMinionInteractCommand> interactCmdLookup;
    public ComponentLookup<OnMinionFollowCommand>   followCmdLookup;
    public Entity             playerEntity;
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

        // Attack — path to target, then RequestAttack.
        if (attackCmdLookup.HasComponent(unit) && attackCmdLookup.IsComponentEnabled(unit))
        {
            Entity target    = attackCmdLookup[unit].targetEntity;
            int    defIndex  = BrainBlobUtils.GetActionDefIndex(
                ref aiConfig.Value, brain.unitType, ActionType.MeleeSingle);
            options.Add(new UtilityActions
            {
                actionType      = ActionType.MeleeSingle,
                targetEntity    = target,
                actionDefIndex  = defIndex,
                isPlayerOrdered = true,
            });
            attackCmdLookup.SetComponentEnabled(unit, false);
            if (loggingEnabled)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Attack {target.Index}", LogLevel.Info, timestamp, category: LogCategory.AI);
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
    }
}
