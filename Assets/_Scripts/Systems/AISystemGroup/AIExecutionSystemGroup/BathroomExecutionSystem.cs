using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
[UpdateBefore(typeof(GenericInteractionExecutionSystem))]
public partial struct BladderExecutionSystem : ISystem
{
    private ComponentLookup<BladderMotivation> bladderLookup;
    private ComponentLookup<LocalTransform>    transformLookup;
    private ComponentLookup<NeedsAction>       needsActionLookup;
    private ComponentLookup<Dead>              deadLookup;
    private ComponentLookup<UnitAction>        unitActionLookup;
    private ComponentLookup<PathRequest>       pathRequestLookup;
    private ComponentLookup<PathfindingAgent>  pathfindingAgentLookup;
    private ComponentLookup<Movement>          unitMoverLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BladderInteraction>();

        bladderLookup          = state.GetComponentLookup<BladderMotivation>(false);
        transformLookup        = state.GetComponentLookup<LocalTransform>(true);
        needsActionLookup      = state.GetComponentLookup<NeedsAction>(false);
        deadLookup             = state.GetComponentLookup<Dead>(true);
        unitActionLookup       = state.GetComponentLookup<UnitAction>(false);
        pathRequestLookup      = state.GetComponentLookup<PathRequest>(false);
        pathfindingAgentLookup = state.GetComponentLookup<PathfindingAgent>(false);
        unitMoverLookup        = state.GetComponentLookup<Movement>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        bladderLookup.Update(ref state);
        transformLookup.Update(ref state);
        needsActionLookup.Update(ref state);
        deadLookup.Update(ref state);
        unitActionLookup.Update(ref state);
        pathRequestLookup.Update(ref state);
        pathfindingAgentLookup.Update(ref state);
        unitMoverLookup.Update(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;

        // Step 1: Request paths for newly assigned occupants
        state.Dependency = new BladderPathRequestJob
        {
            pathRequestLookup      = pathRequestLookup,
            pathfindingAgentLookup = pathfindingAgentLookup,
            unitMoverLookup        = unitMoverLookup,
        }.Schedule(state.Dependency);

        // Step 2: Check for arrival
        state.Dependency = new BladderArrivalJob
        {
            transformLookup = transformLookup,
        }.Schedule(state.Dependency);

        // Step 3: Handle completion
        state.Dependency = new BladderCompletionJob
        {
            deltaTime         = deltaTime,
            bladderLookup     = bladderLookup,
            needsActionLookup = needsActionLookup,
            deadLookup        = deadLookup,
            unitActionLookup  = unitActionLookup,
        }.Schedule(state.Dependency);
    }

    // -------------------------------------------------------
    // PATH REQUEST — fires once when occupants are assigned
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionTimer))]
    [WithDisabled(typeof(InteractionProvider))]
    [WithDisabled(typeof(InteractionHandled))]
    public partial struct BladderPathRequestJob : IJobEntity
    {
        public ComponentLookup<PathRequest>      pathRequestLookup;
        public ComponentLookup<PathfindingAgent> pathfindingAgentLookup;
        public ComponentLookup<Movement>         unitMoverLookup;

        public void Execute(
            in BladderInteraction bladderInteraction,
            in LocalTransform interactionTransform,
            in DynamicBuffer<InteractionOccupant> occupants,
            EnabledRefRW<InteractionHandled> interactionHandledEnabled)
        {
            if (occupants.Length == 0)
                return;

            for (int i = 0; i < occupants.Length; i++)
            {
                // Occupant entity IS the unit entity — request path directly.
                AIUtils.RequestPath(
                    occupants[i].entity,
                    interactionTransform.Position,
                    ref pathRequestLookup,
                    ref pathfindingAgentLookup,
                    ref unitMoverLookup);
            }

            interactionHandledEnabled.ValueRW = true;
        }
    }

    // -------------------------------------------------------
    // ARRIVAL — detect when the NPC reaches the interaction
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionTimer))]
    [WithDisabled(typeof(InteractionProvider))]
    [WithAll(typeof(InteractionHandled))]
    public partial struct BladderArrivalJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;

        public void Execute(
            in BladderInteraction bladderInteraction,
            in Interaction interaction,
            in LocalTransform interactionTransform,
            in DynamicBuffer<InteractionOccupant> occupants,
            ref InteractionTimer timer,
            EnabledRefRW<InteractionTimer> timerEnabled)
        {
            if (AIUtils.CheckArrival(in occupants, in interactionTransform,
                    interaction.interactionRange, ref transformLookup))
            {
                timer.elapsed        = 0f;
                timer.duration       = timer.maxTime;
                timerEnabled.ValueRW = true;
            }
        }
    }

    // -------------------------------------------------------
    // COMPLETION — tick timer, apply bladder effect, release
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionProvider))]
    [WithAll(typeof(InteractionHandled))]
    public partial struct BladderCompletionJob : IJobEntity
    {
        public float deltaTime;
        public ComponentLookup<BladderMotivation> bladderLookup;
        public ComponentLookup<NeedsAction>       needsActionLookup;
        [ReadOnly] public ComponentLookup<Dead>   deadLookup;
        public ComponentLookup<UnitAction>        unitActionLookup;

        public void Execute(
            in BladderInteraction bladderInteraction,
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

            // Apply bladder restoration — occupant entity IS the unit, has BladderMotivation.
            for (int i = 0; i < occupants.Length; i++)
            {
                Entity unitEntity = occupants[i].entity;

                if (bladderLookup.HasComponent(unitEntity))
                    bladderLookup[unitEntity] = new BladderMotivation { value = 100 };
            }

            AIUtils.ReleaseOccupants(occupants, ref needsActionLookup, ref unitActionLookup, ref deadLookup);

            timer.elapsed                      = 0f;
            timerEnabled.ValueRW               = false;
            interactionProviderEnabled.ValueRW = true;
            interactionHandledEnabled.ValueRW  = false;
        }
    }
}
