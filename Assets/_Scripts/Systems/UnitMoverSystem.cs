using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;

partial struct UnitMoverSystem : ISystem {
    
    public const float REACHED_TARGET_POSITION_DISTANCE_SQ = 2f;
    
    public ComponentLookup<TargetPositionPathQueued> targetPositionPathQueuedComponentLookup;
    public ComponentLookup<FlowFieldPathRequest> flowFieldPathRequestComponentLookup;
    public ComponentLookup<FlowFieldFollower> flowFieldFollowerComponentLookup;
    public ComponentLookup<MoveOverride> moveOverrideComponentLookup;
    public ComponentLookup<GridSystem.GridNode> gridNodeComponentLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        state.RequireForUpdate<GridSystem.GridSystemData>();

        targetPositionPathQueuedComponentLookup = SystemAPI.GetComponentLookup<TargetPositionPathQueued>(false);
        flowFieldPathRequestComponentLookup = SystemAPI.GetComponentLookup<FlowFieldPathRequest>(false);
        flowFieldFollowerComponentLookup = SystemAPI.GetComponentLookup<FlowFieldFollower>(false);
        moveOverrideComponentLookup = SystemAPI.GetComponentLookup<MoveOverride>(false);
        gridNodeComponentLookup = SystemAPI.GetComponentLookup<GridSystem.GridNode>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        GridSystem.GridSystemData gridSystemData = SystemAPI.GetSingleton<GridSystem.GridSystemData>();

        targetPositionPathQueuedComponentLookup.Update(ref state);
        flowFieldPathRequestComponentLookup.Update(ref state);
        flowFieldFollowerComponentLookup.Update(ref state);
        moveOverrideComponentLookup.Update(ref state);
        gridNodeComponentLookup.Update(ref state);

        PhysicsWorldSingleton physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        CollisionWorld collisionWorld = physicsWorldSingleton.CollisionWorld;


        TargetPositionPathQueuedJob targetPositionPathQueuedJob = new TargetPositionPathQueuedJob {
            collisionWorld = collisionWorld,
            gridNodeSize = gridSystemData.gridNodeSize,
            width = gridSystemData.width,
            height = gridSystemData.height,
            costMap = gridSystemData.costMap,
            flowFieldFollowerComponentLookup = flowFieldFollowerComponentLookup,
            flowFieldPathRequestComponentLookup = flowFieldPathRequestComponentLookup,
            moveOverrideComponentLookup = moveOverrideComponentLookup,
            targetPositionPathQueuedComponentLookup = targetPositionPathQueuedComponentLookup
        };
        targetPositionPathQueuedJob.ScheduleParallel();


        TestCanMoveStraightJob testCanMoveStraightJob = new TestCanMoveStraightJob {
            collisionWorld = collisionWorld, 
            flowFieldFollowerComponentLookup = flowFieldFollowerComponentLookup,
        };
        testCanMoveStraightJob.ScheduleParallel();



        FlowFieldFollowerJob flowFieldFollowerJob = new FlowFieldFollowerJob {
            width = gridSystemData.width,
            height = gridSystemData.height,
            gridNodeSize = gridSystemData.gridNodeSize,
            gridNodeSizeDouble = gridSystemData.gridNodeSize * 2f,
            flowFieldFollowerComponentLookup = flowFieldFollowerComponentLookup,
            totalGridMapEntityArray = gridSystemData.totalGridMapEntityArray,
            gridNodeComponentLookup = gridNodeComponentLookup,
        };
        flowFieldFollowerJob.ScheduleParallel();


        CollisionFilter moveCollisionFilter = new CollisionFilter
        {
            BelongsTo    = ~0u,
            CollidesWith =
                (1u << GameAssets.PATHFINDING_WALLS) |
                (1u << GameAssets.BUILDINGS_LAYER),
            GroupIndex   = 0
        };

        UnitMoverJob unitMoverJob = new UnitMoverJob
        {
            deltaTime        = SystemAPI.Time.DeltaTime,
        };
        unitMoverJob.ScheduleParallel();
    }

}


[BurstCompile]
public partial struct UnitMoverJob : IJobEntity
{
    public float deltaTime;

    public void Execute(
        ref LocalTransform localTransform,
        ref UnitMover unitMover,
        ref PhysicsVelocity physicsVelocity)
    {
        float3 currentPos = localTransform.Position;
        float3 toTarget   = unitMover.targetPosition - currentPos;
        float  distanceSq = math.lengthsq(toTarget);

        const float stopDistanceSq   = 0.01f;
        const float blockedThreshold = 0.001f;  // ~ how little movement counts as "stuck"
        const float blockedTimeMax   = 0.3f;    // seconds

        // Check if we're blocked (moved very little since last frame)
        float movedSq = math.lengthsq(currentPos - unitMover.lastPosition);
        if (movedSq < blockedThreshold * blockedThreshold &&
            math.lengthsq(physicsVelocity.Linear) > 0.0001f)
        {
            unitMover.blockedTime += deltaTime;
        }
        else
        {
            unitMover.blockedTime = 0f;
        }

        unitMover.lastPosition = currentPos;

        // If we've been blocked for a bit, stop trying
        if (unitMover.blockedTime >= blockedTimeMax)
        {
            physicsVelocity.Linear  = float3.zero;
            physicsVelocity.Angular = float3.zero;
            unitMover.isMoving      = false;
            unitMover.targetPosition = currentPos;
            unitMover.blockedTime    = 0f;
            return;
        }

        if (distanceSq <= stopDistanceSq)
        {
            physicsVelocity.Linear  = float3.zero;
            physicsVelocity.Angular = float3.zero;
            unitMover.isMoving      = false;
            return;
        }

        float distance = math.sqrt(distanceSq);
        float3 moveDirection = toTarget / math.max(distance, 1e-4f);

        // quaternion targetRot = quaternion.LookRotation(moveDirection, math.up());
        // localTransform.Rotation = math.slerp(
        //     localTransform.Rotation,
        //     targetRot,
        //     deltaTime * unitMover.rotationSpeed);

        physicsVelocity.Linear  = moveDirection * unitMover.moveSpeed;
        physicsVelocity.Angular = float3.zero;

        unitMover.isMoving = true;
    }
}






[BurstCompile]
[WithAll(typeof(TargetPositionPathQueued))]
public partial struct TargetPositionPathQueuedJob : IJobEntity {
    
    [NativeDisableParallelForRestriction] public ComponentLookup<TargetPositionPathQueued> targetPositionPathQueuedComponentLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<FlowFieldPathRequest> flowFieldPathRequestComponentLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<FlowFieldFollower> flowFieldFollowerComponentLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<MoveOverride> moveOverrideComponentLookup;

    [ReadOnly] public CollisionWorld collisionWorld;
    [ReadOnly] public int width;
    [ReadOnly] public int height;
    [ReadOnly] public NativeArray<byte> costMap;
    [ReadOnly] public float gridNodeSize;
    
    public void Execute(
        in LocalTransform localTransform,
        ref UnitMover unitMover,
        Entity entity) {

        // add blocked test, if blocked too long set target to zero

        RaycastInput raycastInput = new RaycastInput {
            Start = localTransform.Position,
            End = targetPositionPathQueuedComponentLookup[entity].targetPosition,
            Filter = new CollisionFilter {
                BelongsTo = ~0u,
                CollidesWith = 1u << GameAssets.PATHFINDING_WALLS,
                GroupIndex = 0
            }
        };

        if (!collisionWorld.CastRay(raycastInput)) {
            // Did not hit anything, no walls in between
            unitMover.targetPosition = targetPositionPathQueuedComponentLookup[entity].targetPosition;
            flowFieldPathRequestComponentLookup.SetComponentEnabled(entity, false);
            flowFieldFollowerComponentLookup.SetComponentEnabled(entity, false);
        } else {
            // There's a wall in between
            
            if (GridSystem.IsValidWalkableGridPosition(targetPositionPathQueuedComponentLookup[entity].targetPosition, width, height, costMap, gridNodeSize)) {
                FlowFieldPathRequest flowFieldPathRequest = flowFieldPathRequestComponentLookup[entity];
                flowFieldPathRequest.targetPosition = targetPositionPathQueuedComponentLookup[entity].targetPosition;
                flowFieldPathRequestComponentLookup[entity] = flowFieldPathRequest;
                flowFieldPathRequestComponentLookup.SetComponentEnabled(entity, true);
            } else {
                if (moveOverrideComponentLookup.HasComponent(entity)) {
                    moveOverrideComponentLookup.SetComponentEnabled(entity, false);
                }
                unitMover.targetPosition = localTransform.Position;
                flowFieldPathRequestComponentLookup.SetComponentEnabled(entity, false);
                flowFieldFollowerComponentLookup.SetComponentEnabled(entity, false);
            }
        }

        targetPositionPathQueuedComponentLookup.SetComponentEnabled(entity, false);
    }
}


[BurstCompile]
[WithAll(typeof(FlowFieldFollower))]
public partial struct TestCanMoveStraightJob : IJobEntity {
    
    [NativeDisableParallelForRestriction] public ComponentLookup<FlowFieldFollower> flowFieldFollowerComponentLookup;
    
    [ReadOnly] public CollisionWorld collisionWorld;
    
    public void Execute(
        in LocalTransform localTransform,
        ref UnitMover unitMover,
        Entity entity) {

        FlowFieldFollower flowFieldFollower = flowFieldFollowerComponentLookup[entity];

        RaycastInput raycastInput = new RaycastInput {
            Start = localTransform.Position,
            End = flowFieldFollower.targetPosition,
            Filter = new CollisionFilter {
                BelongsTo = ~0u,
                CollidesWith = 1u << GameAssets.PATHFINDING_WALLS,
                GroupIndex = 0
            }
        };

        if (!collisionWorld.CastRay(raycastInput)) {
            // Did not hit anything, no walls in between
            unitMover.targetPosition = flowFieldFollower.targetPosition;
            flowFieldFollowerComponentLookup.SetComponentEnabled(entity, false);
        }
    }
}


[BurstCompile]
[WithAll(typeof(FlowFieldFollower))]
public partial struct FlowFieldFollowerJob : IJobEntity {
    
    [NativeDisableParallelForRestriction] public ComponentLookup<FlowFieldFollower> flowFieldFollowerComponentLookup;
    
    [ReadOnly] public ComponentLookup<GridSystem.GridNode> gridNodeComponentLookup;
    [ReadOnly] public float gridNodeSize;
    [ReadOnly] public float gridNodeSizeDouble;
    [ReadOnly] public int width;
    [ReadOnly] public int height;
    [ReadOnly] public NativeArray<Entity> totalGridMapEntityArray;
    
    public void Execute(
        in LocalTransform localTransform,
        ref UnitMover unitMover,
        Entity entity) {
        
        FlowFieldFollower flowFieldFollower = flowFieldFollowerComponentLookup[entity];
        int2 gridPosition = GridSystem.GetGridPosition(localTransform.Position, gridNodeSize);
        int index = GridSystem.CalculateIndex(gridPosition, width);
        int totalCount = width * height;
        Entity gridNodeEntity = totalGridMapEntityArray[totalCount * flowFieldFollower.gridIndex + index];
        GridSystem.GridNode gridNode = gridNodeComponentLookup[gridNodeEntity];
        float3 gridNodeMoveVector = GridSystem.GetWorldMovementVector(gridNode.vector);

        if (GridSystem.IsWall(gridNode)) {
            gridNodeMoveVector = flowFieldFollower.lastMoveVector;
        } else {
            flowFieldFollower.lastMoveVector = gridNodeMoveVector;
        }

        unitMover.targetPosition =
            GridSystem.GetWorldCenterPosition(gridPosition.x, gridPosition.y, gridNodeSize) +
            gridNodeMoveVector *
            gridNodeSizeDouble;

        if (math.distance(localTransform.Position, flowFieldFollower.targetPosition) < gridNodeSize) {
            // Target destination
            unitMover.targetPosition = localTransform.Position;
            flowFieldFollowerComponentLookup.SetComponentEnabled(entity, false);
        }

        flowFieldFollowerComponentLookup[entity] = flowFieldFollower;
    }
}