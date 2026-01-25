using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;

[UpdateInGroup(typeof(MovementSystemGroup))]
partial struct UnitMoverSystem : ISystem
{
    public const float REACHED_TARGET_POSITION_DISTANCE_SQ = 0.04f;
    
    float deltaTime;
    GridSystem.GridSystemData gridSystemData;
    PhysicsWorldSingleton physicsWorldSingleton;
    CollisionWorld collisionWorld;

    ComponentLookup<TargetPositionPathQueued> targetPositionPathQueuedComponentLookup;
    ComponentLookup<FlowFieldPathRequest> flowFieldPathRequestComponentLookup;
    ComponentLookup<FlowFieldFollower> flowFieldFollowerComponentLookup;
    ComponentLookup<MoveOverride> moveOverrideComponentLookup;
    ComponentLookup<GridSystem.GridNode> gridNodeComponentLookup;
    ComponentLookup<PhysicsCollider> physicsColliderLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridSystem.GridSystemData>();
        state.RequireForUpdate<PhysicsWorldSingleton>();

        targetPositionPathQueuedComponentLookup = SystemAPI.GetComponentLookup<TargetPositionPathQueued>(false);
        flowFieldPathRequestComponentLookup     = SystemAPI.GetComponentLookup<FlowFieldPathRequest>(false);
        flowFieldFollowerComponentLookup        = SystemAPI.GetComponentLookup<FlowFieldFollower>(false);
        moveOverrideComponentLookup             = SystemAPI.GetComponentLookup<MoveOverride>(false);
        gridNodeComponentLookup                 = SystemAPI.GetComponentLookup<GridSystem.GridNode>(false);
        physicsColliderLookup                   = SystemAPI.GetComponentLookup<PhysicsCollider>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        UpdateLocalData(ref state);

        UpdateLookUps(ref state);

        RunTargetPostionPathQeued(ref state);

        RunTestCanMoveStraight(ref state);

        RunFlowFieldFollower(ref state);

        RunUnitMover(ref state);
    }

    private void UpdateLocalData(ref SystemState state)
    {
        gridSystemData = SystemAPI.GetSingleton<GridSystem.GridSystemData>(); 
        physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        collisionWorld = physicsWorldSingleton.CollisionWorld;
        deltaTime = SystemAPI.Time.DeltaTime;
    }
    
    private void UpdateLookUps(ref SystemState state)
    {
        targetPositionPathQueuedComponentLookup.Update(ref state);
        flowFieldPathRequestComponentLookup.Update(ref state);
        flowFieldFollowerComponentLookup.Update(ref state);
        moveOverrideComponentLookup.Update(ref state);
        gridNodeComponentLookup.Update(ref state);
        physicsColliderLookup.Update(ref state);
    }
    
    private void RunTargetPostionPathQeued(ref SystemState state)
    {
        TargetPositionPathQueuedJob targetPositionPathQueuedJob = new TargetPositionPathQueuedJob
        {
            collisionWorld                          = collisionWorld,
            gridNodeSize                            = gridSystemData.gridNodeSize,
            width                                   = gridSystemData.width,
            height                                  = gridSystemData.height,
            costMap                                 = gridSystemData.costMap,
            flowFieldFollowerComponentLookup        = flowFieldFollowerComponentLookup,
            flowFieldPathRequestComponentLookup     = flowFieldPathRequestComponentLookup,
            moveOverrideComponentLookup             = moveOverrideComponentLookup,
            targetPositionPathQueuedComponentLookup = targetPositionPathQueuedComponentLookup
        };
        targetPositionPathQueuedJob.ScheduleParallel();
    }

    private void RunTestCanMoveStraight(ref SystemState state)
    {
        TestCanMoveStraightJob testCanMoveStraightJob = new TestCanMoveStraightJob
        {
            collisionWorld                   = collisionWorld,
            flowFieldFollowerComponentLookup = flowFieldFollowerComponentLookup,
        };
        testCanMoveStraightJob.ScheduleParallel();
    }
    
    private void RunFlowFieldFollower(ref SystemState state)
    {
        FlowFieldFollowerJob flowFieldFollowerJob = new FlowFieldFollowerJob
        {
            width                          = gridSystemData.width,
            height                         = gridSystemData.height,
            gridNodeSize                   = gridSystemData.gridNodeSize,
            gridNodeSizeDouble             = gridSystemData.gridNodeSize * 2f,
            flowFieldFollowerComponentLookup = flowFieldFollowerComponentLookup,
            totalGridMapEntityArray        = gridSystemData.totalGridMapEntityArray,
            gridNodeComponentLookup        = gridNodeComponentLookup,
        };
        flowFieldFollowerJob.ScheduleParallel();
    }

    private void RunUnitMover(ref SystemState state)
    {
        UnitMoverJob unitMoverJob = new UnitMoverJob
        {
            deltaTime       = deltaTime,
            collisionWorld  = collisionWorld,
        };

        unitMoverJob.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct UnitMoverJob : IJobEntity
{
    public float deltaTime;
    [ReadOnly] public CollisionWorld collisionWorld;

    public void Execute(
        ref LocalTransform localTransform, 
        ref UnitMover unitMover,
        in PhysicsCollider physicsCollider)
    {
        if (deltaTime <= 0f) return;

        float3 currentPosition = localTransform.Position;
        float3 toTarget = unitMover.targetPosition - currentPosition;
        float distSq = math.lengthsq(toTarget);

        if (distSq < 0.0001f)
        {
            unitMover.isMoving = false;
            return;
        }

        float dist = math.sqrt(distSq);
        float moveDist = math.min(dist, unitMover.moveSpeed * deltaTime);
        float3 moveDir = toTarget / dist;
        float3 desiredMove = moveDir * moveDist;

        unsafe
        {
            float3 newPosition = CalculateMovementWithCollision(
                currentPosition, 
                desiredMove, 
                moveDir,
                physicsCollider.ColliderPtr,
                localTransform.Rotation);

            localTransform.Position = newPosition;
        }

        float3 actualMove = localTransform.Position - currentPosition;
        unitMover.isMoving = math.lengthsq(actualMove) > 1e-6f;
    }

    private unsafe float3 CalculateMovementWithCollision(
        float3 currentPosition,
        float3 desiredMove,
        float3 moveDir,
        Unity.Physics.Collider* collider,
        quaternion orientation)
    {
        ColliderCastHit hit;
        bool haveHit = CastCollider(currentPosition, desiredMove, collider, orientation, out hit);
        
        if (!haveHit)
        {
            return currentPosition + desiredMove;
        }

        if (IsMovingAwayFromSurface(hit, moveDir))
        {
            return currentPosition + desiredMove;
        }

        return CalculateSlideMovement(currentPosition, desiredMove, hit, collider, orientation);
    }

    private unsafe bool CastCollider(
        float3 start,
        float3 movement,
        Unity.Physics.Collider* collider,
        quaternion orientation,
        out ColliderCastHit hit)
    {
        ColliderCastInput castInput = new ColliderCastInput()
        {
            Collider = collider,
            Orientation = orientation,
            Start = start,
            End = start + movement
        };

        return collisionWorld.CastCollider(castInput, out hit);
    }

    private bool IsMovingAwayFromSurface(ColliderCastHit hit, float3 moveDir)
    {
        if (hit.Fraction >= 0.01f)
        {
            return false;
        }

        float3 normal = hit.SurfaceNormal;
        float moveTowardsWall = math.dot(moveDir, -normal);
        
        return moveTowardsWall < 0f;
    }

    private unsafe float3 CalculateSlideMovement(
        float3 currentPosition,
        float3 desiredMove,
        ColliderCastHit hit,
        Unity.Physics.Collider* collider,
        quaternion orientation)
    {
        float3 normal = hit.SurfaceNormal;
        float3 moveToHit = desiredMove * hit.Fraction;
        float3 hitPos = currentPosition + moveToHit;

        // Push slightly away from surface to avoid immediate re-collision
        float3 separationOffset = normal * 0.001f;
        hitPos += separationOffset;

        // Calculate slide direction (project remaining movement onto surface)
        float3 remainingMove = desiredMove * (1f - hit.Fraction);
        float3 slideDir = remainingMove - math.dot(remainingMove, normal) * normal;

        if (math.lengthsq(slideDir) < 0.0001f)
        {
            return hitPos;
        }

        return TrySlideAlongSurface(hitPos, slideDir, collider, orientation);
    }

    private unsafe float3 TrySlideAlongSurface(
        float3 hitPos,
        float3 slideDir,
        Unity.Physics.Collider* collider,
        quaternion orientation)
    {
        ColliderCastHit slideHit;
        bool hasSlideHit = CastCollider(hitPos, slideDir, collider, orientation, out slideHit);

        if (hasSlideHit)
        {
            return hitPos + slideDir * slideHit.Fraction;
        }

        return hitPos + slideDir;
    }
}

[BurstCompile]
[WithAll(typeof(TargetPositionPathQueued))]
public partial struct TargetPositionPathQueuedJob : IJobEntity
{
    [NativeDisableParallelForRestriction] public ComponentLookup<TargetPositionPathQueued> targetPositionPathQueuedComponentLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<FlowFieldPathRequest>    flowFieldPathRequestComponentLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<FlowFieldFollower>      flowFieldFollowerComponentLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<MoveOverride>           moveOverrideComponentLookup;

    [ReadOnly] public CollisionWorld    collisionWorld;
    [ReadOnly] public int               width;
    [ReadOnly] public int               height;
    [ReadOnly] public NativeArray<byte> costMap;
    [ReadOnly] public float             gridNodeSize;

    public void Execute(
        in LocalTransform localTransform,
        ref UnitMover unitMover,
        Entity entity)
    {
        RaycastInput raycastInput = new RaycastInput
        {
            Start = localTransform.Position,
            End   = targetPositionPathQueuedComponentLookup[entity].targetPosition,
            Filter = new CollisionFilter
            {
                BelongsTo    = ~0u,
                CollidesWith = 1u << GameAssets.WALLS_LAYER,
                GroupIndex   = 0
            }
        };

        if (!collisionWorld.CastRay(raycastInput))
        {
            // Did not hit anything, no walls in between
            unitMover.targetPosition = targetPositionPathQueuedComponentLookup[entity].targetPosition;
            flowFieldPathRequestComponentLookup.SetComponentEnabled(entity, false);
            flowFieldFollowerComponentLookup.SetComponentEnabled(entity, false);
        }
        else
        {
            // There's a wall in between
            if (moveOverrideComponentLookup.HasComponent(entity))
            {
                moveOverrideComponentLookup.SetComponentEnabled(entity, false);
            }

            if (GridSystem.IsValidWalkableGridPosition(
                    targetPositionPathQueuedComponentLookup[entity].targetPosition,
                    width, height, costMap, gridNodeSize))
            {
                FlowFieldPathRequest flowFieldPathRequest = flowFieldPathRequestComponentLookup[entity];
                flowFieldPathRequest.targetPosition       = targetPositionPathQueuedComponentLookup[entity].targetPosition;
                flowFieldPathRequestComponentLookup[entity] = flowFieldPathRequest;
                flowFieldPathRequestComponentLookup.SetComponentEnabled(entity, true);
            }
            else
            {
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
public partial struct TestCanMoveStraightJob : IJobEntity
{
    [NativeDisableParallelForRestriction] public ComponentLookup<FlowFieldFollower> flowFieldFollowerComponentLookup;

    [ReadOnly] public CollisionWorld collisionWorld;

    public void Execute(
        in LocalTransform localTransform,
        ref UnitMover unitMover,
        Entity entity)
    {
        FlowFieldFollower flowFieldFollower = flowFieldFollowerComponentLookup[entity];

        RaycastInput raycastInput = new RaycastInput
        {
            Start = localTransform.Position,
            End   = flowFieldFollower.targetPosition,
            Filter = new CollisionFilter
            {
                BelongsTo    = ~0u,
                CollidesWith = 1u << GameAssets.WALLS_LAYER,
                GroupIndex   = 0
            }
        };

        if (!collisionWorld.CastRay(raycastInput))
        {
            // Did not hit anything, no walls in between
            unitMover.targetPosition = flowFieldFollower.targetPosition;
            flowFieldFollowerComponentLookup.SetComponentEnabled(entity, false);
        }
    }
}

[BurstCompile]
[WithAll(typeof(FlowFieldFollower))]
public partial struct FlowFieldFollowerJob : IJobEntity
{
    [NativeDisableParallelForRestriction] public ComponentLookup<FlowFieldFollower> flowFieldFollowerComponentLookup;

    [ReadOnly] public ComponentLookup<GridSystem.GridNode> gridNodeComponentLookup;
    [ReadOnly] public float                                gridNodeSize;
    [ReadOnly] public float                                gridNodeSizeDouble;
    [ReadOnly] public int                                  width;
    [ReadOnly] public int                                  height;
    [ReadOnly] public NativeArray<Entity>                  totalGridMapEntityArray;

    public void Execute(
        in LocalTransform localTransform,
        ref UnitMover unitMover,
        Entity entity)
    {
        FlowFieldFollower flowFieldFollower = flowFieldFollowerComponentLookup[entity];

        int2 gridPosition   = GridSystem.GetGridPosition(localTransform.Position, gridNodeSize);
        int  index          = GridSystem.CalculateIndex(gridPosition, width);
        int  totalCount     = width * height;
        Entity gridNodeEntity = totalGridMapEntityArray[totalCount * flowFieldFollower.gridIndex + index];
        GridSystem.GridNode gridNode = gridNodeComponentLookup[gridNodeEntity];
        float3 gridNodeMoveVector    = GridSystem.GetWorldMovementVector(gridNode.vector);

        if (GridSystem.IsWall(gridNode))
        {
            gridNodeMoveVector = flowFieldFollower.lastMoveVector;
        }
        else
        {
            flowFieldFollower.lastMoveVector = gridNodeMoveVector;
        }

        unitMover.targetPosition =
            GridSystem.GetWorldCenterPosition(gridPosition.x, gridPosition.y, gridNodeSize) +
            gridNodeMoveVector * gridNodeSizeDouble;

        if (math.distance(localTransform.Position, flowFieldFollower.targetPosition) < gridNodeSize)
        {
            // Target destination
            unitMover.targetPosition = localTransform.Position;
            flowFieldFollowerComponentLookup.SetComponentEnabled(entity, false);
        }

        flowFieldFollowerComponentLookup[entity] = flowFieldFollower;
    }
}
