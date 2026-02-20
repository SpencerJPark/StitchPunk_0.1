using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Coordinates between Flow Field and D* Lite pathfinding systems.
/// Handles mode switching, target counting, and routing path requests.
/// Works with HordeSystem for group pathfinding.
/// </summary>
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]
public partial struct PathfindingCoordinatorSystem : ISystem
{
    public struct PathfindingCoordinatorData : IComponentData
    {
        /// <summary>Map from target grid position hash to count of entities targeting it</summary>
        public NativeHashMap<int, int> targetCounts;
        
        /// <summary>Map from target grid position hash to flow field index (if exists)</summary>
        public NativeHashMap<int, int> targetToFlowFieldIndex;
    }
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridSystem.GridConfig>();
        
        var data = new PathfindingCoordinatorData
        {
            targetCounts = new NativeHashMap<int, int>(256, Allocator.Persistent),
            targetToFlowFieldIndex = new NativeHashMap<int, int>(FlowFieldSystem.FLOW_FIELD_MAP_COUNT, Allocator.Persistent)
        };
        
        state.EntityManager.AddComponent<PathfindingCoordinatorData>(state.SystemHandle);
        state.EntityManager.SetComponentData(state.SystemHandle, data);
    }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.HasComponent<PathfindingCoordinatorData>(state.SystemHandle))
        {
            var data = SystemAPI.GetComponent<PathfindingCoordinatorData>(state.SystemHandle);
            if (data.targetCounts.IsCreated) data.targetCounts.Dispose();
            if (data.targetToFlowFieldIndex.IsCreated) data.targetToFlowFieldIndex.Dispose();
        }
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var coordData = SystemAPI.GetComponent<PathfindingCoordinatorData>(state.SystemHandle);
        var gridConfig = SystemAPI.GetSingleton<GridSystem.GridConfig>();
        
        // Clear and rebuild target counts
        coordData.targetCounts.Clear();
        coordData.targetToFlowFieldIndex.Clear();
        
        // Build flow field index lookup from FlowFieldSystem if available
        if (SystemAPI.HasSingleton<FlowFieldSystem.FlowFieldData>())
        {
            var flowFieldData = SystemAPI.GetSingleton<FlowFieldSystem.FlowFieldData>();
            for (int i = 0; i < FlowFieldSystem.FLOW_FIELD_MAP_COUNT; i++)
            {
                var target = flowFieldData.targets[i];
                if (target.isValid)
                {
                    int hash = HashGridPosition(target.gridPosition);
                    coordData.targetToFlowFieldIndex.TryAdd(hash, i);
                }
            }
        }
        
        // Count entities per target and handle path requests (skip horde members - they're handled by HordeSystem)
        foreach (var (agent, request, requestEnabled, entity) in 
            SystemAPI.Query<
                RefRW<PathfindingAgent>,
                RefRO<PathRequest>,
                EnabledRefRW<PathRequest>>()
            .WithAbsent<HordeMembership>()
            .WithEntityAccess())
        {
            if (!requestEnabled.ValueRO) continue;
            
            int2 targetGridPos = GridSystem.GetGridPosition(
                request.ValueRO.targetPosition, gridConfig.cellSize);
            int targetHash = HashGridPosition(targetGridPos);
            
            // Increment target count
            if (coordData.targetCounts.TryGetValue(targetHash, out int count))
            {
                coordData.targetCounts[targetHash] = count + 1;
            }
            else
            {
                coordData.targetCounts.TryAdd(targetHash, 1);
            }
        }
        
        // Also count horde members' targets
        foreach (var (membership, agent) in 
            SystemAPI.Query<RefRO<HordeMembership>, RefRO<PathfindingAgent>>()
            .WithAll<HordeMembership>())
        {
            int2 targetGridPos = GridSystem.GetGridPosition(
                agent.ValueRO.targetPosition, gridConfig.cellSize);
            int targetHash = HashGridPosition(targetGridPos);
            
            if (coordData.targetCounts.TryGetValue(targetHash, out int count))
            {
                coordData.targetCounts[targetHash] = count + 1;
            }
            else
            {
                coordData.targetCounts.TryAdd(targetHash, 1);
            }
        }
        
        // Process path requests for non-horde members
        foreach (var (agent, request, requestEnabled, transform, entity) in 
            SystemAPI.Query<
                RefRW<PathfindingAgent>,
                RefRO<PathRequest>,
                EnabledRefRW<PathRequest>,
                RefRO<LocalTransform>>()
            .WithAbsent<HordeMembership>()
            .WithEntityAccess())
        {
            if (!requestEnabled.ValueRO) continue;
            
            int2 targetGridPos = GridSystem.GetGridPosition(
                request.ValueRO.targetPosition, gridConfig.cellSize);
            int targetHash = HashGridPosition(targetGridPos);
            
            // Get count for this target
            int targetCount = 1;
            coordData.targetCounts.TryGetValue(targetHash, out targetCount);
            
            // Check if flow field exists for this target
            bool hasFlowField = coordData.targetToFlowFieldIndex.TryGetValue(targetHash, out int flowFieldIndex);
            
            // Determine optimal mode (not in a horde)
            PathfindingMode optimalMode = PathfindingUtils.DetermineOptimalMode(
                false, // not in horde
                targetCount,
                agent.ValueRO.hordeFormationThreshold,
                hasFlowField);
            
            // Update agent
            agent.ValueRW.targetPosition = request.ValueRO.targetPosition;
            agent.ValueRW.isActive = true;
            agent.ValueRW.needsRepath = false;
            agent.ValueRW.timeSinceLastRepath = 0f;
            
            // Route to appropriate system
            if (optimalMode == PathfindingMode.FlowField)
            {
                RouteToFlowField(ref state, entity, request.ValueRO.targetPosition, 
                    hasFlowField, flowFieldIndex, ref agent.ValueRW);
            }
            else if (optimalMode == PathfindingMode.DStarLite)
            {
                RouteToDStarLite(ref state, entity, request.ValueRO.targetPosition, 
                    transform.ValueRO.Position, ref agent.ValueRW);
            }
            
            // Consume the request
            requestEnabled.ValueRW = false;
        }
        
        // Handle mode switching for active non-horde agents
        HandleModeSwitching(ref state, ref coordData, gridConfig);
        
        // Update repath timers
        UpdateRepathTimers(ref state, gridConfig);
        
        SystemAPI.SetComponent(state.SystemHandle, coordData);
    }
    
    private void RouteToFlowField(ref SystemState state, Entity entity, float3 targetPosition,
        bool hasExistingFlowField, int flowFieldIndex, ref PathfindingAgent agent)
    {
        agent.currentMode = PathfindingMode.FlowField;
        
        // Disable D* Lite follower
        if (SystemAPI.HasComponent<DStarLiteFollower>(entity))
        {
            SystemAPI.SetComponentEnabled<DStarLiteFollower>(entity, false);
        }
        
        if (hasExistingFlowField)
        {
            // Use existing flow field
            if (SystemAPI.HasComponent<FlowFieldFollower>(entity))
            {
                var follower = SystemAPI.GetComponentRW<FlowFieldFollower>(entity);
                follower.ValueRW.targetPosition = targetPosition;
                //follower.ValueRW.gridIndex = flowFieldIndex;
                SystemAPI.SetComponentEnabled<FlowFieldFollower>(entity, true);
            }
        }
        else
        {
            // Request new flow field via centralized PathRequest
            if (SystemAPI.HasComponent<PathRequest>(entity))
            {
                var pathRequest = SystemAPI.GetComponentRW<PathRequest>(entity);
                pathRequest.ValueRW.targetPosition = targetPosition;
                pathRequest.ValueRW.requestedMode = PathfindingMode.FlowField;
                SystemAPI.SetComponentEnabled<PathRequest>(entity, true);
            }
        }
    }
    
    private void RouteToDStarLite(ref SystemState state, Entity entity, float3 targetPosition,
        float3 currentPosition, ref PathfindingAgent agent)
    {
        agent.currentMode = PathfindingMode.DStarLite;
        
        // Disable flow field follower
        if (SystemAPI.HasComponent<FlowFieldFollower>(entity))
        {
            SystemAPI.SetComponentEnabled<FlowFieldFollower>(entity, false);
        }
        
        // Enable D* Lite follower and set initial data
        if (SystemAPI.HasComponent<DStarLiteFollower>(entity))
        {
            var follower = SystemAPI.GetComponentRW<DStarLiteFollower>(entity);
            follower.ValueRW.targetPosition = targetPosition;
            follower.ValueRW.nextWaypoint = currentPosition; // Will be updated by D* system
            follower.ValueRW.pathDataIndex = -1; // Will be assigned by D* system
            SystemAPI.SetComponentEnabled<DStarLiteFollower>(entity, true);
            
            // Create path request for D* system
            if (SystemAPI.HasComponent<PathRequest>(entity))
            {
                var pathRequest = SystemAPI.GetComponentRW<PathRequest>(entity);
                pathRequest.ValueRW.targetPosition = targetPosition;
                pathRequest.ValueRW.requestedMode = PathfindingMode.DStarLite;
                SystemAPI.SetComponentEnabled<PathRequest>(entity, true);
            }
        }
    }
    
    private void HandleModeSwitching(ref SystemState state, ref PathfindingCoordinatorData coordData,
        GridSystem.GridConfig gridConfig)
    {
        // Only handle non-horde members - horde members are managed by HordeSystem
        foreach (var (agent, transform, entity) in 
            SystemAPI.Query<RefRW<PathfindingAgent>, RefRO<LocalTransform>>()
            .WithAbsent<HordeMembership>()
            .WithEntityAccess())
        {
            if (!agent.ValueRO.isActive) continue;
            
            int2 targetGridPos = GridSystem.GetGridPosition(
                agent.ValueRO.targetPosition, gridConfig.cellSize);
            int targetHash = HashGridPosition(targetGridPos);
            
            // Get current target count
            int targetCount = 1;
            coordData.targetCounts.TryGetValue(targetHash, out targetCount);
            
            // Check flow field availability
            bool hasFlowField = coordData.targetToFlowFieldIndex.TryGetValue(targetHash, out int flowFieldIndex);
            
            // Check if D* path is valid
            bool hasDStarPath = false;
            if (SystemAPI.HasComponent<DStarLiteFollower>(entity))
            {
                var follower = SystemAPI.GetComponent<DStarLiteFollower>(entity);
                hasDStarPath = follower.pathDataIndex >= 0;
            }
            
            // Determine if we should switch (not in horde)
            bool shouldSwitch = PathfindingUtils.ShouldSwitchMode(
                agent.ValueRO.currentMode,
                agent.ValueRO.preferredMode,
                false, // not in horde
                targetCount,
                agent.ValueRO.hordeFormationThreshold,
                hasFlowField,
                hasDStarPath);
            
            if (shouldSwitch)
            {
                PathfindingMode newMode = PathfindingUtils.DetermineOptimalMode(
                    false, targetCount, agent.ValueRO.hordeFormationThreshold, hasFlowField);
                
                if (newMode == PathfindingMode.FlowField)
                {
                    RouteToFlowField(ref state, entity, agent.ValueRO.targetPosition,
                        hasFlowField, flowFieldIndex, ref agent.ValueRW);
                }
                else if (newMode == PathfindingMode.DStarLite)
                {
                    RouteToDStarLite(ref state, entity, agent.ValueRO.targetPosition,
                        transform.ValueRO.Position, ref agent.ValueRW);
                }
            }
        }
    }
    
    private void UpdateRepathTimers(ref SystemState state, GridSystem.GridConfig gridConfig)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        
        foreach (var (agent, transform, entity) in 
            SystemAPI.Query<RefRW<PathfindingAgent>, RefRO<LocalTransform>>()
            .WithEntityAccess())
        {
            if (!agent.ValueRO.isActive) continue;
            
            agent.ValueRW.timeSinceLastRepath += deltaTime;
            
            // Check if we need to repath (only for D* Lite - flow fields are shared)
            if (agent.ValueRO.currentMode == PathfindingMode.DStarLite &&
                agent.ValueRO.timeSinceLastRepath >= agent.ValueRO.repathInterval)
            {
                // Check if target changed significantly or obstacles changed
                // For now, just mark for repath periodically
                agent.ValueRW.needsRepath = true;
                agent.ValueRW.timeSinceLastRepath = 0f;
            }
            
            // Check if reached destination
            float distToTarget = math.distance(
                new float2(transform.ValueRO.Position.x, transform.ValueRO.Position.z),
                new float2(agent.ValueRO.targetPosition.x, agent.ValueRO.targetPosition.z));
            
            if (distToTarget < gridConfig.cellSize * 0.5f)
            {
                agent.ValueRW.isActive = false;
                
                // Disable followers
                if (SystemAPI.HasComponent<FlowFieldFollower>(entity))
                    SystemAPI.SetComponentEnabled<FlowFieldFollower>(entity, false);
                if (SystemAPI.HasComponent<DStarLiteFollower>(entity))
                    SystemAPI.SetComponentEnabled<DStarLiteFollower>(entity, false);
            }
        }
    }
    
    private static int HashGridPosition(int2 pos)
    {
        // Simple spatial hash
        return pos.x * 73856093 ^ pos.y * 19349663;
    }
}