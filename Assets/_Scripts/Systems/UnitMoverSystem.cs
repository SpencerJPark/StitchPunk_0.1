using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;

partial struct UnitMoverSystem : ISystem
{
    public const float REACHED_TARGET_POSITION_DISTANCE_SQ = 0.04f;
    public const float BLOCKED_TARGET_POSITION_DISTANCE_SQ = .05f;
    
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
        CollisionFilter filter = new CollisionFilter
        {
            BelongsTo    = ~0u,
            CollidesWith = (1u << GameAssets.PATHFINDING_WALLS) |
                           (1u << GameAssets.BUILDINGS_LAYER),
            GroupIndex   = 0
        };

        UnitMoverJob unitMoverJob = new UnitMoverJob
        {
            deltaTime       = deltaTime,
            collisionWorld  = collisionWorld,
            collisionFilter = filter,
        };

        unitMoverJob.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct UnitMoverJob : IJobEntity
{
    public float deltaTime;
    [ReadOnly] public CollisionWorld collisionWorld;
    [ReadOnly] public CollisionFilter collisionFilter;

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
            // Cast the character's collider along the movement path
            ColliderCastInput castInput = new ColliderCastInput()
            {
                Collider = physicsCollider.ColliderPtr,
                Orientation = localTransform.Rotation,
                Start = currentPosition,
                End = currentPosition + desiredMove
            };

            ColliderCastHit hit;
            bool haveHit = collisionWorld.CastCollider(castInput, out hit);
            
            if (haveHit)
            {
                // Check if we're moving away from the surface (dot product with normal)
                float3 normal = hit.SurfaceNormal;
                float moveTowardsWall = math.dot(moveDir, -normal);
                
                // If moving away from wall (negative dot product) and already touching (low fraction), allow movement
                if (hit.Fraction < 0.01f && moveTowardsWall < 0f)
                {
                    // Already touching and moving away - allow free movement
                    localTransform.Position = currentPosition + desiredMove;
                }
                else
                {
                    // Hit something ahead - move up to hit point
                    float3 moveToHit = desiredMove * hit.Fraction;
                    float3 hitPos = currentPosition + moveToHit;

                    // Push slightly away from surface to avoid immediate re-collision
                    float3 separationOffset = normal * 0.001f;
                    hitPos += separationOffset;

                    // Calculate slide direction (project remaining movement onto surface)
                    float3 remainingMove = desiredMove * (1f - hit.Fraction);
                    float3 slideDir = remainingMove - math.dot(remainingMove, normal) * normal;
                
                    // Try sliding if there's meaningful movement left
                    if (math.lengthsq(slideDir) > 0.0001f)
                    {
                        // Normalize and scale to maintain desired speed
                        float slideLength = math.length(slideDir);
                        
                        ColliderCastInput slideInput = new ColliderCastInput()
                        {
                            Collider = physicsCollider.ColliderPtr,
                            Orientation = localTransform.Rotation,
                            Start = hitPos,
                            End = hitPos + slideDir
                        };

                        ColliderCastHit slideHit;
                        if (collisionWorld.CastCollider(slideInput, out slideHit))
                        {
                            // Hit another surface while sliding
                            localTransform.Position = hitPos + slideDir * slideHit.Fraction;
                        }
                        else
                        {
                            // Free to slide along surface
                            localTransform.Position = hitPos + slideDir;
                        }
                    }
                    else
                    {
                        localTransform.Position = hitPos;
                    }
                }
            }
            else
            {
                // No collision - move freely
                localTransform.Position = currentPosition + desiredMove;
            }
        }

        float3 actualMove = localTransform.Position - currentPosition;
        unitMover.isMoving = math.lengthsq(actualMove) > 1e-6f;
        unitMover.blocked = !unitMover.isMoving && distSq > 0.0001f;
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
                CollidesWith = 1u << GameAssets.PATHFINDING_WALLS,
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
                CollidesWith = 1u << GameAssets.PATHFINDING_WALLS,
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
