using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;

// Other systems capture moveSpeed as a base, so the scale must never outlive the mover; ignore enabled state so a disabled Movement is still restored.
[BurstCompile]
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
[UpdateAfter(typeof(UnitMoverSystem))]
public partial struct TerrainSurfaceSpeedRevertSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        TerrainSurfaceSpeedRevertJob job = new TerrainSurfaceSpeedRevertJob();
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
public partial struct TerrainSurfaceSpeedRevertJob : IJobEntity
{
    public void Execute(ref Movement movement, ref SurfaceSpeedFactor surfaceSpeedFactor)
    {
        if (surfaceSpeedFactor.appliedFactor <= 0f)
            return;

        movement.moveSpeed /= surfaceSpeedFactor.appliedFactor;
        movement.runSpeed /= surfaceSpeedFactor.appliedFactor;
        surfaceSpeedFactor.appliedFactor = 0f;
    }
}
