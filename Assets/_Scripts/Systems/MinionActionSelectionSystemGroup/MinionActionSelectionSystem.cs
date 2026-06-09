using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Translates active player commands into high-priority UtilityActions buffer entries for
// player-controlled units. Runs AFTER AIAwarenessSystemGroup (buffer already cleared this frame)
// and BEFORE StateMachineSystemGroup. isPlayerOrdered = true causes WinnerSelectionSystem to
// skip scoring and pick this entry unconditionally.
//
// Move-to-position (OnMinionMoveCommand) is not yet wired — needs targetPosition support
// in StateMachine. Tracked in UtilityAI.md Phase 4 notes.
[BurstCompile]
[UpdateInGroup(typeof(MinionActionSelectionSystemGroup))]
public partial struct MinionActionSelectionSystem : ISystem
{
    private ComponentLookup<OnMinionAttackCommand>   _attackCmdLookup;
    private ComponentLookup<OnMinionInteractCommand> _interactCmdLookup;
    private ComponentLookup<OnMinionFollowCommand>   _followCmdLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BrainLibrary>();
        _attackCmdLookup   = state.GetComponentLookup<OnMinionAttackCommand>(true);
        _interactCmdLookup = state.GetComponentLookup<OnMinionInteractCommand>(true);
        _followCmdLookup   = state.GetComponentLookup<OnMinionFollowCommand>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<Player>()) return;

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
    [ReadOnly] public ComponentLookup<OnMinionAttackCommand>  attackCmdLookup;
    [ReadOnly] public ComponentLookup<OnMinionInteractCommand> interactCmdLookup;
    [ReadOnly] public ComponentLookup<OnMinionFollowCommand>  followCmdLookup;
    public Entity             playerEntity;
    public EntityCommandBuffer ecb;
    public bool               loggingEnabled;
    public double             timestamp;

    public void Execute(
        Entity                           unit,
        in UtilityBrain                  brain,
        ref DynamicBuffer<UtilityActions> options)
    {
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
            if (loggingEnabled)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Interact {target.Index}", LogLevel.Info, timestamp, category: LogCategory.AI);
        }

        // Follow — shadow the player by using Wander toward the player entity.
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
            if (loggingEnabled)
                LogUtil.Log(ref ecb, $"[MinionOrder] Unit {unit.Index} -> Follow player", LogLevel.Info, timestamp, category: LogCategory.AI);
        }
    }
}
