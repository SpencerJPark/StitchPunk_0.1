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
    private ComponentLookup<TargetPositionPathQueued> targetPositionLookup;
    private ComponentLookup<UnitAction> unitActionLookup;
    private ComponentLookup<BrainLink> brainLinkLookup;
    private ComponentLookup<NeedsAction> needsActionLookup;
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Interaction>();

        targetPositionLookup = state.GetComponentLookup<TargetPositionPathQueued>(false);
        unitActionLookup = state.GetComponentLookup<UnitAction>(false);
        brainLinkLookup = state.GetComponentLookup<BrainLink>(true);
        needsActionLookup = state.GetComponentLookup<NeedsAction>(false);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        targetPositionLookup.Update(ref state);
        unitActionLookup.Update(ref state);
        brainLinkLookup.Update(ref state);
        needsActionLookup.Update(ref state);
        transformLookup.Update(ref state);

        state.Dependency = new InteractionAssignmentJob
        {
            targetPositionLookup = targetPositionLookup,
            unitActionLookup = unitActionLookup,
            brainLinkLookup = brainLinkLookup,
            needsActionLookup = needsActionLookup
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
public partial struct InteractionAssignmentJob : IJobEntity
{
    public ComponentLookup<TargetPositionPathQueued> targetPositionLookup;
    public ComponentLookup<UnitAction> unitActionLookup;
    [ReadOnly] public ComponentLookup<BrainLink> brainLinkLookup;
    public ComponentLookup<NeedsAction> needsActionLookup;

    public void Execute(
        in BladderInteraction bladderInteraction,
        in Interaction interaction,
        in LocalTransform interactionTransform,
        DynamicBuffer<InteractionOccupant> occupants,
        EnabledRefRW<InteractionProvider> interactionProviderEnabled)
    {
        if (occupants.Length == 0)
            return;

        AIUtil.SelectWinners(occupants, interaction.maxOccupants, ref needsActionLookup);
        AIUtil.AssignWinners(in occupants, interactionTransform.Position, interaction.actionType,
            ref brainLinkLookup, ref targetPositionLookup, ref unitActionLookup);

        interactionProviderEnabled.ValueRW = false;
    }
}