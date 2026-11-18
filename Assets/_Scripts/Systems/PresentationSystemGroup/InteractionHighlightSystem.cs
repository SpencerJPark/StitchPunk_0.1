using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

[UpdateInGroup(typeof(PresentationSystemGroup))]
partial struct InteractionHighlightSystem : ISystem
{
    private Entity playerEntity;
    private Entity previousEntity;
    private Entity nextEntity;

    private NativeList<Entity> highlightChildrenList;
    private EntityQuery interactionVisualQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();

        previousEntity = Entity.Null;
        nextEntity = Entity.Null;
        playerEntity = Entity.Null;

        highlightChildrenList = new NativeList<Entity>(32, Allocator.Persistent);

        interactionVisualQuery = SystemAPI.QueryBuilder()
            .WithAll<InteractableVisual, BaseParent>()
            .Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        highlightChildrenList.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        // A save's moment commit destroys and recreates units, so cached handles can be dead.
        if (playerEntity == Entity.Null
            || !state.EntityManager.Exists(playerEntity)
            || !state.EntityManager.HasComponent<Player>(playerEntity))
        {
            playerEntity = SystemAPI.GetSingletonEntity<Player>();
        }

        if (previousEntity != Entity.Null && !state.EntityManager.Exists(previousEntity))
        {
            highlightChildrenList.Clear();
            previousEntity = Entity.Null;
        }

        FindNextTarget(ref state);

        if (nextEntity == Entity.Null)
        {
            if (previousEntity != Entity.Null)
            {
                ResetPreviousChildren(ref state);
                previousEntity = Entity.Null;
            }
            return;
        }

        if (previousEntity == nextEntity)
        {
            return;
        }

        ResetPreviousChildren(ref state);
        previousEntity = nextEntity;
        GatherNewChildren(ref state);
        SetNextChildren(ref state);
    }

    [BurstCompile]
    private void FindNextTarget(ref SystemState state)
    {
        bool hasTarget = state.EntityManager.HasComponent<Target>(playerEntity)
            && state.EntityManager.IsComponentEnabled<Target>(playerEntity);
        nextEntity = hasTarget
            ? state.EntityManager.GetComponentData<Target>(playerEntity).entity
            : Entity.Null;
        if (nextEntity != Entity.Null && !state.EntityManager.Exists(nextEntity))
        {
            nextEntity = Entity.Null;
        }
    }

    private void ResetPreviousChildren(ref SystemState state)
    {
        // Reset children
        for (int i = 0; i < highlightChildrenList.Length; i++)
        {
            Entity child = highlightChildrenList[i];
            if (!state.EntityManager.Exists(child)) continue;
            if (!state.EntityManager.HasComponent<InteractableVisual>(child)) continue;

            InteractableVisual visual = state.EntityManager.GetComponentData<InteractableVisual>(child);
            visual.value = 0f;
            state.EntityManager.SetComponentData(child, visual);
        }

        // Also reset the entity itself if it has InteractableVisual directly (e.g. simple objects, not unit hierarchies)
        if (state.EntityManager.Exists(previousEntity) &&
            state.EntityManager.HasComponent<InteractableVisual>(previousEntity))
        {
            InteractableVisual visual = state.EntityManager.GetComponentData<InteractableVisual>(previousEntity);
            visual.value = 0f;
            state.EntityManager.SetComponentData(previousEntity, visual);
        }
    }

    private void GatherNewChildren(ref SystemState state)
    {
        highlightChildrenList.Clear();

        int totalCount = interactionVisualQuery.CalculateEntityCount();
        if (totalCount == 0) return;

        if (highlightChildrenList.Capacity < totalCount)
            highlightChildrenList.Capacity = totalCount;

        state.Dependency = new GatherInteractionVisualChildrenJob
        {
            parentEntity = nextEntity,
            childrenList = highlightChildrenList.AsParallelWriter(),
        }.ScheduleParallel(interactionVisualQuery, state.Dependency);

        state.Dependency.Complete();
    }

    private void SetNextChildren(ref SystemState state)
    {
        // Highlight children (unit hierarchy pattern)
        for (int i = 0; i < highlightChildrenList.Length; i++)
        {
            Entity child = highlightChildrenList[i];
            if (!state.EntityManager.Exists(child)) continue;
            if (!state.EntityManager.HasComponent<InteractableVisual>(child)) continue;

            InteractableVisual visual = state.EntityManager.GetComponentData<InteractableVisual>(child);
            visual.value = 1f;
            state.EntityManager.SetComponentData(child, visual);
        }

        // Also highlight the entity itself if it has InteractableVisual directly (e.g. simple objects, not unit hierarchies)
        if (state.EntityManager.Exists(nextEntity) &&
            state.EntityManager.HasComponent<InteractableVisual>(nextEntity))
        {
            InteractableVisual visual = state.EntityManager.GetComponentData<InteractableVisual>(nextEntity);
            visual.value = 1f;
            state.EntityManager.SetComponentData(nextEntity, visual);
        }
    }
}

[BurstCompile]
public partial struct GatherInteractionVisualChildrenJob : IJobEntity
{
    public Entity parentEntity;
    public NativeList<Entity>.ParallelWriter childrenList;

    public void Execute(in BaseParent baseParent, Entity entity)
    {
        if (baseParent.baseParentEntity == parentEntity)
        {
            childrenList.AddNoResize(entity);
        }
    }
}
