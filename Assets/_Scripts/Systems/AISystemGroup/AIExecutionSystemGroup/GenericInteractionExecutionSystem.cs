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
    private ComponentLookup<BrainLink> brainLinkLookup;
    private ComponentLookup<NeedsAction> needsActionLookup;
    private ComponentLookup<UnitAction> unitActionLookup;

    private const float DEFAULT_DURATION = 5f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Interaction>();

        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        brainLinkLookup = state.GetComponentLookup<BrainLink>(true);
        needsActionLookup = state.GetComponentLookup<NeedsAction>(false);
        unitActionLookup = state.GetComponentLookup<UnitAction>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        brainLinkLookup.Update(ref state);
        needsActionLookup.Update(ref state);
        unitActionLookup.Update(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new GenericArrivalJob
        {
            transformLookup = transformLookup,
            brainLinkLookup = brainLinkLookup,
            defaultDuration = DEFAULT_DURATION
        }.Schedule(state.Dependency);

        state.Dependency = new GenericCompletionJob
        {
            deltaTime = deltaTime,
            needsActionLookup = needsActionLookup,
            unitActionLookup = unitActionLookup,
            brainLinkLookup = brainLinkLookup
        }.Schedule(state.Dependency);
    }

    // -------------------------------------------------------
    // ARRIVAL — detect when NPC reaches interaction, start timer
    // Only runs on interactions NOT handled by specific systems
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionTimer))]
    [WithDisabled(typeof(InteractionProvider))]
    [WithDisabled(typeof(InteractionHandled))]
    public partial struct GenericArrivalJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        [ReadOnly] public ComponentLookup<BrainLink> brainLinkLookup;
        public float defaultDuration;

        public void Execute(
            in Interaction interaction,
            in LocalTransform interactionTransform,
            in DynamicBuffer<InteractionOccupant> occupants,
            ref InteractionTimer timer,
            EnabledRefRW<InteractionTimer> timerEnabled,
            EnabledRefRW<InteractionHandled> interactionHandledEnabled)
        {
            if (AIUtil.CheckArrival(in occupants, in interactionTransform, interaction.interactionRange,
                    ref brainLinkLookup, ref transformLookup))
            {
                timer.elapsed = 0f;
                timer.duration = timer.maxTime > 0f ? timer.maxTime : defaultDuration;
                timerEnabled.ValueRW = true;
                interactionHandledEnabled.ValueRW = true;
            }
        }
    }

    // -------------------------------------------------------
    // COMPLETION — tick timer, release NPCs when done
    // Processes any interaction that the generic arrival job claimed
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionProvider))]
    [WithAll(typeof(InteractionHandled))]
    public partial struct GenericCompletionJob : IJobEntity
    {
        public float deltaTime;
        public ComponentLookup<NeedsAction> needsActionLookup;
        public ComponentLookup<UnitAction> unitActionLookup;
        [ReadOnly] public ComponentLookup<BrainLink> brainLinkLookup;

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

            // Release and cleanup
            AIUtil.ReleaseOccupants(occupants, ref needsActionLookup, ref unitActionLookup, ref brainLinkLookup);

            timer.elapsed = 0f;
            timerEnabled.ValueRW = false;
            interactionProviderEnabled.ValueRW = true;
            interactionHandledEnabled.ValueRW = false;
        }
    }
}