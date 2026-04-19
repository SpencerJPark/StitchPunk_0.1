using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AISelectionSystemGroup))]
public partial struct InteractionAssignmentSystem : ISystem
{
    private ComponentLookup<UnitAction>          unitActionLookup;
    private ComponentLookup<NeedsAction>         needsActionLookup;
    private ComponentLookup<CurrentInteraction>  currentInteractionLookup;
    private ComponentLookup<PickupTask>          pickupLookup;
    private ComponentLookup<BuildTask>           buildLookup;
    private ComponentLookup<DrinkTask>           drinkLookup;
    private ComponentLookup<EatTask>             eatLookup;
    private ComponentLookup<TouchAnimateTask>    touchAnimateLookup;
    private ComponentLookup<WanderAreaTask>      wanderAreaLookup;
    private ComponentLookup<SitTask>             sitLookup;
    private ComponentLookup<RepairTask>          repairLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Interaction>();

        unitActionLookup         = state.GetComponentLookup<UnitAction>(false);
        needsActionLookup        = state.GetComponentLookup<NeedsAction>(false);
        currentInteractionLookup = state.GetComponentLookup<CurrentInteraction>(false);
        pickupLookup             = state.GetComponentLookup<PickupTask>(false);
        buildLookup              = state.GetComponentLookup<BuildTask>(false);
        drinkLookup              = state.GetComponentLookup<DrinkTask>(false);
        eatLookup                = state.GetComponentLookup<EatTask>(false);
        touchAnimateLookup       = state.GetComponentLookup<TouchAnimateTask>(false);
        wanderAreaLookup         = state.GetComponentLookup<WanderAreaTask>(false);
        sitLookup                = state.GetComponentLookup<SitTask>(false);
        repairLookup             = state.GetComponentLookup<RepairTask>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        unitActionLookup.Update(ref state);
        needsActionLookup.Update(ref state);
        currentInteractionLookup.Update(ref state);
        pickupLookup.Update(ref state);
        buildLookup.Update(ref state);
        drinkLookup.Update(ref state);
        eatLookup.Update(ref state);
        touchAnimateLookup.Update(ref state);
        wanderAreaLookup.Update(ref state);
        sitLookup.Update(ref state);
        repairLookup.Update(ref state);

        state.Dependency = new AssignmentJob
        {
            unitActionLookup         = unitActionLookup,
            needsActionLookup        = needsActionLookup,
            currentInteractionLookup = currentInteractionLookup,
            pickupLookup             = pickupLookup,
            buildLookup              = buildLookup,
            drinkLookup              = drinkLookup,
            eatLookup                = eatLookup,
            touchAnimateLookup       = touchAnimateLookup,
            wanderAreaLookup         = wanderAreaLookup,
            sitLookup                = sitLookup,
            repairLookup             = repairLookup,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(InteractionProvider))]
    public partial struct AssignmentJob : IJobEntity
    {
        public ComponentLookup<UnitAction>          unitActionLookup;
        public ComponentLookup<NeedsAction>         needsActionLookup;
        public ComponentLookup<CurrentInteraction>  currentInteractionLookup;
        public ComponentLookup<PickupTask>          pickupLookup;
        public ComponentLookup<BuildTask>           buildLookup;
        public ComponentLookup<DrinkTask>           drinkLookup;
        public ComponentLookup<EatTask>             eatLookup;
        public ComponentLookup<TouchAnimateTask>    touchAnimateLookup;
        public ComponentLookup<WanderAreaTask>      wanderAreaLookup;
        public ComponentLookup<SitTask>             sitLookup;
        public ComponentLookup<RepairTask>          repairLookup;

        public void Execute(
            Entity interactionEntity,
            in Interaction interaction,
            in InteractionKindData kindData,
            in LocalTransform interactionTransform,
            DynamicBuffer<InteractionOccupant> occupants,
            EnabledRefRW<InteractionProvider> providerEnabled)
        {
            if (occupants.Length == 0)
                return;

            // Select winners based on score — rejects losers and re-enables NeedsAction on them.
            AIUtils.SelectWinners(occupants, interaction.maxOccupants, ref needsActionLookup);

            if (occupants.Length == 0)
                return;

            // Assign winners — writes UnitAction + CurrentInteraction and enables the
            // kind-specific task component so the matching execution system picks it up.
            AIUtils.AssignWinners(
                interactionEntity,
                in occupants,
                interaction.actionType,
                kindData.kind,
                ref unitActionLookup,
                ref currentInteractionLookup,
                ref pickupLookup, ref buildLookup, ref drinkLookup, ref eatLookup,
                ref touchAnimateLookup, ref wanderAreaLookup, ref sitLookup, ref repairLookup);

            // Disable provider while being used.
            providerEnabled.ValueRW = false;
        }
    }
}
