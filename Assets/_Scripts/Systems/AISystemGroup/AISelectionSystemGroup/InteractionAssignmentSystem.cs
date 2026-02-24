using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AISelectionSystemGroup))]
[UpdateAfter(typeof(ActionSelectionSystem))]
public partial struct InteractionAssignmentSystem : ISystem
{
    private ComponentLookup<BodyLink> brainLinkLookup;
    private ComponentLookup<PathRequest> pathRequestLookup;
    private ComponentLookup<PathfindingAgent> pathfindingAgentLookup;
    private ComponentLookup<UnitAction> unitActionLookup;
    private ComponentLookup<NeedsAction> needsActionLookup;
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<UnitMover> unitMoverLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Interaction>();

        brainLinkLookup = state.GetComponentLookup<BodyLink>(true);
        pathRequestLookup = state.GetComponentLookup<PathRequest>(false);
        pathfindingAgentLookup = state.GetComponentLookup<PathfindingAgent>(false);
        unitActionLookup = state.GetComponentLookup<UnitAction>(false);
        needsActionLookup = state.GetComponentLookup<NeedsAction>(false);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        unitMoverLookup = state.GetComponentLookup<UnitMover>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        brainLinkLookup.Update(ref state);
        pathRequestLookup.Update(ref state);
        pathfindingAgentLookup.Update(ref state);
        unitActionLookup.Update(ref state);
        needsActionLookup.Update(ref state);
        transformLookup.Update(ref state);
        unitMoverLookup.Update(ref state);

        state.Dependency = new AssignmentJob
        {
            brainLinkLookup = brainLinkLookup,
            pathRequestLookup = pathRequestLookup,
            pathfindingAgentLookup = pathfindingAgentLookup,
            unitActionLookup = unitActionLookup,
            needsActionLookup = needsActionLookup,
            transformLookup = transformLookup,
            unitMoverLookup = unitMoverLookup
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(InteractionProvider))]
    public partial struct AssignmentJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<BodyLink> brainLinkLookup;
        public ComponentLookup<PathRequest> pathRequestLookup;
        public ComponentLookup<PathfindingAgent> pathfindingAgentLookup;
        public ComponentLookup<UnitAction> unitActionLookup;
        public ComponentLookup<NeedsAction> needsActionLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        public ComponentLookup<UnitMover> unitMoverLookup;

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
                interactionTransform.Position,
                interaction.actionType,
                ref brainLinkLookup,
                ref pathRequestLookup,
                ref pathfindingAgentLookup,
                ref unitActionLookup,
                ref unitMoverLookup);

            // Disable provider while being used
            providerEnabled.ValueRW = false;
        }
    }
}