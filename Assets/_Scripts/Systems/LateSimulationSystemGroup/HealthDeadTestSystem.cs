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

        new HealthDeadTestJob { EntityCommandBuffer = entityCommandBuffer }
            .ScheduleParallel(state.Dependency).Complete();
    }
}

[BurstCompile]
partial struct HealthDeadTestJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter EntityCommandBuffer;

    // Only runs on entities that still have Alive enabled
    public void Execute([ChunkIndexInQuery] int chunkIndexInQuery, Entity entity,
        ref Health health, ref UnitStateData unitState)
    {
        if (health.healthAmount <= 0)
        {
           //health.onDead = true;
            unitState.state = UnitState.Dead;
            EntityCommandBuffer.SetComponentEnabled<Alive>(chunkIndexInQuery, entity, false);
        }
    }
}