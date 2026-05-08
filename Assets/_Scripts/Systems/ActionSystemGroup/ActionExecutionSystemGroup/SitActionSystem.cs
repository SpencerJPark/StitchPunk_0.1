using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Executes the Sit action. Paths to the interaction entity, plays the sit animation,
// waits for SIT_DURATION, applies motivation satisfaction, then returns to ActionRequest.
//
// States (ActionTimer enabled state as flag):
//   Pathing  (SitAction enabled, ActionTimer disabled) — path toward chair
//   Sitting  (SitAction enabled, ActionTimer enabled)  — animate + count down timer
//   Complete — apply satisfaction, flag occupant release, disable SitAction, re-enable ActionRequest
//
// Interrupted mid-sit: ActionInterruptSystem enables SitReleaseRequest before this
// system runs, so occupant cleanup still happens even without a normal completion.
[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup))]
public partial struct SitActionSystem : ISystem
{
    private ComponentLookup<LocalTransform>          transformLookup;
    private ComponentLookup<Interaction>             interactionReadLookup;
    private ComponentLookup<InteractionProvider>     providerLookup;
    private ComponentLookup<Interaction>             interactionWriteLookup;
    private BufferLookup<MotivationSatisfaction>     satisfactionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        transformLookup        = state.GetComponentLookup<LocalTransform>(true);
        interactionReadLookup  = state.GetComponentLookup<Interaction>(true);
        providerLookup         = state.GetComponentLookup<InteractionProvider>(true);
        interactionWriteLookup = state.GetComponentLookup<Interaction>(false);
        satisfactionLookup     = state.GetBufferLookup<MotivationSatisfaction>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        interactionReadLookup.Update(ref state);
        providerLookup.Update(ref state);
        interactionWriteLookup.Update(ref state);
        satisfactionLookup.Update(ref state);

        state.Dependency = new SitJob
        {
            deltaTime          = SystemAPI.Time.DeltaTime,
            transformLookup    = transformLookup,
            interactionLookup  = interactionReadLookup,
            providerLookup     = providerLookup,
            satisfactionLookup = satisfactionLookup,
        }.ScheduleParallel(state.Dependency);

        // Complete before the single-threaded release job to avoid write conflicts
        state.Dependency.Complete();

        new SitReleaseJob
        {
            interactionLookup = interactionWriteLookup,
        }.Run();
    }
}

[BurstCompile]
[WithAll(typeof(SitAction))]
[WithDisabled(typeof(ActionRequest), typeof(ActionInterruptRequest))]
[WithPresent(typeof(Dead), typeof(PathRequest), typeof(ActionTimer),
             typeof(ReleaseRequest), typeof(AnimationRequest))]
public partial struct SitJob : IJobEntity
{
    private const float SIT_DURATION   = 8.0f;
    private const float REPATH_DIST_SQ = 1.0f;

    public float deltaTime;

    [ReadOnly] public ComponentLookup<LocalTransform>      transformLookup;
    [ReadOnly] public ComponentLookup<Interaction>         interactionLookup;
    [ReadOnly] public ComponentLookup<InteractionProvider> providerLookup;
    [ReadOnly] public BufferLookup<MotivationSatisfaction> satisfactionLookup;

    public void Execute(
        in LocalTransform                           localTransform,
        in CurrentAction                            action,
        ref PathRequest                             pathRequest,
        ref ActionTimer                             actionTimer,
        ref DynamicBuffer<SetAnimation>             setAnimations,
        ref DynamicBuffer<MotivationChangeRequest>  changeRequests,
        ref ReleaseRequest                       releaseReq,
        EnabledRefRW<SitAction>                     sitEnabled,
        EnabledRefRW<ActionRequest>                 actionRequestEnabled,
        EnabledRefRW<ActionTimer>                   actionTimerEnabled,
        EnabledRefRW<PathRequest>                   pathRequestEnabled,
        EnabledRefRW<ReleaseRequest>             sitReleaseEnabled,
        EnabledRefRW<AnimationRequest>              animRequestEnabled,
        EnabledRefRO<Dead>                          dead)
    {
        Entity target = action.targetEntity;

        if (dead.ValueRO)
        {
            Terminate(ref pathRequest, sitEnabled, actionRequestEnabled,
                actionTimerEnabled, pathRequestEnabled, ref releaseReq,
                sitReleaseEnabled, target, actionTimerEnabled.ValueRO);
            return;
        }

        // --- Sitting state ---
        if (actionTimerEnabled.ValueRO)
        {
            actionTimer.time -= deltaTime;
            if (actionTimer.time <= 0f)
            {
                ApplySatisfaction(target, ref changeRequests);
                Terminate(ref pathRequest, sitEnabled, actionRequestEnabled,
                    actionTimerEnabled, pathRequestEnabled, ref releaseReq,
                    sitReleaseEnabled, target, wasArrived: true);
            }
            return;
        }

        // --- Pathing state: validate target ---
        if (!transformLookup.TryGetComponent(target, out LocalTransform targetTransform))
        {
            Terminate(ref pathRequest, sitEnabled, actionRequestEnabled,
                actionTimerEnabled, pathRequestEnabled, ref releaseReq,
                sitReleaseEnabled, target, wasArrived: false);
            return;
        }

        if (!providerLookup.IsComponentEnabled(target))
        {
            Terminate(ref pathRequest, sitEnabled, actionRequestEnabled,
                actionTimerEnabled, pathRequestEnabled, ref releaseReq,
                sitReleaseEnabled, target, wasArrived: false);
            return;
        }

        if (!interactionLookup.TryGetComponent(target, out Interaction interact))
        {
            Terminate(ref pathRequest, sitEnabled, actionRequestEnabled,
                actionTimerEnabled, pathRequestEnabled, ref releaseReq,
                sitReleaseEnabled, target, wasArrived: false);
            return;
        }

        // --- Arrival check ---
        float distSq  = math.distancesq(localTransform.Position, targetTransform.Position);
        float rangeSq = interact.range * interact.range;

        if (distSq <= rangeSq)
        {
            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
            actionTimerEnabled.ValueRW = true;
            actionTimer.time           = SIT_DURATION;

            setAnimations.Add(new SetAnimation
            {
                layer     = AnimationLayerType.Action,
                animation = AnimationType.Sit,
                speed     = 1f
            });
            animRequestEnabled.ValueRW = true;
            return;
        }

        // --- Repath if target has moved ---
        if (math.distancesq(pathRequest.targetPosition, targetTransform.Position) >= REPATH_DIST_SQ)
            AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled,
                targetTransform.Position, interact.range * 0.9f);
    }

    private void ApplySatisfaction(
        Entity                                    target,
        ref DynamicBuffer<MotivationChangeRequest> changeRequests)
    {
        if (!satisfactionLookup.TryGetBuffer(target, out DynamicBuffer<MotivationSatisfaction> providerSatisfactions))
            return;

        for (int i = 0; i < providerSatisfactions.Length; i++)
        {
            MotivationSatisfaction entry = providerSatisfactions[i];
            if (entry.motivationType == MotivationType.None)
                continue;

            changeRequests.Add(new MotivationChangeRequest
            {
                motivationType = entry.motivationType,
                changeType     = MotivationChangeType.Add,
                value          = entry.restorationAmount,
            });
        }
    }

    private void Terminate(
        ref PathRequest                  pathRequest,
        EnabledRefRW<SitAction>          sitEnabled,
        EnabledRefRW<ActionRequest>      actionRequestEnabled,
        EnabledRefRW<ActionTimer>        actionTimerEnabled,
        EnabledRefRW<PathRequest>        pathRequestEnabled,
        ref ReleaseRequest            releaseReq,
        EnabledRefRW<ReleaseRequest>  sitReleaseEnabled,
        Entity                           interactionTarget,
        bool                             wasArrived)
    {
        AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
        actionTimerEnabled.ValueRW   = false;
        sitEnabled.ValueRW           = false;
        actionRequestEnabled.ValueRW = true;

        if (wasArrived)
        {
            releaseReq.interactionEntity = interactionTarget;
            sitReleaseEnabled.ValueRW       = true;
        }
    }
}

// Single-threaded: decrements occupantCount on the interaction entity.
// Runs after SitJob.Complete() to avoid write conflicts on shared Interaction data.
[BurstCompile]
[WithAll(typeof(ReleaseRequest))]
public partial struct SitReleaseJob : IJobEntity
{
    public ComponentLookup<Interaction> interactionLookup;

    public void Execute(
        ref ReleaseRequest           releaseReq,
        EnabledRefRW<ReleaseRequest> releaseEnabled)
    {
        releaseEnabled.ValueRW = false;

        Entity target = releaseReq.interactionEntity;
        if (target == Entity.Null) return;

        if (interactionLookup.TryGetComponent(target, out Interaction interaction))
        {
            interaction.occupantCount = math.max(0, interaction.occupantCount - 1);
            interactionLookup[target] = interaction;
        }

        releaseReq.interactionEntity = Entity.Null;
    }
}
