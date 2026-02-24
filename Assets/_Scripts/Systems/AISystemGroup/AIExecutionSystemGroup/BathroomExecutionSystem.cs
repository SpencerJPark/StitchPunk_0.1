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
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<BodyLink> brainLinkLookup;
    private ComponentLookup<NeedsAction> needsActionLookup;
    private ComponentLookup<UnitAction> unitActionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BladderInteraction>();

        bladderLookup = state.GetComponentLookup<BladderMotivation>(false);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        brainLinkLookup = state.GetComponentLookup<BodyLink>(true);
        needsActionLookup = state.GetComponentLookup<NeedsAction>(false);
        unitActionLookup = state.GetComponentLookup<UnitAction>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        bladderLookup.Update(ref state);
        transformLookup.Update(ref state);
        brainLinkLookup.Update(ref state);
        needsActionLookup.Update(ref state);
        unitActionLookup.Update(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new BladderArrivalJob
        {
            transformLookup = transformLookup,
            brainLinkLookup = brainLinkLookup
        }.Schedule(state.Dependency);

        state.Dependency = new BladderCompletionJob
        {
            deltaTime = deltaTime,
            bladderLookup = bladderLookup,
            needsActionLookup = needsActionLookup,
            unitActionLookup = unitActionLookup,
            brainLinkLookup = brainLinkLookup
        }.Schedule(state.Dependency);
    }

    // -------------------------------------------------------
    // ARRIVAL — detect when the NPC reaches the interaction, start timer
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionTimer))]
    [WithDisabled(typeof(InteractionProvider))]
    public partial struct BladderArrivalJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        [ReadOnly] public ComponentLookup<BodyLink> brainLinkLookup;

        public void Execute(
            in BladderInteraction bladderInteraction,
            in Interaction interaction,
            in LocalTransform interactionTransform,
            in DynamicBuffer<InteractionOccupant> occupants,
            ref InteractionTimer timer,
            EnabledRefRW<InteractionTimer> timerEnabled,
            EnabledRefRW<InteractionHandled> interactionHandledEnabled)
        {
            if (AIUtils.CheckArrival(in occupants, in interactionTransform, interaction.interactionRange,
                    ref brainLinkLookup, ref transformLookup))
            {
                timer.elapsed = 0f;
                timer.duration = timer.maxTime;
                timerEnabled.ValueRW = true;
                interactionHandledEnabled.ValueRW = true;
            }
        }
    }

    // -------------------------------------------------------
    // COMPLETION — tick timer, apply bladder effect, release NPCs
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionProvider))]
    [WithAll(typeof(InteractionHandled))]
    public partial struct BladderCompletionJob : IJobEntity
    {
        public float deltaTime;
        public ComponentLookup<BladderMotivation> bladderLookup;
        public ComponentLookup<NeedsAction> needsActionLookup;
        public ComponentLookup<UnitAction> unitActionLookup;
        [ReadOnly] public ComponentLookup<BodyLink> brainLinkLookup;

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

            // Apply bladder restoration to all occupants
            for (int i = 0; i < occupants.Length; i++)
            {
                Entity brainEntity = occupants[i].entity;

                if (bladderLookup.HasComponent(brainEntity))
                {
                    bladderLookup[brainEntity] = new BladderMotivation { value = 100 };
                }
            }

            // Release and cleanup
            AIUtils.ReleaseOccupants(occupants, ref needsActionLookup, ref unitActionLookup, ref brainLinkLookup);

            timer.elapsed = 0f;
            timerEnabled.ValueRW = false;
            interactionProviderEnabled.ValueRW = true;
            interactionHandledEnabled.ValueRW = false;
        }
    }
}