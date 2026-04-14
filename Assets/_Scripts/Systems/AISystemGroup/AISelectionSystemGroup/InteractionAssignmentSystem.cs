using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AISelectionSystemGroup))]
public partial struct InteractionAssignmentSystem : ISystem
{
    private ComponentLookup<UnitAction>  unitActionLookup;
    private ComponentLookup<NeedsAction> needsActionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Interaction>();

        unitActionLookup  = state.GetComponentLookup<UnitAction>(false);
        needsActionLookup = state.GetComponentLookup<NeedsAction>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        unitActionLookup.Update(ref state);
        needsActionLookup.Update(ref state);

        state.Dependency = new AssignmentJob
        {
            unitActionLookup  = unitActionLookup,
            needsActionLookup = needsActionLookup,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(InteractionProvider))]
    public partial struct AssignmentJob : IJobEntity
    {
        public ComponentLookup<UnitAction>  unitActionLookup;
        public ComponentLookup<NeedsAction> needsActionLookup;

        public void Execute(
            in Interaction interaction,
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

            // Assign winners — occupant entity IS the unit entity in the single-entity model.
            AIUtils.AssignWinners(in occupants, interaction.actionType, ref unitActionLookup);

            // Disable provider while being used.
            providerEnabled.ValueRW = false;
        }
    }
}
