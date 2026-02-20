using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Manages horde lifecycle: creation, membership, pathfinding coordination, and dissolution.
/// Hordes are generic groups that any entity can join for shared pathfinding and AI.
/// </summary>
[UpdateInGroup(typeof(GameManagerSystemGroup))]
public partial struct HordeSystem : ISystem
{
    private EntityQuery hordeQuery;
    private EntityQuery memberQuery;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // Create horde registry singleton
        var registryEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponent<HordeRegistry>(registryEntity);
        state.EntityManager.SetComponentData(registryEntity, new HordeRegistry { nextHordeId = 1 });
        
        hordeQuery = state.GetEntityQuery(ComponentType.ReadWrite<Horde>());
        memberQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<HordeMembership>(),
            ComponentType.ReadOnly<LocalTransform>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var registry = SystemAPI.GetSingletonRW<HordeRegistry>();
        
        // Update member counts and handle disbanded hordes
        UpdateHordeMemberCounts(ref state);
        
        // Process horde path updates
        UpdateHordePaths(ref state);
        
        // Sync horde members with their horde's flow field
        SyncMembersToHorde(ref state);
    }
    
    private void UpdateHordeMemberCounts(ref SystemState state)
    {
        // Reset all horde member counts
        foreach (var horde in SystemAPI.Query<RefRW<Horde>>())
        {
            horde.ValueRW.memberCount = 0;
        }
        
        // Count members per horde
        var hordeLookup = SystemAPI.GetComponentLookup<Horde>(false);
        
        foreach (var (membership, entity) in 
            SystemAPI.Query<RefRO<HordeMembership>>()
            .WithAll<HordeMembership>()
            .WithEntityAccess())
        {
            if (membership.ValueRO.hordeEntity != Entity.Null && 
                hordeLookup.HasComponent(membership.ValueRO.hordeEntity))
            {
                var horde = hordeLookup.GetRefRW(membership.ValueRO.hordeEntity);
                horde.ValueRW.memberCount++;
            }
        }
        
        // Deactivate empty hordes
        foreach (var (horde, entity) in SystemAPI.Query<RefRW<Horde>>().WithEntityAccess())
        {
            if (horde.ValueRO.memberCount == 0 && horde.ValueRO.isActive)
            {
                horde.ValueRW.isActive = false;
                horde.ValueRW.flowFieldIndex = -1;
            }
        }
    }
    
    private void UpdateHordePaths(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<GridSystem.GridConfig>())
            return;
        
        if (!SystemAPI.HasSingleton<FlowFieldSystem.FlowFieldData>())
            return;
            
        var gridConfig = SystemAPI.GetSingleton<GridSystem.GridConfig>();
        var flowFieldData = SystemAPI.GetSingleton<FlowFieldSystem.FlowFieldData>();
        
        foreach (var (horde, entity) in SystemAPI.Query<RefRW<Horde>>().WithEntityAccess())
        {
            if (!horde.ValueRO.isActive || !horde.ValueRO.needsPathUpdate)
                continue;
            
            // Check if we already have a valid flow field for this target
            int2 targetGridPos = GridSystem.GetGridPosition(horde.ValueRO.targetPosition, gridConfig.cellSize);
            int targetLayer = GridSystem.GetLayer(horde.ValueRO.targetPosition, gridConfig.layerHeight);
            int existingIndex = FindExistingFlowField(targetGridPos, targetLayer, flowFieldData);
            
            if (existingIndex >= 0)
            {
                horde.ValueRW.flowFieldIndex = existingIndex;
                horde.ValueRW.needsPathUpdate = false;
            }
            else
            {
                // Request a new flow field by enabling PathRequest on one member
                // The FlowFieldSystem will handle creating the flow field
                RequestFlowFieldForHorde(ref state, entity, horde.ValueRO.targetPosition);
                horde.ValueRW.needsPathUpdate = false;
            }
        }
    }
    
    private void RequestFlowFieldForHorde(ref SystemState state, Entity hordeEntity, float3 targetPosition)
    {
        // Find any member of this horde to request the path
        foreach (var (membership, agent, pathRequest, memberEntity) in 
            SystemAPI.Query<RefRO<HordeMembership>, RefRW<PathfindingAgent>, RefRW<PathRequest>>()
            .WithAll<HordeMembership>()
            .WithEntityAccess())
        {
            if (membership.ValueRO.hordeEntity == hordeEntity)
            {
                // Use this member to request the flow field
                pathRequest.ValueRW.targetPosition = targetPosition;
                pathRequest.ValueRW.requestedMode = PathfindingMode.FlowField;
                agent.ValueRW.currentMode = PathfindingMode.FlowField;
                SystemAPI.SetComponentEnabled<PathRequest>(memberEntity, true);
                break;
            }
        }
    }
    
    private void SyncMembersToHorde(ref SystemState state)
    {
        var hordeLookup = SystemAPI.GetComponentLookup<Horde>(true);
        
        foreach (var (membership, follower, agent, entity) in 
            SystemAPI.Query<RefRO<HordeMembership>, RefRW<FlowFieldFollower>, RefRW<PathfindingAgent>>()
            .WithAll<HordeMembership>()
            .WithEntityAccess())
        {
            if (membership.ValueRO.hordeEntity == Entity.Null)
                continue;
                
            if (!hordeLookup.HasComponent(membership.ValueRO.hordeEntity))
                continue;
                
            var horde = hordeLookup[membership.ValueRO.hordeEntity];
            
            if (!horde.isActive)
                continue;
            
            // Sync member to horde's flow field
            if (horde.flowFieldIndex >= 0)
            {
                follower.ValueRW.flowFieldIndex = horde.flowFieldIndex;
                follower.ValueRW.targetPosition = horde.targetPosition;
                agent.ValueRW.currentMode = PathfindingMode.FlowField;
                SystemAPI.SetComponentEnabled<FlowFieldFollower>(entity, true);
                SystemAPI.SetComponentEnabled<DStarLiteFollower>(entity, false);
            }
        }
    }
    
    private int FindExistingFlowField(int2 targetGridPosition, int layer, FlowFieldSystem.FlowFieldData flowFieldData)
    {
        for (int i = 0; i < FlowFieldSystem.FLOW_FIELD_MAP_COUNT; i++)
        {
            var target = flowFieldData.targets[i];
            if (target.isValid && target.gridPosition.Equals(targetGridPosition) && target.layer == layer)
            {
                return i;
            }
        }
        return -1;
    }
}

