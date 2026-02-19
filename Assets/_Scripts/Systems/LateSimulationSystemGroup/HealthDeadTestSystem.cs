using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
partial struct HealthDeadTestSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer.ParallelWriter entityCommandBuffer =
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
                .AsParallelWriter();

        HealthDeadTestJob healthDeadTestJob = new HealthDeadTestJob
        {
            EntityCommandBuffer = entityCommandBuffer
        };

        state.Dependency = healthDeadTestJob.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
partial struct HealthDeadTestJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter EntityCommandBuffer;

    public void Execute([ChunkIndexInQuery] int chunkIndexInQuery, Entity entity, ref Health health)
    {
        if (health.healthAmount <= 0)
        {
            health.onDead = true;
            EntityCommandBuffer.DestroyEntity(chunkIndexInQuery, entity);
        }
    }
}