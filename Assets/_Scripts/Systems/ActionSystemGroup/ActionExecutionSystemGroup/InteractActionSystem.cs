using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Handles all interaction-type actions (Bathroom, Eat, Sleep, Drink, etc.).
// Multiple ActionTypes route to InteractAction via the function pointer table;
// CurrentAction.actionType is the key for animation and interaction blob lookup.
//
// States (ActionTimer enabled state as flag):
//   Pathing     (InteractAction enabled, ActionTimer disabled) — path toward target
//   Interacting (InteractAction enabled, ActionTimer enabled)  — animate + count down
//   Complete    — apply satisfaction, flag occupant release, disable InteractAction,
//                 re-enable ActionRequest
[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup))]
public partial struct InteractActionSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Interaction>    interactionReadLookup;
    private ComponentLookup<Interaction>    interactionWriteLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<InteractionLibrary>();
        state.RequireForUpdate<UnitDataLibrary>();

        transformLookup        = state.GetComponentLookup<LocalTransform>(true);
        interactionReadLookup  = state.GetComponentLookup<Interaction>(true);
        interactionWriteLookup = state.GetComponentLookup<Interaction>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        interactionReadLookup.Update(ref state);
        interactionWriteLookup.Update(ref state);

        InteractionLibrary interactionLib = SystemAPI.GetSingleton<InteractionLibrary>();
        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        state.Dependency = new InteractJob
        {
            deltaTime          = SystemAPI.Time.DeltaTime,
            transformLookup    = transformLookup,
            interactionLookup  = interactionReadLookup,
            interactionLibrary = interactionLib.library,
            unitLibrary        = unitLibrary,
        }.ScheduleParallel(state.Dependency);

        state.Dependency.Complete();

        new InteractReleaseJob
        {
            interactionLookup = interactionWriteLookup,
        }.Run();
    }
}

[BurstCompile]
[WithAll(typeof(InteractAction))]
[WithDisabled(typeof(ActionRequest), typeof(ActionInterruptRequest))]
[WithPresent(typeof(Dead), typeof(PathRequest), typeof(ActionTimer),
             typeof(ReleaseRequest), typeof(AnimationRequest))]
public partial struct InteractJob : IJobEntity
{
    private const float REPATH_DIST_SQ = 1.0f;

    public float deltaTime;

    [ReadOnly] public ComponentLookup<LocalTransform>            transformLookup;
    [ReadOnly] public ComponentLookup<Interaction>               interactionLookup;
    [ReadOnly] public BlobAssetReference<InteractionLibraryBlob> interactionLibrary;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>        unitLibrary;

    public void Execute(
        in LocalTransform                          localTransform,
        in UnitData                                unitData,
        in CurrentAction                           action,
        ref PathRequest                            pathRequest,
        ref ActionTimer                            actionTimer,
        ref DynamicBuffer<SetAnimation>            setAnimations,
        ref DynamicBuffer<MotivationChangeRequest> changeRequests,
        ref ReleaseRequest                         releaseReq,
        EnabledRefRW<InteractAction>               interactEnabled,
        EnabledRefRW<ActionRequest>                actionRequestEnabled,
        EnabledRefRW<ActionTimer>                  actionTimerEnabled,
        EnabledRefRW<PathRequest>                  pathRequestEnabled,
        EnabledRefRW<ReleaseRequest>               releaseEnabled,
        EnabledRefRW<AnimationRequest>             animRequestEnabled,
        EnabledRefRO<Dead>                         dead)
    {
        Entity target = action.targetEntity;

        if (dead.ValueRO)
        {
            Terminate(ref pathRequest, interactEnabled, actionRequestEnabled,
                actionTimerEnabled, pathRequestEnabled, ref releaseReq,
                releaseEnabled, target, wasArrived: false);
            return;
        }

        // --- Interacting state ---
        if (actionTimerEnabled.ValueRO)
        {
            actionTimer.time -= deltaTime;
            if (actionTimer.time <= 0f)
            {
                ApplySatisfaction(target, ref changeRequests);
                Terminate(ref pathRequest, interactEnabled, actionRequestEnabled,
                    actionTimerEnabled, pathRequestEnabled, ref releaseReq,
                    releaseEnabled, target, wasArrived: true);
            }
            return;
        }

        // --- Pathing state: validate target ---
        if (!transformLookup.TryGetComponent(target, out LocalTransform targetTransform))
        {
            Terminate(ref pathRequest, interactEnabled, actionRequestEnabled,
                actionTimerEnabled, pathRequestEnabled, ref releaseReq,
                releaseEnabled, target, wasArrived: false);
            return;
        }

        if (!interactionLookup.TryGetComponent(target, out Interaction interact))
        {
            Terminate(ref pathRequest, interactEnabled, actionRequestEnabled,
                actionTimerEnabled, pathRequestEnabled, ref releaseReq,
                releaseEnabled, target, wasArrived: false);
            return;
        }

        if (!interactionLookup.IsComponentEnabled(target))
        {
            Terminate(ref pathRequest, interactEnabled, actionRequestEnabled,
                actionTimerEnabled, pathRequestEnabled, ref releaseReq,
                releaseEnabled, target, wasArrived: false);
            return;
        }

        ref InteractionBlob blob = ref interactionLibrary.Value.interactions[(int)interact.actionType];

        // --- Arrival check ---
        float distSq  = math.distancesq(localTransform.Position, targetTransform.Position);
        float rangeSq = blob.range * blob.range;

        if (distSq <= rangeSq)
        {
            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
            actionTimerEnabled.ValueRW = true;
            actionTimer.time           = blob.duration;

            int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
            AnimationType animType = unitIndex >= 0
                ? AIUtils.GetAnimationByAction(ref unitLibrary.Value.units[unitIndex], action.actionType)
                : AnimationType.None;

            if (animType != AnimationType.None)
            {
                setAnimations.Add(new SetAnimation
                {
                    layer     = AnimationLayerType.Action,
                    animation = animType,
                    speed     = 1f,
                });
                animRequestEnabled.ValueRW = true;
            }
            return;
        }

        // --- Repath if target has moved ---
        if (math.distancesq(pathRequest.targetPosition, targetTransform.Position) >= REPATH_DIST_SQ)
            AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled,
                targetTransform.Position, blob.range * 0.9f);
    }

    private void ApplySatisfaction(
        Entity                                     target,
        ref DynamicBuffer<MotivationChangeRequest> changeRequests)
    {
        if (!interactionLookup.TryGetComponent(target, out Interaction interact)) return;

        ref InteractionBlob blob = ref interactionLibrary.Value.interactions[(int)interact.actionType];

        if (blob.satisfiedNeed == NeedType.None) return;

        changeRequests.Add(new MotivationChangeRequest
        {
            needType   = blob.satisfiedNeed,
            changeType = MotivationChangeType.Add,
            value      = blob.restorationAmount,
        });
    }

    private void Terminate(
        ref PathRequest              pathRequest,
        EnabledRefRW<InteractAction> interactEnabled,
        EnabledRefRW<ActionRequest>  actionRequestEnabled,
        EnabledRefRW<ActionTimer>    actionTimerEnabled,
        EnabledRefRW<PathRequest>    pathRequestEnabled,
        ref ReleaseRequest           releaseReq,
        EnabledRefRW<ReleaseRequest> releaseEnabled,
        Entity                       interactionTarget,
        bool                         wasArrived)
    {
        AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
        actionTimerEnabled.ValueRW   = false;
        interactEnabled.ValueRW      = false;
        actionRequestEnabled.ValueRW = true;

        if (wasArrived)
        {
            releaseReq.interactionEntity = interactionTarget;
            releaseEnabled.ValueRW       = true;
        }
    }
}

[BurstCompile]
[WithAll(typeof(ReleaseRequest))]
public partial struct InteractReleaseJob : IJobEntity
{
    public ComponentLookup<Interaction> interactionLookup;

    public void Execute(
        ref ReleaseRequest                   releaseReq,
        ref DynamicBuffer<RecentInteraction> recentInteractions,
        EnabledRefRW<ReleaseRequest>         releaseEnabled)
    {
        releaseEnabled.ValueRW = false;

        Entity target = releaseReq.interactionEntity;
        if (target == Entity.Null) return;

        if (interactionLookup.TryGetComponent(target, out Interaction interaction))
        {
            interaction.currentOccupants = math.max(0, interaction.currentOccupants - 1);
            interactionLookup[target]    = interaction;
        }

        if (recentInteractions.Length >= 2) recentInteractions.RemoveAt(0);
        recentInteractions.Add(new RecentInteraction { entity = target });

        releaseReq.interactionEntity = Entity.Null;
    }
}
