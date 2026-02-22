using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Simple coordinator that routes path requests to the appropriate system.
/// DStarLiteSystem handles the actual PathRequest consumption.
/// </summary>
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]
[UpdateAfter(typeof(GridSystem))]
public partial struct PathfindingCoordinatorSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridSystem.GridConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var gridConfig = SystemAPI.GetSingleton<GridSystem.GridConfig>();
        float deltaTime = SystemAPI.Time.DeltaTime;
        
        // Just update repath timers and check for arrival
        foreach (var (agent, transform, mover, entity) in 
                 SystemAPI.Query<RefRW<PathfindingAgent>, RefRO<LocalTransform>, RefRO<UnitMover>>()
                     .WithEntityAccess())
        {
            if (!agent.ValueRO.isActive) continue;
            
            agent.ValueRW.timeSinceLastRepath += deltaTime;
            
            // Check if reached destination
            float distToTarget = math.distance(
                new float2(transform.ValueRO.Position.x, transform.ValueRO.Position.z),
                new float2(agent.ValueRO.targetPosition.x, agent.ValueRO.targetPosition.z));
            
            if (distToTarget < gridConfig.cellSize * 0.5f)
            {
                agent.ValueRW.isActive = false;
                
                if (SystemAPI.HasComponent<FlowFieldFollower>(entity))
                    SystemAPI.SetComponentEnabled<FlowFieldFollower>(entity, false);
                if (SystemAPI.HasComponent<DStarLiteFollower>(entity))
                    SystemAPI.SetComponentEnabled<DStarLiteFollower>(entity, false);
            }
        }
    }
}