using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AISelectionSystemGroup))]
public partial struct InteractionAssignmentSystem : ISystem
{
    private ComponentLookup<BodyLink> bodyLinkLookup;
    private ComponentLookup<UnitAction> unitActionLookup;
    private ComponentLookup<NeedsAction> needsActionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Interaction>();

        bodyLinkLookup = state.GetComponentLookup<BodyLink>(true);
        unitActionLookup = state.GetComponentLookup<UnitAction>(false);
        needsActionLookup = state.GetComponentLookup<NeedsAction>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        bodyLinkLookup.Update(ref state);
        unitActionLookup.Update(ref state);
        needsActionLookup.Update(ref state);

        state.Dependency = new AssignmentJob
        {
            bodyLinkLookup = bodyLinkLookup,
            unitActionLookup = unitActionLookup,
            needsActionLookup = needsActionLookup,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(InteractionProvider))]
    public partial struct AssignmentJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<BodyLink> bodyLinkLookup;
        public ComponentLookup<UnitAction> unitActionLookup;
        public ComponentLookup<NeedsAction> needsActionLookup;

        public void Execute(
            in Interaction interaction,
            in LocalTransform interactionTransform,
            DynamicBuffer<InteractionOccupant> occupants,
            EnabledRefRW<InteractionProvider> providerEnabled)
        {
            if (occupants.Length == 0)
                return;

            // Select winners based on score
            AIUtils.SelectWinners(occupants, interaction.maxOccupants, ref needsActionLookup);

            if (occupants.Length == 0)
                return;

            // Assign winners to move toward interaction using new pathfinding
            AIUtils.AssignWinners(
                in occupants,
                interaction.actionType,
                ref bodyLinkLookup,
                ref unitActionLookup);

            // Disable provider while being used
            providerEnabled.ValueRW = false;
        }
    }
}