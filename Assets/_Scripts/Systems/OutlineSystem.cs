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
        
        GatherNewChildren(ref state);
        
        SetNextChildrenOutline(ref state);
    }

    private void FindNextOutlined(ref SystemState state)
    {
        
    }
    
    private void ResetPreviousChildrenOutline(ref SystemState state)
    {
        
    }
    
    private void GatherNewChildren(ref SystemState state)
    {
        outlineEntityChildrenList.Clear();
        new GatherOutlineEntityChildrenJob() {
            outlineEntity = nextEntity,
            outlineEntityChildrenList = outlineEntityChildrenList.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency).Complete();
    }
    
    private void SetNextChildrenOutline(ref SystemState state)
    {
        
    }
}

[BurstCompile]
public partial struct GatherOutlineEntityChildrenJob : IJobEntity {
    
    public Entity outlineEntity;
    public NativeList<Entity>.ParallelWriter outlineEntityChildrenList;
    
    public void Execute(ref OutlineChild outlineChild, Entity entity) {
        if (outlineChild.parentEntity == outlineEntity) {
            outlineEntityChildrenList.AddNoResize(entity);
        }
    }
}