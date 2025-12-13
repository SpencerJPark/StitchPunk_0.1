using Unity.Burst;
using Unity.Entities;
using Unity.Collections;


partial struct OutlineSystem : ISystem
{
    private Entity playerEntity;
    private Entity previousEntity;
    private Entity nextEntity;
    
    private NativeList<Entity> outlineEntityChildrenList;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        
        previousEntity = Entity.Null;
        nextEntity = Entity.Null;
        playerEntity = Entity.Null;
        
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
        if (playerEntity == Entity.Null || !state.EntityManager.Exists(playerEntity))
        {
            playerEntity = SystemAPI.GetSingletonEntity<Player>();
        }
        
        FindNextOutlined(ref state);
        
        if (nextEntity == Entity.Null)
        {
            return;
        }

        if (previousEntity == nextEntity)
        {
            return;
        }
        
        SetChildrenOutline(ref state, false);
        
        previousEntity = nextEntity;
        
        GatherNewChildren(ref state);
        
        SetChildrenOutline(ref state, true);
    }

  
    private void FindNextOutlined(ref SystemState state)
    {
        nextEntity = SystemAPI.GetComponent<Player>(playerEntity).interactableEntity;
    }
    
    private void GatherNewChildren(ref SystemState state)
    {
        outlineEntityChildrenList.Clear();
        new GatherOutlineEntityChildrenJob() {
            parentEntity = nextEntity,
            outlineEntityChildrenList = outlineEntityChildrenList.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency).Complete();
    }
    
    private void SetChildrenOutline(ref SystemState state, bool enabled)
    {
        // Set all children in the list to not outlined
        foreach (Entity childEntity in outlineEntityChildrenList)
        {
            if (!state.EntityManager.Exists(childEntity)) continue;
            
            // Disable OutlinedTag
            if (state.EntityManager.HasComponent<OutlinedTag>(childEntity))
            {
                state.EntityManager.SetComponentEnabled<OutlinedTag>(childEntity, enabled);
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