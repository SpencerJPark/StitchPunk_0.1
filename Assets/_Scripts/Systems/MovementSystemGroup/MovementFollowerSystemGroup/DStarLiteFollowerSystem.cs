using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Updates UnitMover.targetPosition for entities following D* Lite paths.
/// Also handles line-of-sight optimization.
/// </summary>
[UpdateInGroup(typeof(MovementFollowerSystemGroup))]
public partial struct DStarLiteFollowerSystem : ISystem
{
    private ComponentLookup<DStarLiteFollower> dstarFollowerLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();

        dstarFollowerLookup = state.GetComponentLookup<DStarLiteFollower>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        dstarFollowerLookup.Update(ref state);

        // Check line of sight to final target
        var lineOfSightJob = new DStarLineOfSightJob
        {
            collisionWorld = physicsWorld.CollisionWorld,
            dstarFollowerLookup = dstarFollowerLookup
        };
        state.Dependency = lineOfSightJob.ScheduleParallel(state.Dependency);
        
        // Set movement targets for entities still following D* path
        var moveJob = new DStarMoveJob();
        state.Dependency = moveJob.ScheduleParallel(state.Dependency);
    }
}

/// <summary>
/// Checks if D* Lite follower has clear line of sight to final target.
/// When LOS is clear, collapses the remaining path to a single direct waypoint
/// so DStarMoveJob drives the unit straight to the goal. DStarLiteFollower
/// stays enabled so that UpdateFollowersJob and PathfindingCoordinatorSystem
/// can detect arrival and handle cleanup normally.
/// </summary>
[BurstCompile]
[WithAll(typeof(DStarLiteFollower))]
public partial struct DStarLineOfSightJob : IJobEntity
{
    [NativeDisableParallelForRestriction]
    public ComponentLookup<DStarLiteFollower> dstarFollowerLookup;

    [ReadOnly] public CollisionWorld collisionWorld;

    public void Execute(
        in LocalTransform localTransform,
        in UnitMover unitMover,
        Entity entity)
    {
        DStarLiteFollower follower = dstarFollowerLookup[entity];

        RaycastInput raycastInput = new RaycastInput
        {
            Start = localTransform.Position,
            End = follower.targetPosition,
            Filter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = 1u << GlobalGameData.WALLS_LAYER,
                GroupIndex = 0
            }
        };

        if (!collisionWorld.CastRay(raycastInput))
        {
            // Clear line of sight — skip intermediate waypoints and aim directly at
            // the final target. DStarMoveJob will pick up this updated nextWaypoint.
            // DStarLiteFollower remains enabled so arrival detection works normally.
            follower.nextWaypoint = follower.targetPosition;
            dstarFollowerLookup[entity] = follower;
        }
    }
}

/// <summary>
/// Sets UnitMover target to D* Lite next waypoint.
/// </summary>
[BurstCompile]
[WithAll(typeof(DStarLiteFollower))]
public partial struct DStarMoveJob : IJobEntity
{
    public void Execute(
        in DStarLiteFollower follower,
        in PathfindingAgent agent,
        ref UnitMover unitMover)
    {
        if (agent.currentMode != PathfindingMode.DStarLite)
            return;
        
        unitMover.targetPosition = follower.nextWaypoint;
    }
}