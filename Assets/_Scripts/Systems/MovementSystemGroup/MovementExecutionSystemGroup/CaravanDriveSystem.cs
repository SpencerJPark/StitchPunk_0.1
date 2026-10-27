using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

// Kinematic driving with the original VehicleController's handling (Caravan_System.md CV-D17), tuned per vehicle
// by its VehicleProfileSO (CV-D18). Heading and speed come from CaravanDriveMath, a sphere cast at the front bumper
// stops it at walls, a down-ray keeps it on the ground. Units are not walls here: running them over is
// CaravanCrashSystem's job. Runs after gravity and body-block so the seated driver is placed last.
[BurstCompile]
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
[UpdateAfter(typeof(UnitGravitySystem))]
[UpdateAfter(typeof(PlayerBodyBlockSystem))]
public partial struct CaravanDriveSystem : ISystem
{
    // TagManager layer "Units".
    private const int UNITS_PHYSICS_LAYER = 6;
    // Anything flatter than this is ground to roll over, not a wall to stop at.
    private const float WALL_NORMAL_MAX_Y = 0.6f;
    private const float WALL_STOP_GAP = 0.05f;
    private const float GROUND_PROBE_ABOVE = 3f;
    private const float GROUND_PROBE_BELOW = 4f;
    // The body only swings round after the heading while it is actually rolling.
    private const float ROLLING_SPEED = 0.03f;
    // Heading this far ahead of the body puts the front axle at full lock.
    private const float FULL_LOCK_DEGREES = 30f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Caravan>();
        state.RequireForUpdate<VehicleLibrary>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<NavGridSettings>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        if (deltaTime <= 0f) return;
        // Parked is the common case: skip the sync point below unless someone is actually driving.
        if (SystemAPI.QueryBuilder().WithAll<CaravanDriving>().Build().IsEmpty) return;
        // Main-thread lookup reads/writes on other entities wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();

        BlobAssetReference<VehicleLibraryBlob> vehicleLibrary = SystemAPI.GetSingleton<VehicleLibrary>().library;
        if (!vehicleLibrary.IsCreated) return;

        CollisionWorld collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        CollisionFilter wallFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = ~(1u << UNITS_PHYSICS_LAYER),
        };
        CollisionFilter groundFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = SystemAPI.GetSingleton<NavGridSettings>().groundLayerMask,
        };
        NativeList<ColliderCastHit> wallHits = new NativeList<ColliderCastHit>(16, Allocator.Temp);
        NativeList<Unity.Physics.RaycastHit> groundHits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);

        Entity seatedDriver = Entity.Null;
        float3 seatPosition = float3.zero;
        quaternion seatRotation = quaternion.identity;

        foreach ((RefRW<LocalTransform> caravanTransform, RefRW<CaravanDriveState> driveState, RefRO<Vehicle> vehicle,
                     RefRO<CaravanDriving> caravanDriving, Entity caravanEntity)
                 in SystemAPI.Query<RefRW<LocalTransform>, RefRW<CaravanDriveState>, RefRO<Vehicle>, RefRO<CaravanDriving>>()
                     .WithAll<Caravan>()
                     .WithEntityAccess())
        {
            int vehicleIndex = (int)vehicle.ValueRO.vehicleType;
            if (vehicleIndex <= 0 || vehicleIndex >= vehicleLibrary.Value.vehicles.Length) continue;
            ref VehicleProfileBlob profile = ref vehicleLibrary.Value.vehicles[vehicleIndex];

            Entity driverEntity = caravanDriving.ValueRO.driver;
            float2 steer = SystemAPI.HasComponent<CaravanDriveInput>(driverEntity)
                ? SystemAPI.GetComponent<CaravanDriveInput>(driverEntity).steer
                : float2.zero;

            // The stick is a world direction to head toward (world axes, like the player's own WASD) and its tilt is
            // the target speed. There is no reverse.
            float2 stick = math.lengthsq(steer) > 1f ? math.normalize(steer) : steer;
            float forwardSpeed = CaravanDriveMath.IntegrateSpeed(driveState.ValueRO.forwardSpeed,
                math.length(stick) * profile.moveSpeed, profile.forwardAcceleration, profile.forwardDeceleration, deltaTime);

            float headingDegrees = driveState.ValueRO.headingDegrees;
            float headingTurnVelocity = driveState.ValueRO.headingTurnVelocity;
            float targetHeadingDegrees = math.lengthsq(stick) > 0.0001f
                ? CaravanDriveMath.HeadingDegrees(new float3(stick.x, 0f, stick.y))
                : headingDegrees;
            float turnSpeedLimit = CaravanDriveMath.TurnSpeedLimit(forwardSpeed, profile.moveSpeed, profile.turnSpeed,
                profile.idleTurnSpeedFactor);
            headingDegrees = CaravanDriveMath.SmoothDampAngle(headingDegrees, targetHeadingDegrees, ref headingTurnVelocity,
                profile.turnSmoothTime, turnSpeedLimit, deltaTime);
            float3 travelDirection = CaravanDriveMath.HeadingDirection(headingDegrees);

            float bodyYawDegrees = CaravanDriveMath.HeadingDegrees(math.rotate(caravanTransform.ValueRO.Rotation, new float3(0f, 0f, 1f)));
            float newBodyYawDegrees = bodyYawDegrees;
            if (forwardSpeed > ROLLING_SPEED)
            {
                float maximumBodyTurn = profile.turnSpeed * deltaTime;
                newBodyYawDegrees += math.clamp(CaravanDriveMath.DeltaAngle(bodyYawDegrees, headingDegrees),
                    -maximumBodyTurn, maximumBodyTurn);
            }
            quaternion rotation = quaternion.RotateY(math.radians(newBodyYawDegrees));
            float3 position = caravanTransform.ValueRO.Position;

            float stepLength = forwardSpeed * deltaTime;
            if (stepLength > 0.00001f)
            {
                float allowedFraction = FrontBumperClearFraction(collisionWorld, wallFilter, ref wallHits, position, rotation,
                    travelDirection, stepLength, ref profile, caravanEntity, driverEntity);
                if (allowedFraction < 1f)
                {
                    // Walls stop a cart dead; there is no slide along them.
                    stepLength *= math.max(0f, allowedFraction - WALL_STOP_GAP / stepLength);
                    forwardSpeed = 0f;
                }
                position += travelDirection * stepLength;
            }

            position.y = GroundHeightUnder(collisionWorld, groundFilter, ref groundHits, position, caravanEntity, driverEntity);

            // The facing the body shows the camera picks the seat and puller offsets, quantised the way units are.
            float3 bodyForward = CaravanDriveMath.HeadingDirection(newBodyYawDegrees);
            float2 bodyForwardXY = new float2(bodyForward.x, bodyForward.z);
            Direction facing = FacingResolver.FromMovement(in bodyForwardXY, AnimationDirections.Eight, driveState.ValueRO.facing);

            caravanTransform.ValueRW.Position = position;
            caravanTransform.ValueRW.Rotation = rotation;
            driveState.ValueRW.forwardSpeed = forwardSpeed;
            driveState.ValueRW.headingDegrees = headingDegrees;
            driveState.ValueRW.headingTurnVelocity = headingTurnVelocity;
            driveState.ValueRW.yawRateDegrees = CaravanDriveMath.DeltaAngle(bodyYawDegrees, newBodyYawDegrees) / deltaTime;
            // The front axle points where the caravan is heading, ahead of where the body faces.
            driveState.ValueRW.steerInput = math.clamp(CaravanDriveMath.DeltaAngle(newBodyYawDegrees, headingDegrees) / FULL_LOCK_DEGREES, -1f, 1f);
            driveState.ValueRW.facing = facing;
            driveState.ValueRW.pullerGait = profile.Gait(forwardSpeed);
            driveState.ValueRW.wheelSpinRadians = CaravanDriveMath.AdvanceWheelSpin(
                driveState.ValueRO.wheelSpinRadians, forwardSpeed, profile.wheelRadius, deltaTime);

            seatedDriver = driverEntity;
            seatPosition = position + math.rotate(rotation, profile.DriverSeatOffset(facing));
            seatRotation = rotation;
        }

        // Written after the loop: the query above holds LocalTransform read-write.
        if (seatedDriver != Entity.Null && SystemAPI.HasComponent<LocalTransform>(seatedDriver))
        {
            LocalTransform driverTransform = SystemAPI.GetComponent<LocalTransform>(seatedDriver);
            driverTransform.Position = seatPosition;
            driverTransform.Rotation = seatRotation;
            SystemAPI.SetComponent(seatedDriver, driverTransform);
            if (SystemAPI.HasComponent<Movement>(seatedDriver))
                SystemAPI.GetComponentRW<Movement>(seatedDriver).ValueRW.targetPosition = seatPosition;
            if (SystemAPI.HasComponent<Gravity>(seatedDriver))
                SystemAPI.GetComponentRW<Gravity>(seatedDriver).ValueRW.verticalVelocity = 0f;
        }

        wallHits.Dispose();
        groundHits.Dispose();
    }

    // Fraction of this frame's step the front bumper can travel before it meets a wall (1 = clear).
    private static float FrontBumperClearFraction(
        in CollisionWorld collisionWorld,
        CollisionFilter wallFilter,
        ref NativeList<ColliderCastHit> wallHits,
        float3 position,
        quaternion rotation,
        float3 travelDirection,
        float stepLength,
        ref VehicleProfileBlob profile,
        Entity caravanEntity,
        Entity driverEntity)
    {
        float bumperRadius = profile.halfWidth;
        float3 bumperCentre = position + math.rotate(rotation,
            new float3(0f, profile.bumperGroundClearance + bumperRadius, profile.frontBumperLocalZ - bumperRadius));

        wallHits.Clear();
        collisionWorld.SphereCastAll(bumperCentre, bumperRadius, travelDirection, stepLength, ref wallHits,
            wallFilter, QueryInteraction.IgnoreTriggers);

        float allowedFraction = 1f;
        for (int hitIndex = 0; hitIndex < wallHits.Length; hitIndex++)
        {
            ColliderCastHit wallHit = wallHits[hitIndex];
            if (wallHit.Entity == caravanEntity || wallHit.Entity == driverEntity) continue;
            if (wallHit.SurfaceNormal.y > WALL_NORMAL_MAX_Y) continue;
            // Already touching and moving away from it: not a block.
            if (math.dot(wallHit.SurfaceNormal, travelDirection) > 0f) continue;
            allowedFraction = math.min(allowedFraction, wallHit.Fraction);
        }
        return allowedFraction;
    }

    private static float GroundHeightUnder(
        in CollisionWorld collisionWorld,
        CollisionFilter groundFilter,
        ref NativeList<Unity.Physics.RaycastHit> groundHits,
        float3 position,
        Entity caravanEntity,
        Entity driverEntity)
    {
        float3 up = new float3(0f, 1f, 0f);
        RaycastInput groundRay = new RaycastInput
        {
            Start = position + up * GROUND_PROBE_ABOVE,
            End = position - up * GROUND_PROBE_BELOW,
            Filter = groundFilter,
        };

        groundHits.Clear();
        collisionWorld.CastRay(groundRay, ref groundHits);

        float bestFraction = 2f;
        float groundHeight = position.y;
        for (int hitIndex = 0; hitIndex < groundHits.Length; hitIndex++)
        {
            Unity.Physics.RaycastHit groundHit = groundHits[hitIndex];
            if (groundHit.Entity == caravanEntity || groundHit.Entity == driverEntity) continue;
            if (groundHit.Fraction >= bestFraction) continue;
            bestFraction = groundHit.Fraction;
            groundHeight = groundHit.Position.y;
        }
        return groundHeight;
    }
}
