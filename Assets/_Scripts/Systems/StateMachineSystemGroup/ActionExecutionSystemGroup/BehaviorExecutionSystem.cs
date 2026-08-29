using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

// Runs the Execute → Complete phase machine for all UtilityBrain units.
// Execution sequences are fully authored in BehaviorSO. Designers compose behaviors from
// BehaviorCommandType steps. Requests (PathRequest, AttackRequest, PickupRequest) are the API
// boundary to downstream systems — this system enables them; downstream systems do the work.
[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup))]
public partial struct BehaviorExecutionSystem : ISystem
{
    private ComponentLookup<LocalTransform>     _transformLookup;
    private ComponentLookup<AttackRequest>      _attackRequestLookup;
    private ComponentLookup<PickupRequest>      _pickupRequestLookup;
    private ComponentLookup<EquipBy>            _equipByLookup;
    private ComponentLookup<AttachedTo>         _attachedToLookup;
    private ComponentLookup<AttachItemRequest>  _attachItemRequestLookup;
    private ComponentLookup<UnitEquip>          _unitEquipLookup;
    private ComponentLookup<NavigationWaypoint> _waypointLookup;
    private ComponentLookup<Dead>               _deadLookup;
    private ComponentLookup<AnimationCommandPending> _animationCommandPendingLookup;
    private BufferLookup<AnimationCommand>      _animationCommandLookup;
    private BufferLookup<Motivation>            _motivationLookup;
    private ComponentLookup<StateMachine>       _stateMachineLookup;
    private ComponentLookup<SocialInvite>       _socialInviteLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BehaviorLibrary>();
        state.RequireForUpdate<SpatialHashRegistry>();
        state.RequireForUpdate<UnitDataLibrary>();
        _transformLookup         = state.GetComponentLookup<LocalTransform>(true);
        _attackRequestLookup     = state.GetComponentLookup<AttackRequest>(false);
        _pickupRequestLookup     = state.GetComponentLookup<PickupRequest>(false);
        _equipByLookup           = state.GetComponentLookup<EquipBy>(false);
        _attachedToLookup        = state.GetComponentLookup<AttachedTo>(false);
        _attachItemRequestLookup = state.GetComponentLookup<AttachItemRequest>(false);
        _unitEquipLookup         = state.GetComponentLookup<UnitEquip>(true);
        _waypointLookup          = state.GetComponentLookup<NavigationWaypoint>(true);
        _deadLookup              = state.GetComponentLookup<Dead>(true);
        _animationCommandPendingLookup = state.GetComponentLookup<AnimationCommandPending>(false);
        _animationCommandLookup  = state.GetBufferLookup<AnimationCommand>(false);
        _motivationLookup        = state.GetBufferLookup<Motivation>(true);
        _stateMachineLookup      = state.GetComponentLookup<StateMachine>(true);
        _socialInviteLookup      = state.GetComponentLookup<SocialInvite>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _transformLookup.Update(ref state);
        _attackRequestLookup.Update(ref state);
        _pickupRequestLookup.Update(ref state);
        _equipByLookup.Update(ref state);
        _attachedToLookup.Update(ref state);
        _attachItemRequestLookup.Update(ref state);
        _unitEquipLookup.Update(ref state);
        _waypointLookup.Update(ref state);
        _deadLookup.Update(ref state);
        _animationCommandPendingLookup.Update(ref state);
        _animationCommandLookup.Update(ref state);
        _motivationLookup.Update(ref state);
        _stateMachineLookup.Update(ref state);
        _socialInviteLookup.Update(ref state);

        BehaviorLibrary      behaviorLib  = SystemAPI.GetSingleton<BehaviorLibrary>();
        SpatialHashRegistry  registry     = SystemAPI.GetSingleton<SpatialHashRegistry>();
        BlobAssetReference<UnitLibraryBlob> unitLibrary = SystemAPI.GetSingleton<UnitDataLibrary>().library;
        float                deltaTime    = SystemAPI.Time.DeltaTime;
        EntityCommandBuffer.ParallelWriter ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.StateMachine) != 0;

        state.Dependency = new BehaviorExecutionJob
        {
            behaviorLib              = behaviorLib.blob,
            unitLibrary              = unitLibrary,
            transformLookup          = _transformLookup,
            attackRequestLookup      = _attackRequestLookup,
            pickupRequestLookup      = _pickupRequestLookup,
            equipByLookup            = _equipByLookup,
            attachedToLookup         = _attachedToLookup,
            attachItemRequestLookup  = _attachItemRequestLookup,
            unitEquipLookup          = _unitEquipLookup,
            waypointLookup           = _waypointLookup,
            deadLookup               = _deadLookup,
            animationCommandPendingLookup = _animationCommandPendingLookup,
            animationCommandLookup   = _animationCommandLookup,
            motivationLookup         = _motivationLookup,
            stateMachineLookup       = _stateMachineLookup,
            socialInviteLookup       = _socialInviteLookup,
            waypointCells            = registry.waypointCells,
            deltaTime                = deltaTime,
            ecb                      = ecb,
            timestamp                = SystemAPI.Time.ElapsedTime,
            loggingEnabled           = loggingEnabled,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
// WithPresent (not WithAll): the StateMachine/execution half must run even when UtilityBrain is
// disabled — corpses (their death behavior still executes) and player minions (decisions come from
// the player, the brain is off). brain.unitType stays readable under WithPresent.
[WithPresent(typeof(UtilityBrain))]
[WithPresent(typeof(PathRequest))]
public partial struct BehaviorExecutionJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BehaviorLibraryBlob>       behaviorLib;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>           unitLibrary;
    [ReadOnly] public ComponentLookup<LocalTransform>                transformLookup;
    [ReadOnly] public ComponentLookup<UnitEquip>                     unitEquipLookup;
    [ReadOnly] public NativeParallelMultiHashMap<int2, Entity>        waypointCells;
    [ReadOnly] public ComponentLookup<NavigationWaypoint>            waypointLookup;
    [ReadOnly] public ComponentLookup<Dead>                          deadLookup;
    [ReadOnly] public BufferLookup<Motivation>                       motivationLookup;
    [ReadOnly] public ComponentLookup<SocialInvite>                  socialInviteLookup;

    // Read-only lookup aliases the StateMachine this job writes by ref. Safe: qualifier checks only
    // read OTHER units' StateMachine (the target's), and each unit writes only its own.
    [ReadOnly] [NativeDisableContainerSafetyRestriction]
    public ComponentLookup<StateMachine> stateMachineLookup;

    // NativeDisableParallelForRestriction is safe: each unit/item is owned by at most one
    // executing behavior at a time, so no two threads write to the same component.
    [NativeDisableParallelForRestriction] public ComponentLookup<AttackRequest>     attackRequestLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<PickupRequest>     pickupRequestLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<EquipBy>           equipByLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<AttachedTo>        attachedToLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<AttachItemRequest> attachItemRequestLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<AnimationCommandPending> animationCommandPendingLookup;
    [NativeDisableParallelForRestriction] public BufferLookup<AnimationCommand>     animationCommandLookup;

    public float  deltaTime;
    public double timestamp;
    public bool   loggingEnabled;
    public EntityCommandBuffer.ParallelWriter ecb;

    public void Execute(
        [EntityIndexInQuery] int               entityIndex,
        Entity                                 unit,
        in LocalTransform                      transform,
        ref StateMachine                       stateMachine,
        ref PathRequest                        pathRequest,
        EnabledRefRW<PathRequest>              pathRequestEnabled,
        ref DynamicBuffer<RecentWaypoint>      recentWaypoints,
        ref DynamicBuffer<RecentInteraction>   recentInteractions,
        in DynamicBuffer<AvailableAttack>      availableAttacks,
        in UtilityBrain                        brain)
    {
        if (stateMachine.activeBehavior == BehaviorType.None) return;

        ref BehaviorConfigBlob behavior =
            ref behaviorLib.Value.behaviors[(int)stateMachine.activeBehavior];

        switch (stateMachine.currentPhase)
        {
            case BehaviorPhase.Execute:
                RunExecute(entityIndex, unit, ref stateMachine, ref pathRequest, pathRequestEnabled,
                    in transform, ref behavior, ref recentWaypoints, ref recentInteractions,
                    in availableAttacks, in brain);
                break;

            case BehaviorPhase.Complete:
                if (stateMachine.targetEntity != Entity.Null)
                    PushRecent(ref recentWaypoints, stateMachine.targetEntity);

                if (loggingEnabled)
                    LogUtil.Log(ref ecb, entityIndex,
                        $"[BehaviorExecution] Entity {unit.Index} behavior {stateMachine.activeBehavior.Name()} complete (action: {stateMachine.action.Name()})",
                        LogLevel.Info, timestamp, category: LogCategory.StateMachine);

                stateMachine.action              = ActionType.Idle;
                stateMachine.activeBehavior      = BehaviorType.None;
                stateMachine.targetEntity        = Entity.Null;
                stateMachine.targetPosition      = default;
                stateMachine.hasTargetPosition   = false;
                stateMachine.activePriority      = 0;
                stateMachine.currentPhase        = BehaviorPhase.Execute;
                stateMachine.currentStance       = StanceType.Normal;
                stateMachine.CurrentCommandIndex = 0;
                stateMachine.CommandTimer        = 0f;
                stateMachine.LoopTimer           = 0f;
                stateMachine.LoopIterations      = 0;
                break;
        }
    }

    private void RunExecute(
        int                               entityIndex,
        Entity                            unit,
        ref StateMachine                  stateMachine,
        ref PathRequest                   pathRequest,
        EnabledRefRW<PathRequest>         pathRequestEnabled,
        in LocalTransform                 transform,
        ref BehaviorConfigBlob            behavior,
        ref DynamicBuffer<RecentWaypoint> recentWaypoints,
        ref DynamicBuffer<RecentInteraction> recentInteractions,
        in DynamicBuffer<AvailableAttack> availableAttacks,
        in UtilityBrain                   brain)
    {
        if (stateMachine.CurrentCommandIndex >= behavior.executionSequence.Length)
        {
            stateMachine.currentPhase = BehaviorPhase.Complete;
            return;
        }

        // Wall time since behavior start — read by LoopUntil; unlike CommandTimer it survives
        // blocking commands resetting their own timers.
        stateMachine.LoopTimer += deltaTime;

        ref BehaviorCommand cmd =
            ref behavior.executionSequence[stateMachine.CurrentCommandIndex];

        BehaviorCommandContext context = new BehaviorCommandContext
        {
            transformLookup               = transformLookup,
            unitEquipLookup               = unitEquipLookup,
            waypointLookup                = waypointLookup,
            deadLookup                    = deadLookup,
            motivationLookup              = motivationLookup,
            socialInviteLookup            = socialInviteLookup,
            stateMachineLookup            = stateMachineLookup,
            waypointCells                 = waypointCells,
            unitLibrary                   = unitLibrary,
            attackRequestLookup           = attackRequestLookup,
            pickupRequestLookup           = pickupRequestLookup,
            equipByLookup                 = equipByLookup,
            attachedToLookup              = attachedToLookup,
            attachItemRequestLookup       = attachItemRequestLookup,
            animationCommandPendingLookup = animationCommandPendingLookup,
            animationCommandLookup        = animationCommandLookup,
            ecb                           = ecb,
            entityIndex                   = entityIndex,
            deltaTime                     = deltaTime,
            timestamp                     = timestamp,
            loggingEnabled                = loggingEnabled,
        };

        switch (cmd.type)
        {
            case BehaviorCommandType.Approach:
                MovementCommands.RunApproach(ref context, ref stateMachine, ref pathRequest,
                    pathRequestEnabled, in transform, cmd.FloatParam, cmd.IntParam);
                return; // blocking — owns its advancement

            case BehaviorCommandType.WaitTime:
                WaitLoopCommands.RunWaitTime(ref context, unit, ref stateMachine, in cmd, in transform);
                return; // blocking — owns its advancement

            case BehaviorCommandType.FleeFromTarget:
                MovementCommands.RunFlee(ref context, ref stateMachine, ref pathRequest, pathRequestEnabled,
                    in transform, InteractionSpatialHashSystem.GetCell(transform.Position), ref recentWaypoints);
                return; // blocking — owns its advancement

            case BehaviorCommandType.LoopUntil:
                WaitLoopCommands.RunLoopUntil(ref context, unit, ref stateMachine, in cmd, in transform);
                return; // owns its advancement

            case BehaviorCommandType.RequestAttack:
                RequestCommands.RunRequestAttack(ref context, unit, ref stateMachine, in availableAttacks);
                break;

            case BehaviorCommandType.RequestPickup:
                ItemCommands.RunRequestPickup(ref context, unit, ref stateMachine);
                break;

            case BehaviorCommandType.ModifyMotivation:
                RequestCommands.RunModifyMotivation(ecb, entityIndex, unit, in cmd);
                break;

            case BehaviorCommandType.PlayAnimation:
                AnimationCommands.RunPlayAnimation(ref context, unit, in cmd);
                break;

            case BehaviorCommandType.PlayActionAnimation:
                AnimationCommands.RunPlayActionAnimation(ref context, unit, in cmd, in stateMachine, in brain);
                break;

            case BehaviorCommandType.RequestSocialResponse:
                RequestCommands.RunRequestSocialResponse(ref context, unit, in stateMachine);
                break;

            case BehaviorCommandType.StopAnimation:
                AnimationCommands.RunStopAnimation(animationCommandPendingLookup, animationCommandLookup, unit);
                break;

            case BehaviorCommandType.ReleaseInteraction:
                MiscCommands.RunReleaseInteraction(timestamp, in cmd, stateMachine.targetEntity, ref recentInteractions);
                break;

            case BehaviorCommandType.PlaySound:
                MiscCommands.RunPlaySound(ref context, unit, in cmd);
                break;

            default:
                // No interpreter arm — bake validation (BehaviorCommandCatalog) should have
                // warned already; this is the runtime backstop for data that slipped past it.
                if (loggingEnabled)
                {
                    LogUtil.Log(ref ecb, entityIndex,
                        $"[BehaviorExecution] Unimplemented command {cmd.type.Name()} in {stateMachine.activeBehavior.Name()} — skipping",
                        LogLevel.Warning, timestamp, category: LogCategory.StateMachine);
                }
                break;
        }

        stateMachine.CurrentCommandIndex++;
        stateMachine.CommandTimer = 0f;
    }

    private static void PushRecent(ref DynamicBuffer<RecentWaypoint> buf, Entity entity)
    {
        if (buf.Length >= 4) buf.RemoveAt(0);
        buf.Add(new RecentWaypoint { entity = entity });
    }
}
