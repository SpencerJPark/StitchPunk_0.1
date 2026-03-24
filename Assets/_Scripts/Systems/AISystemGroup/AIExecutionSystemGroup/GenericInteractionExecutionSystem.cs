using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// Generic fallback execution system that handles interactions NOT claimed by specific systems.
/// 
/// Runs LAST in the execution group. Only processes interactions where:
/// - InteractionHandled is DISABLED (no specific system claimed it)
/// - Provider is disabled (NPC is assigned)
/// - Has occupants
/// 
/// Behavior:
/// - Requests path to interaction
/// - Detects arrival
/// - Waits 5 seconds (default)
/// - Releases NPCs
/// 
/// This prevents NPCs from getting stuck on interactions that don't have
/// custom execution systems yet.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup), OrderLast = true)]
public partial struct GenericInteractionExecutionSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<BodyLink> brainLinkLookup;
    private ComponentLookup<NeedsAction> needsActionLookup;
    private ComponentLookup<Dead> deadLookup;
    private ComponentLookup<UnitAction> unitActionLookup;
    private ComponentLookup<PathRequest> pathRequestLookup;
    private ComponentLookup<PathfindingAgent> pathfindingAgentLookup;
    private ComponentLookup<Movement> unitMoverLookup;

    private const float DEFAULT_DURATION = 5f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Interaction>();

        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        brainLinkLookup = state.GetComponentLookup<BodyLink>(true);
        needsActionLookup = state.GetComponentLookup<NeedsAction>(false);
        deadLookup = state.GetComponentLookup<Dead>(true);
        unitActionLookup = state.GetComponentLookup<UnitAction>(false);
        pathRequestLookup = state.GetComponentLookup<PathRequest>(false);
        pathfindingAgentLookup = state.GetComponentLookup<PathfindingAgent>(false);
        unitMoverLookup = state.GetComponentLookup<Movement>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        brainLinkLookup.Update(ref state);
        needsActionLookup.Update(ref state);
        deadLookup.Update(ref state);
        unitActionLookup.Update(ref state);
        pathRequestLookup.Update(ref state);
        pathfindingAgentLookup.Update(ref state);
        unitMoverLookup.Update(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;

        // Step 1: Request paths for newly assigned occupants
        state.Dependency = new GenericPathRequestJob
        {
            brainLinkLookup = brainLinkLookup,
            pathRequestLookup = pathRequestLookup,
            pathfindingAgentLookup = pathfindingAgentLookup,
            unitMoverLookup = unitMoverLookup
        }.Schedule(state.Dependency);

        // Step 2: Check for arrival
        state.Dependency = new GenericArrivalJob
        {
            transformLookup = transformLookup,
            brainLinkLookup = brainLinkLookup,
            defaultDuration = DEFAULT_DURATION
        }.Schedule(state.Dependency);

        // Step 3: Handle completion
        state.Dependency = new GenericCompletionJob
        {
            deltaTime = deltaTime,
            needsActionLookup = needsActionLookup,
            deadLookup = deadLookup,
            unitActionLookup = unitActionLookup,
            brainLinkLookup = brainLinkLookup
        }.Schedule(state.Dependency);
    }

    // -------------------------------------------------------
    // PATH REQUEST — fires once when occupants are assigned
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionTimer))]
    [WithDisabled(typeof(InteractionProvider))]
    [WithDisabled(typeof(InteractionHandled))]
    public partial struct GenericPathRequestJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<BodyLink> brainLinkLookup;
        public ComponentLookup<PathRequest> pathRequestLookup;
        public ComponentLookup<PathfindingAgent> pathfindingAgentLookup;
        public ComponentLookup<Movement> unitMoverLookup;

        public void Execute(
            in LocalTransform interactionTransform,
            in DynamicBuffer<InteractionOccupant> occupants,
            EnabledRefRW<InteractionHandled> interactionHandledEnabled)
        {
            if (occupants.Length == 0)
                return;

            for (int i = 0; i < occupants.Length; i++)
            {
                Entity brainEntity = occupants[i].entity;

                if (!brainLinkLookup.TryGetComponent(brainEntity, out BodyLink brainLink))
                    continue;

                AIUtils.RequestPath(
                    brainLink.body,
                    interactionTransform.Position,
                    ref pathRequestLookup,
                    ref pathfindingAgentLookup,
                    ref unitMoverLookup);
            }

            interactionHandledEnabled.ValueRW = true;
        }
    }

    // -------------------------------------------------------
    // ARRIVAL — detect when NPC reaches interaction, start timer
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionTimer))]
    [WithDisabled(typeof(InteractionProvider))]
    [WithAll(typeof(InteractionHandled))]
    public partial struct GenericArrivalJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        [ReadOnly] public ComponentLookup<BodyLink> brainLinkLookup;
        public float defaultDuration;

        public void Execute(
            in Interaction interaction,
            in LocalTransform interactionTransform,
            in DynamicBuffer<InteractionOccupant> occupants,
            ref InteractionTimer timer,
            EnabledRefRW<InteractionTimer> timerEnabled)
        {
            if (AIUtils.CheckArrival(in occupants, in interactionTransform, interaction.interactionRange,
                    ref brainLinkLookup, ref transformLookup))
            {
                timer.elapsed = 0f;
                timer.duration = timer.maxTime > 0f ? timer.maxTime : defaultDuration;
                timerEnabled.ValueRW = true;
            }
        }
    }

    // -------------------------------------------------------
    // COMPLETION — tick timer, release NPCs when done
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionProvider))]
    [WithAll(typeof(InteractionHandled))]
    public partial struct GenericCompletionJob : IJobEntity
    {
        public float deltaTime;
        public ComponentLookup<NeedsAction> needsActionLookup;
        [ReadOnly] public ComponentLookup<Dead> deadLookup;
        public ComponentLookup<UnitAction> unitActionLookup;
        [ReadOnly] public ComponentLookup<BodyLink> brainLinkLookup;

        public void Execute(
            in Interaction interaction,
            DynamicBuffer<InteractionOccupant> occupants,
            ref InteractionTimer timer,
            EnabledRefRW<InteractionTimer> timerEnabled,
            EnabledRefRW<InteractionProvider> interactionProviderEnabled,
            EnabledRefRW<InteractionHandled> interactionHandledEnabled)
        {
            if (!timerEnabled.ValueRO)
                return;

            timer.elapsed += deltaTime;

            if (timer.elapsed < timer.duration)
                return;

            // Release and cleanup — dead units are cleared but NeedsAction is not re-enabled.
            AIUtils.ReleaseOccupants(occupants, ref needsActionLookup, ref unitActionLookup, ref brainLinkLookup, ref deadLookup);

            timer.elapsed = 0f;
            timerEnabled.ValueRW = false;
            interactionProviderEnabled.ValueRW = true;
            interactionHandledEnabled.ValueRW = false;
        }
    }
}