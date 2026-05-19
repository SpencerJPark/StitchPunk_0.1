using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(GameManagerSystemGroup))]
[UpdateAfter(typeof(InteractionSpatialHashSystem))]
public partial struct WaypointRegistrationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<SpatialHashRegistry>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        RefRW<SpatialHashRegistry> registryRW = SystemAPI.GetSingletonRW<SpatialHashRegistry>();

        state.Dependency = new RegisterWaypointsJob
        {
            waypointWriter = registryRW.ValueRW.waypointCells.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency);

        state.Dependency.Complete();
    }
}

[BurstCompile]
[WithPresent(typeof(NavigationWaypoint))]
public partial struct RegisterWaypointsJob : IJobEntity
{
    public NativeParallelMultiHashMap<int2, Entity>.ParallelWriter waypointWriter;

    public void Execute(Entity entity, in LocalTransform transform)
    {
        int2 cell = InteractionSpatialHashSystem.GetCell(transform.Position);
        waypointWriter.Add(cell, entity);
    }
}
