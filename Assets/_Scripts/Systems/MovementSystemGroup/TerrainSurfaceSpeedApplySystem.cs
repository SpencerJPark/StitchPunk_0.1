using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Other systems capture moveSpeed as a base, so this scale is applied late and must never outlive the mover (see the Revert system).
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(DrunkStumbleSystem))]
[UpdateBefore(typeof(MovementExecutionSystemGroup))]
public partial struct TerrainSurfaceSpeedApplySystem : ISystem
{
    ComponentLookup<PlayerRoll> playerRollLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        playerRollLookup = state.GetComponentLookup<PlayerRoll>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        playerRollLookup.Update(ref state);
        TerrainSurfaceSpeedApplyJob job = new TerrainSurfaceSpeedApplyJob
        {
            playerRollLookup = playerRollLookup
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct TerrainSurfaceSpeedApplyJob : IJobEntity
{
    const float NoScaleTolerance = 0.001f;

    [ReadOnly] public ComponentLookup<PlayerRoll> playerRollLookup;

    public void Execute(Entity entity, ref Movement movement, ref SurfaceSpeedFactor surfaceSpeedFactor)
    {
        bool isRolling = playerRollLookup.HasComponent(entity) && playerRollLookup.IsComponentEnabled(entity);
        if (isRolling || math.abs(surfaceSpeedFactor.factor - 1f) < NoScaleTolerance)
        {
            surfaceSpeedFactor.appliedFactor = 0f;
            return;
        }

        movement.moveSpeed *= surfaceSpeedFactor.factor;
        movement.runSpeed *= surfaceSpeedFactor.factor;
        surfaceSpeedFactor.appliedFactor = surfaceSpeedFactor.factor;
    }
}
