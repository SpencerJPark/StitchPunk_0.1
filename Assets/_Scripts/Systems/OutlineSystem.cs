using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;

partial struct OutlineSystem : ISystem
{
    private Entity previousEntity;
    private Entity nextEntity;
    
    private NativeList<Entity> outlineEntityChildrenList;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        
        previousEntity = Entity.Null;
        nextEntity = Entity.Null;
        
        outlineEntityChildrenList = new NativeList<Entity>(Allocator.Persistent);
    }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        outlineEntityChildrenList.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        FindNextOutlined(ref state);
        
        if (nextEntity == Entity.Null)
        {
            return;
        }

        if (previousEntity == nextEntity)
        {
            return;
        }
        
        ResetPreviousChildrenOutline(ref state);
        
        previousEntity = nextEntity;
        
        GatherNewChildren(ref state);
        
        SetNextChildrenOutline(ref state);
    }

    private void FindNextOutlined(ref SystemState state)
    {
        nextEntity = SystemAPI.GetSingleton<Player>().interactableEntity;
    }

    private void ResetPreviousChildrenOutline(ref SystemState state)
    {
        // Set all children in the list to not outlined
        foreach (Entity childEntity in outlineEntityChildrenList)
        {
            if (!state.EntityManager.Exists(childEntity)) continue;
            
            // Disable OutlinedTag
            if (state.EntityManager.HasComponent<OutlinedTag>(childEntity))
            {
                state.EntityManager.SetComponentEnabled<OutlinedTag>(childEntity, false);
            }
            
            // Clear material RenderType (main thread operation)
            if (state.EntityManager.HasComponent<RenderMeshArray>(childEntity))
            {
                RenderMeshArray renderMeshArray = state.EntityManager.GetSharedComponentManaged<RenderMeshArray>(childEntity);
                var materials = renderMeshArray.Materials;
                
                foreach (var material in materials)
                {
                    if (material == null) continue;
                    material.SetOverrideTag("RenderType", "");
                }
            }
        }
    }
    
    private void GatherNewChildren(ref SystemState state)
    {
        outlineEntityChildrenList.Clear();
        new GatherOutlineEntityChildrenJob() {
            parentEntity = nextEntity,
            outlineEntityChildrenList = outlineEntityChildrenList.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency).Complete();
    }
    
    private void SetNextChildrenOutline(ref SystemState state)
    {
        // Get outline settings from parent entity
        Outline settings = state.EntityManager.GetComponentData<Outline>(nextEntity);
        
        // Set all children in the list to outlined
        foreach (Entity childEntity in outlineEntityChildrenList)
        {
            if (!state.EntityManager.Exists(childEntity)) continue;
            
            // Enable OutlinedTag
            if (!state.EntityManager.HasComponent<OutlinedTag>(childEntity))
            {
                state.EntityManager.AddComponent<OutlinedTag>(childEntity);
            }
            state.EntityManager.SetComponentEnabled<OutlinedTag>(childEntity, true);
            
            // Set material RenderType to "Outlined" (main thread operation)
            if (state.EntityManager.HasComponent<RenderMeshArray>(childEntity))
            {
                RenderMeshArray renderMeshArray = state.EntityManager.GetSharedComponentManaged<RenderMeshArray>(childEntity);
                var materials = renderMeshArray.Materials;
                
                foreach (var material in materials)
                {
                    if (material == null) continue;
                    material.SetOverrideTag("RenderType", "Outlined");
                }
            }
        }
    }
}

[BurstCompile]
public partial struct GatherOutlineEntityChildrenJob : IJobEntity {
    
    public Entity parentEntity;
    public NativeList<Entity>.ParallelWriter outlineEntityChildrenList;
    
    public void Execute(ref OutlineChild outlineChild, Entity entity) {
        if (outlineChild.parentEntity == parentEntity) {
            outlineEntityChildrenList.AddNoResize(entity);
        }
    }
}