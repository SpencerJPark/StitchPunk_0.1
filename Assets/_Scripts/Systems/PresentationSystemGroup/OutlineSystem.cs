using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Rendering;
using UnityEngine;

[UpdateInGroup(typeof(PresentationSystemGroup))]
partial struct OutlineSystem : ISystem
{
    private Entity playerEntity;
    private Entity previousEntity;
    private Entity nextEntity;
    
    private NativeList<Entity> outlineEntityChildrenList;
    private EntityQuery outlineChildQuery;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        
        previousEntity = Entity.Null;
        nextEntity = Entity.Null;
        playerEntity = Entity.Null;
        
        // Initialize with reasonable capacity (will grow automatically if needed)
        outlineEntityChildrenList = new NativeList<Entity>(32, Allocator.Persistent);
        
        // Create query for outline children
        outlineChildQuery = SystemAPI.QueryBuilder()
            .WithAll<OutlineChild>()
            .Build();
    }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        outlineEntityChildrenList.Dispose();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        if (playerEntity == Entity.Null || !state.EntityManager.Exists(playerEntity))
        {
            playerEntity = SystemAPI.GetSingletonEntity<Player>();
        }
        
        FindNextOutlined(ref state);
        
        if (nextEntity == Entity.Null)
        {
            // Clear previous outline if nothing is targeted
            if (previousEntity != Entity.Null)
            {
                ResetPreviousChildrenOutline(ref state);
                previousEntity = Entity.Null;
            }
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

   [BurstCompile]
    private void FindNextOutlined(ref SystemState state)
    {
        nextEntity = SystemAPI.GetComponent<Player>(playerEntity).interactableEntity;
    }

    private void ResetPreviousChildrenOutline(ref SystemState state)
    {
        // Set all children in the list to not outlined
        for (int i = 0; i < outlineEntityChildrenList.Length; i++)
        {
            Entity childEntity = outlineEntityChildrenList[i];
            
            if (!state.EntityManager.Exists(childEntity)) continue;
            
            // Disable OutlinedTag
            if (state.EntityManager.HasComponent<OutlinedTag>(childEntity))
            {
                state.EntityManager.SetComponentEnabled<OutlinedTag>(childEntity, false);
            }
        }
    }
    
    private void GatherNewChildren(ref SystemState state)
    {
        outlineEntityChildrenList.Clear();
        
        // Get total count to ensure capacity
        int totalCount = outlineChildQuery.CalculateEntityCount();
        
        if (totalCount == 0)
        {
            return;
        }
        
        // Ensure capacity for worst case (all entities match)
        if (outlineEntityChildrenList.Capacity < totalCount)
        {
            outlineEntityChildrenList.Capacity = totalCount;
        }
        
        // Schedule job to gather matching children
        GatherOutlineEntityChildrenJob job = new GatherOutlineEntityChildrenJob
        {
            parentEntity = nextEntity,
            outlineEntityChildrenList = outlineEntityChildrenList.AsParallelWriter(),
        };
        
        state.Dependency = job.ScheduleParallel(outlineChildQuery, state.Dependency);
        state.Dependency.Complete();
    }
    
    private void SetNextChildrenOutline(ref SystemState state)
    {
        // Set all children in the list to outlined
        for (int i = 0; i < outlineEntityChildrenList.Length; i++)
        {
            Entity childEntity = outlineEntityChildrenList[i];
            
            if (!state.EntityManager.Exists(childEntity)) continue;
            
            // Enable OutlinedTag
            if (!state.EntityManager.HasComponent<OutlinedTag>(childEntity))
            {
                state.EntityManager.AddComponent<OutlinedTag>(childEntity);
            }
            state.EntityManager.SetComponentEnabled<OutlinedTag>(childEntity, true);
        }
    }
}

[BurstCompile]
public partial struct GatherOutlineEntityChildrenJob : IJobEntity
{
    public Entity parentEntity;
    public NativeList<Entity>.ParallelWriter outlineEntityChildrenList;
    
    public void Execute(ref OutlineChild outlineChild, Entity entity)
    {
        if (outlineChild.parentEntity == parentEntity)
        {
            outlineEntityChildrenList.AddNoResize(entity);
        }
    }
}