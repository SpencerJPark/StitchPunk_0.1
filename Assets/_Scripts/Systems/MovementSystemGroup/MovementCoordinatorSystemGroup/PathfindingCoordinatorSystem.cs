using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

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
        GridSystem.GridConfig gridConfig = SystemAPI.GetSingleton<GridSystem.GridConfig>();
        float deltaTime = SystemAPI.Time.DeltaTime;
        
        EndSimulationEntityCommandBufferSystem.Singleton ecbSingleton = 
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        EntityCommandBuffer.ParallelWriter ecb = 
            ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        
        PathfindingCoordinatorJob job = new PathfindingCoordinatorJob
        {
            deltaTime = deltaTime,
            arrivalDistance = gridConfig.cellSize * 0.5f,
            ecb = ecb
        };
        
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public partial struct PathfindingCoordinatorJob : IJobEntity
    {
        public float deltaTime;
        public float arrivalDistance;
        public EntityCommandBuffer.ParallelWriter ecb;

        public void Execute(
            ref PathfindingAgent agent,
            in LocalTransform transform,
            Entity entity,
            [EntityIndexInQuery] int sortKey)
        {
            if (!agent.isActive)
                return;

            agent.timeSinceLastRepath += deltaTime;

            // Repath activation: re-enable PathRequest if a path is needed and the
            // interval has elapsed. Serves as both the periodic repath mechanism and
            // a safety net in case the initial RequestPath activation was missed.
            if (agent.needsRepath && agent.timeSinceLastRepath >= agent.repathInterval)
            {
                agent.timeSinceLastRepath = 0f;
                ecb.SetComponentEnabled<PathRequest>(sortKey, entity, true);
            }

            float distToTarget = math.distance(
                new float2(transform.Position.x, transform.Position.z),
                new float2(agent.targetPosition.x, agent.targetPosition.z));

            if (distToTarget < arrivalDistance)
            {
                agent.isActive = false;
                agent.needsRepath = false;

                ecb.SetComponentEnabled<FlowFieldFollower>(sortKey, entity, false);
                ecb.SetComponentEnabled<DStarLiteFollower>(sortKey, entity, false);
            }
        }
    }
}