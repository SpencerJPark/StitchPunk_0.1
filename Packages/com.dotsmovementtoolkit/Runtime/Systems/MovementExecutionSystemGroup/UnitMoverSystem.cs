using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Collections;

namespace DotsMovementToolkit
{
/// <summary>
/// Executes movement for all entities with UnitMover component.
/// This system only handles the actual movement - pathfinding systems
/// (FlowFieldFollowerSystem, DStarLiteMovementSystem) set the targetPosition.
///
/// Handles collision detection and wall sliding.
/// </summary>
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
public partial struct UnitMoverSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        PhysicsWorldSingleton physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        UnitMoverJob job = new UnitMoverJob
        {
            deltaTime = deltaTime,
            collisionWorld = physicsWorld.CollisionWorld
        };
        
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }
}

/// <summary>
/// Moves entities toward their target position with collision handling.
/// </summary>
[BurstCompile]
public partial struct UnitMoverJob : IJobEntity
{
    public float deltaTime;
    [ReadOnly] public CollisionWorld collisionWorld;

    public void Execute(
        ref LocalTransform localTransform, 
        ref Movement movement,
        in PhysicsCollider physicsCollider)
    {
        if (deltaTime <= 0f) return;

        float3 currentPosition = localTransform.Position;
        float3 toTarget = movement.targetPosition - currentPosition;
        float distSq = math.lengthsq(toTarget);

        // Already at target
        if (distSq < 0.0001f)
        {
            movement.isMoving = false;
            return;
        }

        float dist = math.sqrt(distSq);
        float activeSpeed = movement.isRunning ? movement.runSpeed : movement.moveSpeed;
        float moveDist = math.min(dist, activeSpeed * deltaTime);
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

        // Update rotation to face movement direction
        if (movement.rotationSpeed > 0f && math.lengthsq(moveDir) > 0.001f)
        {
            float3 flatDir = math.normalize(new float3(moveDir.x, 0f, moveDir.z));
            if (math.lengthsq(flatDir) > 0.001f)
            {
                quaternion targetRotation = quaternion.LookRotationSafe(flatDir, math.up());
                quaternion turnedRotation = math.slerp(
                    localTransform.Rotation,
                    targetRotation,
                    movement.rotationSpeed * deltaTime);
                unsafe
                {
                    if (!TurnSwingsColliderIntoWall(localTransform.Position, localTransform.Rotation, turnedRotation, physicsCollider.ColliderPtr))
                    {
                        localTransform.Rotation = turnedRotation;
                    }
                }
            }
        }

        float3 actualMove = localTransform.Position - currentPosition;
        movement.isMoving = math.lengthsq(actualMove) > 1e-6f;
    }

    // A head-on push into a round trunk projects to almost no sideways motion, which reads as
    // "stopped dead". Past a small angle the slide keeps at least this share of the blocked move,
    // so pushing into an obstacle creeps round it slowly.
    private const float MinimumSlideShareOfBlockedMove = 0.25f;
    // Turns smaller than this (quaternion dot) skip the wall check; slerp converges asymptotically.
    private const float NegligibleTurnDot = 0.99999f;
    // Below this share the push is treated as square-on and the unit simply stops.
    private const float SquareOnSlideShareThreshold = 0.02f;
    // Normals flatter than this are floor or ceiling; only walls push a unit out sideways.
    private const float MinimumHorizontalNormalForPushOut = 0.5f;
    private const int MaximumPushOutIterations = 4;
    private const float PushOutSkinMetres = 0.002f;

    private unsafe float3 CalculateMovementWithCollision(
        float3 currentPosition,
        float3 desiredMove,
        float3 moveDir,
        Unity.Physics.Collider* collider,
        quaternion orientation)
    {
        ColliderCastHit hit;
        bool haveHit = CastCollider(currentPosition, desiredMove, collider, orientation, out hit);

        // A cast that starts inside a wall reports a meaningless normal and lets the unit walk
        // through. Turning swings an off-centre collider into walls unchecked, so this is common.
        if (haveHit && hit.Fraction <= 0f)
        {
            float3 pushedOutPosition = PushOutOfWalls(currentPosition, collider, orientation);
            if (math.any(pushedOutPosition != currentPosition))
            {
                currentPosition = pushedOutPosition;
                haveHit = CastCollider(currentPosition, desiredMove, collider, orientation, out hit);
            }
        }

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
        ColliderCastInput castInput = new ColliderCastInput
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

        float remainingLength = math.length(remainingMove);
        float slideLength = math.length(slideDir);
        if (remainingLength < 1e-5f || slideLength < remainingLength * SquareOnSlideShareThreshold)
        {
            return hitPos;
        }

        float boostedSlideLength = math.max(slideLength, remainingLength * MinimumSlideShareOfBlockedMove);
        slideDir *= boostedSlideLength / slideLength;

        return TrySlideAlongSurface(hitPos, slideDir, collider, orientation);
    }

    // An off-centre collider sweeps an arc when the unit turns; letting it swing into a trunk and
    // then pushing out shoves the unit sideways, which reads as bouncing. Hold the old facing instead.
    private unsafe bool TurnSwingsColliderIntoWall(float3 position, quaternion currentRotation, quaternion turnedRotation, Unity.Physics.Collider* collider)
    {
        if (math.abs(math.dot(currentRotation.value, turnedRotation.value)) > NegligibleTurnDot)
        {
            return false;
        }

        NativeList<DistanceHit> overlaps = new NativeList<DistanceHit>(8, Allocator.Temp);
        float turnedPenetration = DeepestWallPenetration(position, turnedRotation, collider, ref overlaps, out float3 unusedNormal);
        bool swingsIntoWall = false;
        if (turnedPenetration > 0f)
        {
            float currentPenetration = DeepestWallPenetration(position, currentRotation, collider, ref overlaps, out unusedNormal);
            swingsIntoWall = turnedPenetration > currentPenetration + PushOutSkinMetres;
        }
        overlaps.Dispose();
        return swingsIntoWall;
    }

    private unsafe float3 PushOutOfWalls(float3 position, Unity.Physics.Collider* collider, quaternion orientation)
    {
        NativeList<DistanceHit> overlaps = new NativeList<DistanceHit>(8, Allocator.Temp);
        for (int iteration = 0; iteration < MaximumPushOutIterations; iteration++)
        {
            float deepestPenetration = DeepestWallPenetration(position, orientation, collider, ref overlaps, out float3 outOfWallNormal);
            if (deepestPenetration <= 0f) break;
            position += outOfWallNormal * (deepestPenetration + PushOutSkinMetres);
        }
        overlaps.Dispose();
        return position;
    }

    // Floor contacts are ignored; the returned normal is horizontal and points out of the wall.
    private unsafe float DeepestWallPenetration(float3 position, quaternion orientation, Unity.Physics.Collider* collider, ref NativeList<DistanceHit> overlaps, out float3 outOfWallNormal)
    {
        ColliderDistanceInput overlapInput = new ColliderDistanceInput
        {
            Collider = collider,
            Transform = new RigidTransform(orientation, position),
            Scale = 1f,
            MaxDistance = 0f,
        };
        overlaps.Clear();
        collisionWorld.CalculateDistance(overlapInput, ref overlaps);

        float deepestPenetration = 0f;
        outOfWallNormal = float3.zero;
        for (int overlapIndex = 0; overlapIndex < overlaps.Length; overlapIndex++)
        {
            DistanceHit overlap = overlaps[overlapIndex];
            float3 horizontalNormal = new float3(overlap.SurfaceNormal.x, 0f, overlap.SurfaceNormal.z);
            if (math.length(horizontalNormal) < MinimumHorizontalNormalForPushOut) continue;
            if (-overlap.Distance > deepestPenetration)
            {
                deepestPenetration = -overlap.Distance;
                outOfWallNormal = math.normalize(horizontalNormal);
            }
        }
        return deepestPenetration;
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
}