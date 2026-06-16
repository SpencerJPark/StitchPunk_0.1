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
    private ComponentLookup<AnimationRequest>   _animationRequestLookup;
    private BufferLookup<SetAnimation>          _setAnimationLookup;
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
        _animationRequestLookup  = state.GetComponentLookup<AnimationRequest>(false);
        _setAnimationLookup      = state.GetBufferLookup<SetAnimation>(false);
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
        _animationRequestLookup.Update(ref state);
        _setAnimationLookup.Update(ref state);
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
            animationRequestLookup   = _animationRequestLookup,
            setAnimationLookup       = _setAnimationLookup,
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
    [NativeDisableParallelForRestriction] public ComponentLookup<AnimationRequest>  animationRequestLookup;
    [NativeDisableParallelForRestriction] public BufferLookup<SetAnimation>         setAnimationLookup;

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

        switch (cmd.type)
        {
            case BehaviorCommandType.Approach:
                RunApproach(entityIndex, ref stateMachine, ref pathRequest, pathRequestEnabled,
                    in transform, cmd.FloatParam, cmd.IntParam);
                return; // blocking

            case BehaviorCommandType.WaitTime:
            {
                stateMachine.CommandTimer += deltaTime;

                // Qualifier-as-early-exit: any ticked flag ends the wait before Duration
                // (e.g. Talk's WaitTime exits when the partner dies or disengages).
                bool earlyExit = cmd.Qualifier != LoopQualifier.None
                    && BehaviorQualifiers.Evaluate(unit, in cmd, in stateMachine,
                        in transform, in transformLookup, in deadLookup,
                        in motivationLookup, in stateMachineLookup);

                if (stateMachine.CommandTimer >= cmd.Duration || earlyExit)
                {
                    stateMachine.CurrentCommandIndex++;
                    stateMachine.CommandTimer = 0f;
                }
                return; // blocking
            }

            case BehaviorCommandType.FleeFromTarget:
                RunFlee(ref stateMachine, ref pathRequest, pathRequestEnabled,
                    in transform, ref recentWaypoints);
                return; // blocking

            case BehaviorCommandType.LoopUntil:
            {
                bool qualified = BehaviorQualifiers.Evaluate(unit, in cmd, in stateMachine,
                    in transform, in transformLookup, in deadLookup,
                    in motivationLookup, in stateMachineLookup);

                // Safety guards — always armed, regardless of ticked qualifiers.
                float timeout    = cmd.Duration > 0f ? cmd.Duration : BehaviorQualifiers.DEFAULT_LOOP_TIMEOUT;
                bool  timedOut   = stateMachine.LoopTimer >= timeout;
                bool  capReached = stateMachine.LoopIterations >= BehaviorQualifiers.MAX_LOOP_ITERATIONS;

                if (qualified || timedOut || capReached)
                {
                    if (!qualified && loggingEnabled)
                    {
                        int timedOutFlag = timedOut ? 1 : 0; // hoisted: Burst forbids control flow inside format arguments (BC1352)
                        LogUtil.Log(ref ecb, entityIndex,
                            $"[BehaviorExecution] LoopUntil guard fired in {stateMachine.activeBehavior.Name()} (timedOut: {timedOutFlag}, iterations: {stateMachine.LoopIterations})",
                            LogLevel.Warning, timestamp, category: LogCategory.StateMachine);
                    }

                    stateMachine.CurrentCommandIndex++;
                    stateMachine.LoopTimer      = 0f;
                    stateMachine.LoopIterations = 0;
                }
                else
                {
                    stateMachine.CurrentCommandIndex =
                        math.clamp(cmd.IntParam, 0, stateMachine.CurrentCommandIndex);
                    stateMachine.LoopIterations++;
                }

                stateMachine.CommandTimer = 0f;
                return; // owns its advancement
            }

            case BehaviorCommandType.RequestAttack:
                // AttackRequestSystem reads targetEntity/attackType and the swing timer, so a fresh
                // request must be written each swing — enabling alone would reuse stale hitFired/elapsed.
                if (attackRequestLookup.HasComponent(unit))
                {
                    AttackType attackType = AttackType.None;
                    for (int i = 0; i < availableAttacks.Length; i++)
                    {
                        if (availableAttacks[i].actionType != stateMachine.action) continue;
                        attackType = availableAttacks[i].attackType;
                        break;
                    }

                    if (attackType != AttackType.None && stateMachine.targetEntity != Entity.Null)
                    {
                        attackRequestLookup[unit] = new AttackRequest
                        {
                            targetEntity = stateMachine.targetEntity,
                            attackType   = attackType,
                            hitFired     = false,
                            elapsed      = 0f,
                        };
                        attackRequestLookup.SetComponentEnabled(unit, true);
                    }
                    else if (loggingEnabled)
                    {
                        LogUtil.Log(ref ecb, entityIndex,
                            $"[BehaviorExecution] RequestAttack skipped — no attack mapped to {stateMachine.action.Name()} or no target",
                            LogLevel.Warning, timestamp, category: LogCategory.StateMachine);
                    }
                }
                break;

            case BehaviorCommandType.RequestPickup:
                RunRequestPickup(unit, ref stateMachine);
                break;

            case BehaviorCommandType.ModifyMotivation:
                ecb.AppendToBuffer(entityIndex, unit, new MotivationChangeRequest
                {
                    needType   = (NeedType)cmd.IntParam,
                    changeType = MotivationChangeType.Add,
                    value      = cmd.FloatParam,
                });
                break;

            case BehaviorCommandType.PlayAnimation:
                if (animationRequestLookup.HasComponent(unit) && setAnimationLookup.HasBuffer(unit))
                {
                    setAnimationLookup[unit].Add(new SetAnimation
                    {
                        layer     = AnimationLayerType.Action,
                        animation = (AnimationType)cmd.IntParam,
                        speed     = cmd.FloatParam > 0f ? cmd.FloatParam : 1f,
                        looping   = cmd.Looping,
                    });
                    animationRequestLookup.SetComponentEnabled(unit, true);
                }
                break;

            case BehaviorCommandType.PlayActionAnimation:
                if (animationRequestLookup.HasComponent(unit) && setAnimationLookup.HasBuffer(unit))
                {
                    int unitIndex = unitLibrary.Value.FindByUnitType(brain.unitType);
                    AnimationType actionAnimation = unitIndex >= 0
                        ? AIUtils.GetAnimationByAction(ref unitLibrary.Value.units[unitIndex], stateMachine.action)
                        : AnimationType.None;

                    if (actionAnimation != AnimationType.None)
                    {
                        setAnimationLookup[unit].Add(new SetAnimation
                        {
                            layer     = AnimationLayerType.Action,
                            animation = actionAnimation,
                            speed     = cmd.FloatParam > 0f ? cmd.FloatParam : 1f,
                            looping   = cmd.Looping,
                        });
                        animationRequestLookup.SetComponentEnabled(unit, true);
                    }
                }
                break;

            case BehaviorCommandType.RequestSocialResponse:
                // Written via ECB, not a lookup: multiple initiators may target the same invitee
                // in one frame, so the "one owner" rule that makes the other lookups safe doesn't
                // hold. SocialResponseSystem consumes the invite next frame.
                if (stateMachine.targetEntity != Entity.Null
                    && socialInviteLookup.HasComponent(stateMachine.targetEntity))
                {
                    ecb.SetComponent(entityIndex, stateMachine.targetEntity,
                        new SocialInvite { initiator = unit });
                    ecb.SetComponentEnabled<SocialInvite>(entityIndex, stateMachine.targetEntity, true);
                }
                break;

            case BehaviorCommandType.StopAnimation:
                if (animationRequestLookup.HasComponent(unit) && setAnimationLookup.HasBuffer(unit))
                {
                    setAnimationLookup[unit].Add(new SetAnimation
                    {
                        layer     = AnimationLayerType.Action,
                        animation = AnimationType.None,
                        speed     = 1f,
                        looping   = false,
                    });
                    animationRequestLookup.SetComponentEnabled(unit, true);
                }
                break;

            case BehaviorCommandType.ReleaseInteraction:
                if (stateMachine.targetEntity != Entity.Null)
                {
                    float cooldownEnd = (float)timestamp + (cmd.FloatParam > 0f ? cmd.FloatParam : 30f);
                    if (recentInteractions.Length >= 8) recentInteractions.RemoveAt(0);
                    recentInteractions.Add(new RecentInteraction
                    {
                        entity          = stateMachine.targetEntity,
                        cooldownEndTime = cooldownEnd,
                    });
                }
                break;

            case BehaviorCommandType.PlaySound:
                // Behaviour-level audio cue (e.g. a yell when Flee starts). Fire-and-advance.
                SoundUtil.PlayOn(ref ecb, entityIndex, (SoundType)cmd.IntParam, unit);
                break;

            default:
                break;
        }

        stateMachine.CurrentCommandIndex++;
        stateMachine.CommandTimer = 0f;
    }

    // Scans nearby waypoints and paths at Running speed toward the one farthest from the aggressor.
    // stateMachine.targetEntity must be the aggressor. On first frame: picks waypoint and starts path.
    // On subsequent frames: waits for arrival. On arrival: replaces targetEntity with the waypoint
    // (so PushRecent logs the destination, not the aggressor) and advances.
    private void RunFlee(
        ref StateMachine          stateMachine,
        ref PathRequest           pathRequest,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        in LocalTransform         transform,
        ref DynamicBuffer<RecentWaypoint> recentWaypoints)
    {
        if (!pathRequestEnabled.ValueRO)
        {
            // Pick waypoint farthest from aggressor.
            float3 unitPos      = transform.Position;
            float3 aggressorPos = unitPos; // fallback if aggressor has no transform
            if (stateMachine.targetEntity != Entity.Null)
                transformLookup.TryGetComponent(stateMachine.targetEntity, out LocalTransform aggressorXf);

            // Reread after TryGet — use a local variable to avoid the ref issue.
            if (stateMachine.targetEntity != Entity.Null
                && transformLookup.TryGetComponent(stateMachine.targetEntity, out LocalTransform agXf))
                aggressorPos = agXf.Position;

            int2   centerCell = InteractionSpatialHashSystem.GetCell(unitPos);
            int    cellRange  = 2; // 2 × 20m cells = 40m search radius

            Entity bestWaypoint  = Entity.Null;
            float  bestDistSq    = float.MinValue;

            for (int x = -cellRange; x <= cellRange; x++)
            {
                for (int z = -cellRange; z <= cellRange; z++)
                {
                    int2 cell = centerCell + new int2(x, z);
                    if (!waypointCells.TryGetFirstValue(cell, out Entity waypoint,
                            out NativeParallelMultiHashMapIterator<int2> it))
                        continue;
                    do
                    {
                        if (IsRecent(waypoint, recentWaypoints)) continue;
                        if (!transformLookup.TryGetComponent(waypoint, out LocalTransform wpXf)) continue;

                        // Score = distance from aggressor; higher = safer.
                        float distSq = math.distancesq(aggressorPos, wpXf.Position);
                        if (distSq <= bestDistSq) continue;
                        bestDistSq  = distSq;
                        bestWaypoint = waypoint;
                    }
                    while (waypointCells.TryGetNextValue(out waypoint, ref it));
                }
            }

            if (bestWaypoint == Entity.Null
                || !transformLookup.TryGetComponent(bestWaypoint, out LocalTransform dest))
            {
                // No valid flee waypoint found — give up and let scoring re-evaluate.
                stateMachine.currentPhase = BehaviorPhase.Complete;
                return;
            }

            stateMachine.currentStance = StanceType.Running;
            // Swap aggressor for chosen waypoint so PushRecent logs the destination on complete.
            stateMachine.targetEntity  = bestWaypoint;
            AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, dest.Position, 0.5f);
        }

        // Blocking: wait until arrival.
        const float ARRIVE_SQ = 0.25f;
        if (math.distancesq(transform.Position, pathRequest.targetPosition) <= ARRIVE_SQ)
        {
            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
            stateMachine.CurrentCommandIndex++;
            stateMachine.CommandTimer = 0f;
        }
    }

    private void RunRequestPickup(Entity unit, ref StateMachine stateMachine)
    {
        Entity item = stateMachine.targetEntity;
        if (item == Entity.Null || !pickupRequestLookup.HasComponent(item)) return;

        // Re-validate: another unit may have claimed the item during the approach.
        if (equipByLookup.HasComponent(item)
            && equipByLookup[item].owner != Entity.Null
            && equipByLookup[item].owner != unit)
            return;

        if (equipByLookup.HasComponent(item))
            equipByLookup[item] = new EquipBy { owner = unit };

        Entity socket = Entity.Null;
        if (unitEquipLookup.TryGetComponent(unit, out UnitEquip unitEquip))
            socket = unitEquip.socketEntity;

        if (attachedToLookup.HasComponent(item))
            attachedToLookup[item] = new AttachedTo { socket = socket };

        pickupRequestLookup.SetComponentEnabled(item, true);

        if (attachItemRequestLookup.HasComponent(item))
            attachItemRequestLookup.SetComponentEnabled(item, true);
    }

    private void RunApproach(
        int                       entityIndex,
        ref StateMachine          stateMachine,
        ref PathRequest           pathRequest,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        in LocalTransform         transform,
        float                     stoppingDist,
        int                       stanceIntParam)
    {
        // Raw position target (player move orders): no entity to track — path once and wait
        // for arrival. The moving-target block below degrades safely (lookups miss on Null).
        if (stateMachine.targetEntity == Entity.Null && stateMachine.hasTargetPosition)
        {
            if (!pathRequestEnabled.ValueRO)
            {
                stateMachine.currentStance = (StanceType)stanceIntParam;
                AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled,
                    stateMachine.targetPosition, stoppingDist);
            }
        }
        else if (stateMachine.targetEntity == Entity.Null)
        {
            stateMachine.currentPhase = BehaviorPhase.Complete;
            return;
        }
        else if (!pathRequestEnabled.ValueRO)
        {
            // Dead target — give up immediately rather than pathing to a corpse.
            if (deadLookup.TryGetComponent(stateMachine.targetEntity, out Dead dead)
                && deadLookup.IsComponentEnabled(stateMachine.targetEntity))
            {
                stateMachine.currentPhase = BehaviorPhase.Complete;
                return;
            }

            if (!transformLookup.TryGetComponent(stateMachine.targetEntity, out LocalTransform tgt))
            {
                stateMachine.currentPhase = BehaviorPhase.Complete;
                return;
            }

            // Apply radius scatter for waypoint targets so wandering looks natural.
            float3 targetPos = tgt.Position;
            if (waypointLookup.TryGetComponent(stateMachine.targetEntity, out NavigationWaypoint waypoint)
                && waypoint.radius > 0f)
            {
                Unity.Mathematics.Random rng = new Unity.Mathematics.Random(
                    (uint)(entityIndex + 1) * (uint)(timestamp * 1000f + 1f));
                float angle = rng.NextFloat(0f, math.PI * 2f);
                float dist  = rng.NextFloat(0f, waypoint.radius);
                targetPos  += new float3(math.cos(angle) * dist, 0f, math.sin(angle) * dist);
            }

            stateMachine.currentStance = (StanceType)stanceIntParam;
            AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, targetPos, stoppingDist);
        }

        // Moving targets (units): re-path when the target drifts from the pathed point, and
        // measure ARRIVAL against the live target — the pathed point is stale by up to the
        // repath threshold, and checking against it deadlocks when both units stand close but
        // the stale point sits just out of stopping range (neither arrival nor re-path fires).
        const float REPATH_THRESHOLD_SQ = 1f;
        float3 arrivalPoint = pathRequest.targetPosition;
        if (!waypointLookup.HasComponent(stateMachine.targetEntity)
            && transformLookup.TryGetComponent(stateMachine.targetEntity, out LocalTransform livePos))
        {
            arrivalPoint = livePos.Position;

            if (pathRequestEnabled.ValueRO
                && math.distancesq(livePos.Position, pathRequest.targetPosition) > REPATH_THRESHOLD_SQ)
            {
                AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, livePos.Position, stoppingDist);
            }
        }

        float arrivalSq = stoppingDist * stoppingDist;
        if (math.distancesq(transform.Position, arrivalPoint) <= arrivalSq)
        {
            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
            stateMachine.CurrentCommandIndex++;
            stateMachine.CommandTimer = 0f;
        }
    }

    private static void PushRecent(ref DynamicBuffer<RecentWaypoint> buf, Entity entity)
    {
        if (buf.Length >= 4) buf.RemoveAt(0);
        buf.Add(new RecentWaypoint { entity = entity });
    }

    private static bool IsRecent(Entity entity, in DynamicBuffer<RecentWaypoint> recent)
    {
        for (int i = 0; i < recent.Length; i++)
            if (recent[i].entity == entity) return true;
        return false;
    }
}
