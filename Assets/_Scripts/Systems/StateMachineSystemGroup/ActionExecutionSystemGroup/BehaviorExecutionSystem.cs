using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

// Interprets the chosen BehaviorSO's command sequence. Requests (PathRequest, AttackRequest, PickupRequest)
// are the boundary: this system enables them, downstream systems do the work.
[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup))]
public partial struct BehaviorExecutionSystem : ISystem
{
    private ComponentLookup<EmoteState>         _emoteStateLookup;
    private ComponentLookup<Intoxication>       _intoxicationLookup;
    private ComponentLookup<Movement>           _movementLookup;
    private ComponentLookup<LocalTransform>     _transformLookup;
    private ComponentLookup<AttackRequest>      _attackRequestLookup;
    private ComponentLookup<Item>               _itemLookup;
    private ComponentLookup<RangedAim>          _rangedAimLookup;
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
    private ComponentLookup<UnitFacing>         _unitFacingLookup;
    private ComponentLookup<SocialInvite>       _socialInviteLookup;
    private BufferLookup<AnimEventOutput>       _animEventOutputLookup;
    private ComponentLookup<AnimEventsPending>  _animEventsPendingLookup;
    private BufferLookup<PlaybackLayer>         _playbackLayerLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BehaviorLibrary>();
        state.RequireForUpdate<SpatialHashRegistry>();
        state.RequireForUpdate<UnitDataLibrary>();
        _emoteStateLookup        = state.GetComponentLookup<EmoteState>(false);
        _intoxicationLookup      = state.GetComponentLookup<Intoxication>(false);
        _movementLookup          = state.GetComponentLookup<Movement>(true);
        _transformLookup         = state.GetComponentLookup<LocalTransform>(true);
        _attackRequestLookup     = state.GetComponentLookup<AttackRequest>(false);
        _itemLookup              = state.GetComponentLookup<Item>(true);
        _rangedAimLookup         = state.GetComponentLookup<RangedAim>(false);
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
        _unitFacingLookup        = state.GetComponentLookup<UnitFacing>(true);
        _socialInviteLookup      = state.GetComponentLookup<SocialInvite>(true);
        _animEventOutputLookup   = state.GetBufferLookup<AnimEventOutput>(true);
        _animEventsPendingLookup = state.GetComponentLookup<AnimEventsPending>(true);
        _playbackLayerLookup     = state.GetBufferLookup<PlaybackLayer>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _emoteStateLookup.Update(ref state);
        _intoxicationLookup.Update(ref state);
        _movementLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _attackRequestLookup.Update(ref state);
        _itemLookup.Update(ref state);
        _rangedAimLookup.Update(ref state);
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
        _unitFacingLookup.Update(ref state);
        _socialInviteLookup.Update(ref state);
        _animEventOutputLookup.Update(ref state);
        _animEventsPendingLookup.Update(ref state);
        _playbackLayerLookup.Update(ref state);

        BehaviorLibrary      behaviorLib  = SystemAPI.GetSingleton<BehaviorLibrary>();
        SpatialHashRegistry  registry     = SystemAPI.GetSingleton<SpatialHashRegistry>();
        BlobAssetReference<UnitLibraryBlob> unitLibrary = SystemAPI.GetSingleton<UnitDataLibrary>().library;
        BlobAssetReference<ItemLibraryBlob> itemLibrary = SystemAPI.TryGetSingleton<ItemLibrary>(out ItemLibrary itemLibrarySingleton)
            ? itemLibrarySingleton.library : default;
        float                deltaTime    = SystemAPI.Time.DeltaTime;
        EntityCommandBuffer.ParallelWriter ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.StateMachine) != 0;

        BehaviorCommandContext sharedCommandContext = new BehaviorCommandContext
        {
            transformLookup               = _transformLookup,
            unitEquipLookup               = _unitEquipLookup,
            waypointLookup                = _waypointLookup,
            deadLookup                    = _deadLookup,
            motivationLookup              = _motivationLookup,
            socialInviteLookup            = _socialInviteLookup,
            stateMachineLookup            = _stateMachineLookup,
            unitFacingLookup              = _unitFacingLookup,
            animEventOutputLookup         = _animEventOutputLookup,
            animEventsPendingLookup       = _animEventsPendingLookup,
            waypointCells                 = registry.waypointCells,
            unitLibrary                   = unitLibrary,
            attackRequestLookup           = _attackRequestLookup,
            rangedAimLookup               = _rangedAimLookup,
            itemLookup                    = _itemLookup,
            itemLibrary                   = itemLibrary,
            pickupRequestLookup           = _pickupRequestLookup,
            equipByLookup                 = _equipByLookup,
            attachedToLookup              = _attachedToLookup,
            attachItemRequestLookup       = _attachItemRequestLookup,
            animationCommandPendingLookup = _animationCommandPendingLookup,
            animationCommandLookup        = _animationCommandLookup,
            ecb                           = ecb,
            deltaTime                     = deltaTime,
            timestamp                     = SystemAPI.Time.ElapsedTime,
            loggingEnabled                = loggingEnabled,
        };

        state.Dependency = new BehaviorExecutionJob
        {
            behaviorLib         = behaviorLib.blob,
            playbackLayerLookup = _playbackLayerLookup,
            emoteStateLookup    = _emoteStateLookup,
            intoxicationLookup  = _intoxicationLookup,
            movementLookup      = _movementLookup,
            sharedCommandContext = sharedCommandContext,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
// WithPresent (not WithAll): must run with UtilityBrain disabled, for corpses (death behavior) and player minions.
[WithPresent(typeof(UtilityBrain))]
[WithPresent(typeof(PathRequest))]
// An abstract unit's position is a route, not a transform: running commands on it would re-enable the PathRequest the tier switched off.
[WithNone(typeof(AbstractSimulation))]
public partial struct BehaviorExecutionJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BehaviorLibraryBlob>       behaviorLib;
    [ReadOnly] public BufferLookup<PlaybackLayer>                    playbackLayerLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<EmoteState>   emoteStateLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<Intoxication> intoxicationLookup;
    [ReadOnly] public ComponentLookup<Movement> movementLookup;

    // Shared by all units; RunExecute copies it and sets only entityIndex, never writes the shared copy.
    public BehaviorCommandContext sharedCommandContext;

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

                if (sharedCommandContext.loggingEnabled)
                    LogUtil.Log(ref sharedCommandContext.ecb, entityIndex,
                        $"[BehaviorExecution] Entity {unit.Index} behavior {stateMachine.activeBehavior.Name()} complete (action: {stateMachine.action.Name()})",
                        LogLevel.Info, sharedCommandContext.timestamp, category: LogCategory.StateMachine);

                StateMachineTransitions.ResetToIdle(ref stateMachine);
                break;
        }
    }

    private void RunShowEmote(Entity unit, in BehaviorCommand cmd)
    {
        if (!emoteStateLookup.HasComponent(unit)) return;
        float seconds = cmd.FloatParam > 0f ? cmd.FloatParam : 2.5f;
        EmoteState emoteState = emoteStateLookup[unit];
        if (EmoteRules.TryShow(ref emoteState, (EmoteIcon)cmd.IntParam, seconds, EmoteRules.ConversationPriority))
            emoteStateLookup[unit] = emoteState;
    }

    private void RunAddIntoxication(Entity unit, in BehaviorCommand cmd)
    {
        if (!intoxicationLookup.HasComponent(unit)) return;
        float delta = cmd.FloatParam;
        Intoxication intoxication = intoxicationLookup[unit];
        bool wasEnabled = intoxicationLookup.IsComponentEnabled(unit);
        intoxication.level = math.saturate(intoxication.level + delta);
        // Capture the sober speed only on the sober -> drunk edge so the wobble system can restore it.
        if (!wasEnabled && delta > 0f)
        {
            if (movementLookup.HasComponent(unit))
                intoxication.baseMoveSpeed = movementLookup[unit].moveSpeed;
            intoxication.nextDrunkEmoteIn = 2f;
            intoxicationLookup.SetComponentEnabled(unit, true);
        }
        intoxicationLookup[unit] = intoxication;
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

        // Wall time since behavior start for LoopUntil; survives blocking commands resetting CommandTimer.
        stateMachine.LoopTimer += sharedCommandContext.deltaTime;

        ref BehaviorCommand cmd =
            ref behavior.executionSequence[stateMachine.CurrentCommandIndex];

        BehaviorCommandContext context = sharedCommandContext;
        context.entityIndex = entityIndex;

        switch (cmd.type)
        {
            case BehaviorCommandType.Approach:
                MovementCommands.RunApproach(ref context, ref stateMachine, ref pathRequest,
                    pathRequestEnabled, in transform, cmd.FloatParam, cmd.IntParam);
                return; // blocking — owns its advancement

            case BehaviorCommandType.WaitTime:
                WaitLoopCommands.RunWaitTime(ref context, unit, ref stateMachine, in cmd, in transform);
                return; // blocking — owns its advancement

            case BehaviorCommandType.WindUp:
                RangedCommands.RunWindUp(ref context, unit, ref stateMachine, in cmd);
                return; // blocking - owns its advancement

            case BehaviorCommandType.FleeFromTarget:
                MovementCommands.RunFlee(ref context, ref stateMachine, ref pathRequest, pathRequestEnabled,
                    in transform, InteractionSpatialHashSystem.GetCell(transform.Position), ref recentWaypoints);
                return; // blocking — owns its advancement

            case BehaviorCommandType.LoopUntil:
                WaitLoopCommands.RunLoopUntil(ref context, unit, ref stateMachine, in cmd, in transform);
                return; // owns its advancement

            case BehaviorCommandType.WaitForAnimEvent:
                WaitForAnimEventCommand.Run(ref context, unit, ref stateMachine, in cmd);
                return; // blocking — owns its advancement

            case BehaviorCommandType.WaitForClipFinished:
                WaitForClipFinishedCommand.Run(ref context, unit, ref stateMachine, in cmd);
                return; // blocking — owns its advancement

            case BehaviorCommandType.RequestAttack:
                RequestCommands.RunRequestAttack(ref context, unit, ref stateMachine, in availableAttacks);
                break;

            case BehaviorCommandType.RequestPickup:
                ItemCommands.RunRequestPickup(ref context, unit, ref stateMachine);
                break;

            case BehaviorCommandType.RequestThrow:
                ItemCommands.RunRequestThrow(ref context, unit, in stateMachine);
                break;

            case BehaviorCommandType.RequestShoot:
                RangedCommands.RunRequestShoot(ref context, unit, in stateMachine);
                break;

            case BehaviorCommandType.ModifyMotivation:
                RequestCommands.RunModifyMotivation(sharedCommandContext.ecb, entityIndex, unit, in cmd);
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
                AnimationCommands.RunStopAnimation(sharedCommandContext.animationCommandPendingLookup, sharedCommandContext.animationCommandLookup, playbackLayerLookup, unit);
                break;

            case BehaviorCommandType.ReleaseInteraction:
                MiscCommands.RunReleaseInteraction(sharedCommandContext.timestamp, in cmd, stateMachine.targetEntity, ref recentInteractions);
                break;

            case BehaviorCommandType.PlaySound:
                MiscCommands.RunPlaySound(ref context, unit, in cmd);
                break;

            case BehaviorCommandType.ShowEmote:
                RunShowEmote(unit, in cmd);
                break;

            case BehaviorCommandType.AddIntoxication:
                RunAddIntoxication(unit, in cmd);
                break;

            default:
                // No interpreter arm — bake validation (BehaviorCommandCatalog) should have
                // warned already; this is the runtime backstop for data that slipped past it.
                if (sharedCommandContext.loggingEnabled)
                {
                    LogUtil.Log(ref sharedCommandContext.ecb, entityIndex,
                        $"[BehaviorExecution] Unimplemented command {cmd.type.Name()} in {stateMachine.activeBehavior.Name()} — skipping",
                        LogLevel.Warning, sharedCommandContext.timestamp, category: LogCategory.StateMachine);
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
