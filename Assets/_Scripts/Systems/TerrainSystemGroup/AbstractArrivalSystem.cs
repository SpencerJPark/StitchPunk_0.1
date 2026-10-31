using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Finished routes become a resting anchor and raise AbstractRouteCompleted for one frame.
[BurstCompile]
[UpdateInGroup(typeof(TerrainSystemGroup))]
[UpdateAfter(typeof(AbstractTransitionSystem))]
public partial struct AbstractArrivalSystem : ISystem
{
    private const double SecondsBetweenScans = 1.0;

    private double nextScanElapsedSeconds;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WorldClock>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Runs every frame so the event cannot outlive the frame after it was raised; the scan below is the 1 Hz part.
        state.Dependency = new ClearArrivalEventJob().Schedule(state.Dependency);

        double elapsedSeconds = SystemAPI.Time.ElapsedTime;
        if (elapsedSeconds < nextScanElapsedSeconds)
            return;
        nextScanElapsedSeconds = elapsedSeconds + SecondsBetweenScans;

        state.Dependency = new AbstractArrivalJob
        {
            nowWorldSeconds = SystemAPI.GetSingleton<WorldClock>().totalWorldSeconds
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
public partial struct ClearArrivalEventJob : IJobEntity
{
    public void Execute(EnabledRefRW<AbstractRouteCompleted> routeCompletedEnabled)
    {
        routeCompletedEnabled.ValueRW = false;
    }
}

[BurstCompile]
[WithAll(typeof(AbstractSimulation))]
[WithPresent(typeof(AbstractRouteCompleted))]
public partial struct AbstractArrivalJob : IJobEntity
{
    public double nowWorldSeconds;

    public void Execute(
        ref LocalTransform transform,
        ref AbstractRoute route,
        ref AbstractRestAnchor restAnchor,
        DynamicBuffer<AbstractRouteWaypoint> waypoints,
        EnabledRefRW<AbstractRouteCompleted> routeCompletedEnabled)
    {
        if (waypoints.Length < 2 || route.totalLengthMetres <= 0f)
            return;

        AbstractTravel.PointAlongRoute(in waypoints, in route, nowWorldSeconds, out float3 position, out float3 _, out bool isFinished);
        if (!isFinished)
            return;

        transform.Position = position;
        restAnchor.position = position;
        waypoints.Clear();
        route.totalLengthMetres = 0f;
        routeCompletedEnabled.ValueRW = true;
    }
}
