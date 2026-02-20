using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Collections;

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
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        var job = new UnitMoverJob
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
        ref UnitMover unitMover,
        in PhysicsCollider physicsCollider)
    {
        if (deltaTime <= 0f) return;

        float3 currentPosition = localTransform.Position;
        float3 toTarget = unitMover.targetPosition - currentPosition;
        float distSq = math.lengthsq(toTarget);

        // Already at target
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

        // Update rotation to face movement direction
        if (unitMover.rotationSpeed > 0f && math.lengthsq(moveDir) > 0.001f)
        {
            float3 flatDir = math.normalize(new float3(moveDir.x, 0f, moveDir.z));
            if (math.lengthsq(flatDir) > 0.001f)
            {
                quaternion targetRotation = quaternion.LookRotationSafe(flatDir, math.up());
                localTransform.Rotation = math.slerp(
                    localTransform.Rotation, 
                    targetRotation, 
                    unitMover.rotationSpeed * deltaTime);
            }
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