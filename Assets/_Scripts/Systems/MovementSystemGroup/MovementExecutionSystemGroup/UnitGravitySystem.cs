using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
[UpdateAfter(typeof(UnitMoverSystem))] // move first, then stick to ground
public partial struct UnitGravitySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        CollisionWorld collisionWorld = physicsWorldSingleton.CollisionWorld;

        float deltaTime = SystemAPI.Time.DeltaTime;

        CollisionFilter filter = new CollisionFilter
        {
            BelongsTo    = ~0u,
            CollidesWith = (1u << GlobalGameData.GROUND_LAYER) |
                           (1u << GlobalGameData.STRUCTURES_LAYER),
            GroupIndex   = 0,
        };

        var job = new UnitGravityJob
        {
            deltaTime       = deltaTime,
            collisionWorld  = collisionWorld,
            collisionFilter = filter,
            maxCheckDist    = 3f,
            snapTolerance   = 0.1f
        };

        job.ScheduleParallel();
    }
}

[BurstCompile]
[WithNone(typeof(Parent))] // only roots, not visual children
public partial struct UnitGravityJob : IJobEntity
{
    public float deltaTime;

    [ReadOnly] public CollisionWorld collisionWorld;
    [ReadOnly] public CollisionFilter collisionFilter;

    public float maxCheckDist;
    public float snapTolerance;

    public void Execute(
        ref LocalTransform transform,
        ref Gravity gravity)
    {
        float3 pos = transform.Position;

        // Cast down from just above feet
        float3 rayStart = pos + new float3(0f, 0.2f, 0f);
        float3 rayEnd   = pos - new float3(0f, maxCheckDist, 0f);

        var input = new RaycastInput
        {
            Start  = rayStart,
            End    = rayEnd,
            Filter = collisionFilter
        };

        bool hitGround = collisionWorld.CastRay(input, out var hit);

        if (hitGround)
        {
            float3 hitPoint = math.lerp(input.Start, input.End, hit.Fraction);
            float groundY = hitPoint.y + gravity.groundOffset;

            float distToGround = pos.y - groundY;

            if (distToGround > snapTolerance)
            {
                // We're above ground by more than tolerance → apply gravity
                gravity.verticalVelocity -= gravity.fallSpeed * deltaTime;
                pos.y += gravity.verticalVelocity * deltaTime;
                gravity.isGrounded = false;
            }
            else if (distToGround < -snapTolerance)
            {
                // We somehow ended up inside ground → snap up
                pos.y = groundY;
                gravity.verticalVelocity = 0f;
                gravity.isGrounded = true;
            }
            else
            {
                // We're close enough: snap & zero
                pos.y = groundY;
                gravity.verticalVelocity = 0f;
                gravity.isGrounded = true;
            }
        }
        else
        {
            // No ground below within range → freefall
            gravity.verticalVelocity -= gravity.fallSpeed * deltaTime;
            pos.y += gravity.verticalVelocity * deltaTime;
            gravity.isGrounded = false;
        }

        transform.Position = pos;
    }
}