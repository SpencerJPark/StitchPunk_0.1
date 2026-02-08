using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
[UpdateAfter(typeof(AIExecutionSystem))]
public partial struct OccupancyClaimSystem : ISystem
{
    private BufferLookup<OccupantEntity> occupantLookup;
    private ComponentLookup<Interactable> interactableLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        occupantLookup = state.GetBufferLookup<OccupantEntity>(false);
        interactableLookup = state.GetComponentLookup<Interactable>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        occupantLookup.Update(ref state);
        interactableLookup.Update(ref state);

        state.Dependency = new OccupancyClaimJob
        {
            occupantLookup = occupantLookup,
            interactableLookup = interactableLookup
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct OccupancyClaimJob : IJobEntity
{
    [NativeDisableParallelForRestriction] public BufferLookup<OccupantEntity> occupantLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<Interactable> interactableLookup;

    public void Execute(
        in CurrentInteraction currentInteraction,
        Entity entity)
    {
        Entity target = currentInteraction.target;

        if (target == Entity.Null)
            return;

        if (!currentInteraction.isInRange)
            return;

        if (!occupantLookup.TryGetBuffer(target, out DynamicBuffer<OccupantEntity> occupants))
            return;

        // Check if already claimed
        for (int i = 0; i < occupants.Length; i++)
        {
            if (occupants[i].entity == entity)
                return;
        }

        // Try to claim
        if (!interactableLookup.TryGetComponent(target, out Interactable interactable))
            return;

        if (interactable.currentOccupants >= interactable.maxOccupants)
            return;

        occupants.Add(new OccupantEntity { entity = entity });
        interactable.currentOccupants++;
        interactableLookup[target] = interactable;
    }
}