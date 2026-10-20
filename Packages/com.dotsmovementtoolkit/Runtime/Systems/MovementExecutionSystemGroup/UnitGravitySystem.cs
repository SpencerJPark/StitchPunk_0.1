using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace DotsMovementToolkit
{
[BurstCompile]
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
[UpdateAfter(typeof(UnitMoverSystem))] // move first, then stick to ground
public partial struct UnitGravitySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<NavGridSettings>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        PhysicsWorldSingleton physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        CollisionWorld collisionWorld = physicsWorldSingleton.CollisionWorld;

        float deltaTime = SystemAPI.Time.DeltaTime;

        CollisionFilter filter = new CollisionFilter
        {
            BelongsTo    = ~0u,
            CollidesWith = SystemAPI.GetSingleton<NavGridSettings>().groundLayerMask,
            GroupIndex   = 0,
        };

        UnitGravityJob job = new UnitGravityJob
        {
            deltaTime       = deltaTime,
            collisionWorld  = collisionWorld,
            collisionFilter = filter,
            maxCheckDist    = 3f
        };

        job.ScheduleParallel();
    }
}

[BurstCompile]
[WithNone(typeof(Parent))]  // only roots, not visual children
public partial struct UnitGravityJob : IJobEntity
{
    public float deltaTime;

    [ReadOnly] public CollisionWorld collisionWorld;
    [ReadOnly] public CollisionFilter collisionFilter;

    public float maxCheckDist;

    public void Execute(
        ref LocalTransform transform,
        ref Gravity gravity)
    {
        float3 position = transform.Position;
        float3 up       = new float3(0f, 1f, 0f);

        RaycastInput input = new RaycastInput
        {
            Start  = position + up * GravityStep.PROBE_HEIGHT,
            End    = position - up * GravityStep.SweepDistance(gravity.verticalVelocity, gravity.fallSpeed, deltaTime, maxCheckDist),
            Filter = collisionFilter
        };

        bool hasGround = collisionWorld.CastRay(input, out RaycastHit hit);
        float groundY = 0f;
        if (hasGround)
        {
            float3 hitPoint = math.lerp(input.Start, input.End, hit.Fraction);
            groundY = hitPoint.y + gravity.groundOffset;
        }

        GravityStepResult result = GravityStep.Resolve(position.y, gravity.verticalVelocity, gravity.fallSpeed, deltaTime, hasGround, groundY);

        position.y               = result.positionY;
        gravity.verticalVelocity = result.verticalVelocity;
        gravity.isGrounded       = result.isGrounded;
        transform.Position       = position;

        if (result.isGrounded)
        {
            gravity.lastGroundedPosition    = transform.Position;
            gravity.hasLastGroundedPosition = true;
        }
        else if (!hasGround && gravity.hasLastGroundedPosition
                 && transform.Position.y < gravity.lastGroundedPosition.y - GravityStep.RECOVERY_FALL_DISTANCE)
        {
            // Last-resort net: a unit that fell through the world comes back instead of falling forever.
            transform.Position       = gravity.lastGroundedPosition;
            gravity.verticalVelocity = 0f;
        }
    }
}
}