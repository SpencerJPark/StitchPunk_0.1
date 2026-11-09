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
    // Long enough that the front probe of a pitched body still finds the ground on a steep descent.
    private const float GROUND_PROBE_ABOVE = 8f;
    private const float GROUND_PROBE_BELOW = 14f;
    // A miss inside the window above used to keep the old height for good: a caravan that outran tile streaming (4x time
    // when zoomed out) was left flying over valleys or buried in hills. This long ray finds the ground again.
    private const float REGROUND_PROBE_METRES = 1000f;
    // The body only swings round after the heading while it is actually rolling.
    private const float ROLLING_SPEED = 0.03f;
    // Heading this far ahead of the body puts the front axle at full lock.
    private const float FULL_LOCK_DEGREES = 30f;
    private const float SeatedSizeHoldSeconds = 0.25f;
    private const float MAX_PITCH_RADIANS = 0.61f;
    private const float MAX_ROLL_RADIANS = 0.45f;
    // Per second; keeps the nose from snapping over triangle edges of the ground mesh.
    private const float PITCH_FOLLOW_RATE = 10f;
    private const int WHEEL_FRONT_LEFT = 0;
    private const int WHEEL_FRONT_RIGHT = 1;
    private const int WHEEL_REAR_LEFT = 2;
    private const int WHEEL_REAR_RIGHT = 3;

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
        // Rocks and other dropped items are rolled straight through, never treated as walls.
        ComponentLookup<Item> itemLookup = SystemAPI.GetComponentLookup<Item>(true);
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
            // A held stick still swings the body at the idle rate, or a caravan stopped dead by an obstacle (speed
            // reset to ~0 every frame) could never turn away from it.
            bool stickHeld = math.lengthsq(stick) > 0.0001f;
            if (forwardSpeed > ROLLING_SPEED || stickHeld)
            {
                float turnRateFactor = forwardSpeed > ROLLING_SPEED ? 1f : profile.idleTurnSpeedFactor;
                float maximumBodyTurn = profile.turnSpeed * turnRateFactor * deltaTime;
                newBodyYawDegrees += math.clamp(CaravanDriveMath.DeltaAngle(bodyYawDegrees, headingDegrees),
                    -maximumBodyTurn, maximumBodyTurn);
            }
            // Yaw only: the wall cast and travel stay flat; the slope tilt is added to the written pose below.
            quaternion yawRotation = quaternion.RotateY(math.radians(newBodyYawDegrees));
            float3 position = caravanTransform.ValueRO.Position;
            float3 positionBeforeStep = position;

            float stepLength = forwardSpeed * deltaTime;
            if (stepLength > 0.00001f)
            {
                // On a rising hill the fixed-height bumper sphere ends up buried in the slope and reads it as a wall.
                float3 bumperFootprint = position + math.rotate(yawRotation,
                    new float3(0f, 0f, profile.frontBumperLocalZ - profile.halfWidth));
                float groundHeightAtBumper = math.max(position.y,
                    GroundHeightUnder(collisionWorld, groundFilter, ref groundHits, bumperFootprint, caravanEntity, driverEntity));
                float allowedFraction = FrontBumperClearFraction(collisionWorld, wallFilter, ref wallHits, position, yawRotation,
                    groundHeightAtBumper - position.y, travelDirection, stepLength, ref profile, caravanEntity, driverEntity, in itemLookup);
                if (allowedFraction < 1f)
                {
                    // Walls stop a cart dead; there is no slide along them.
                    stepLength *= math.max(0f, allowedFraction - WALL_STOP_GAP / stepLength);
                    forwardSpeed = 0f;
                }
                position += travelDirection * stepLength;
            }

            float3 bodyForward = CaravanDriveMath.HeadingDirection(newBodyYawDegrees);

            // Tilt to the plane through the ground under the four wheels, then lift the body until no wheel is below the
            // ground. Probing at the bumpers (+7/-3 m, drawbar included) let wheels sink up to 0.74 m on humps between them;
            // now on twisted ground two diagonal wheels touch and the other two ride a little high, like a rigid cart.
            // Lifting with the smoothed tilt keeps the wheels out of the ground while the tilt is still catching up.
            // A missed probe (a steep drop, a hole) holds the last tilt instead of snapping flat.
            float3 bodyRight = new float3(bodyForward.z, 0f, -bodyForward.x);
            float4 wheelGroundHeights = float4.zero;
            bool4 wheelGroundFound = false;
            for (int wheelIndex = 0; wheelIndex < 4; wheelIndex++)
            {
                float3 contactLocal = WheelContactLocal(ref profile, wheelIndex);
                float3 probePosition = position + bodyRight * contactLocal.x + bodyForward * contactLocal.z;
                wheelGroundFound[wheelIndex] = TryGroundHeightUnder(collisionWorld, groundFilter, ref groundHits,
                    probePosition, caravanEntity, driverEntity, out float wheelGroundHeight);
                wheelGroundHeights[wheelIndex] = wheelGroundHeight;
            }

            // No ground under any wheel even on the long ray: the tile ahead has not streamed in yet. Wait at the edge
            // instead of rolling on at the old height.
            if (!math.any(wheelGroundFound) && math.any(position.xz != positionBeforeStep.xz))
            {
                position = positionBeforeStep;
                forwardSpeed = 0f;
            }

            float pitchRadians = driveState.ValueRO.pitchRadians;
            float rollRadians = driveState.ValueRO.rollRadians;
            float followBlend = 1f - math.exp(-PITCH_FOLLOW_RATE * deltaTime);
            if (math.all(wheelGroundFound))
            {
                float axleSpacingMetres = math.max(profile.wheelFrontAxleLocalZ - profile.wheelRearAxleLocalZ, 0.01f);
                float trackMetres = math.max(profile.wheelHalfTrack * 2f, 0.01f);
                float frontAxleGroundHeight = 0.5f * (wheelGroundHeights[WHEEL_FRONT_LEFT] + wheelGroundHeights[WHEEL_FRONT_RIGHT]);
                float rearAxleGroundHeight = 0.5f * (wheelGroundHeights[WHEEL_REAR_LEFT] + wheelGroundHeights[WHEEL_REAR_RIGHT]);
                float leftSideGroundHeight = 0.5f * (wheelGroundHeights[WHEEL_FRONT_LEFT] + wheelGroundHeights[WHEEL_REAR_LEFT]);
                float rightSideGroundHeight = 0.5f * (wheelGroundHeights[WHEEL_FRONT_RIGHT] + wheelGroundHeights[WHEEL_REAR_RIGHT]);
                float slopeRadians = math.clamp(math.atan2(frontAxleGroundHeight - rearAxleGroundHeight, axleSpacingMetres),
                    -MAX_PITCH_RADIANS, MAX_PITCH_RADIANS);
                float camberRadians = math.clamp(math.atan2(rightSideGroundHeight - leftSideGroundHeight, trackMetres),
                    -MAX_ROLL_RADIANS, MAX_ROLL_RADIANS);
                pitchRadians = math.lerp(pitchRadians, slopeRadians, followBlend);
                rollRadians = math.lerp(rollRadians, camberRadians, followBlend);
            }
            // Positive X rotation tips the nose down (negated for nose-up); positive Z rotation lifts the right side.
            quaternion rotation = math.mul(yawRotation, math.mul(quaternion.RotateX(-pitchRadians), quaternion.RotateZ(rollRadians)));

            if (math.any(wheelGroundFound))
            {
                float restingHeight = float.MinValue;
                for (int wheelIndex = 0; wheelIndex < 4; wheelIndex++)
                {
                    if (!wheelGroundFound[wheelIndex]) continue;
                    float contactHeightAboveOrigin = math.rotate(rotation, WheelContactLocal(ref profile, wheelIndex)).y;
                    restingHeight = math.max(restingHeight, wheelGroundHeights[wheelIndex] - contactHeightAboveOrigin);
                }
                position.y = restingHeight;
            }

            // The facing the body shows the camera picks the seat and puller offsets, quantised the way units are.
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
            driveState.ValueRW.pitchRadians = pitchRadians;
            driveState.ValueRW.rollRadians = rollRadians;
            driveState.ValueRW.pullerGait = profile.Gait(forwardSpeed);
            driveState.ValueRW.wheelSpinRadians = CaravanDriveMath.AdvanceWheelSpin(
                driveState.ValueRO.wheelSpinRadians, forwardSpeed, profile.wheelRadius, deltaTime);

            seatedDriver = driverEntity;
            // The zoomed-out view grows the caravan about its pivot, so the seat has to move out with it.
            float seatVisualScale = SystemAPI.TryGetSingleton(out StrategicSizeScales sizeScales) ? sizeScales.vehicleScale : 1f;
            seatPosition = position + math.rotate(rotation, profile.DriverSeatOffset(facing) * seatVisualScale);
            seatRotation = yawRotation;
        }

        // Written after the loop: the query above holds LocalTransform read-write.
        if (seatedDriver != Entity.Null && SystemAPI.HasComponent<LocalTransform>(seatedDriver))
        {
            LocalTransform driverTransform = SystemAPI.GetComponent<LocalTransform>(seatedDriver);
            driverTransform.Position = seatPosition;
            driverTransform.Rotation = seatRotation;
            SystemAPI.SetComponent(seatedDriver, driverTransform);
            if (SystemAPI.HasComponent<StrategicSizeTag>(seatedDriver))
            {
                RefRW<StrategicSizeTag> driverSizeTag = SystemAPI.GetComponentRW<StrategicSizeTag>(seatedDriver);
                driverSizeTag.ValueRW.seatedUntilSeconds = SystemAPI.Time.ElapsedTime + SeatedSizeHoldSeconds;
            }
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
        float groundRiseAtBumper,
        float3 travelDirection,
        float stepLength,
        ref VehicleProfileBlob profile,
        Entity caravanEntity,
        Entity driverEntity,
        in ComponentLookup<Item> itemLookup)
    {
        float bumperRadius = profile.halfWidth;
        float3 bumperCentre = position + math.rotate(rotation,
            new float3(0f, groundRiseAtBumper + profile.bumperGroundClearance + bumperRadius, profile.frontBumperLocalZ - bumperRadius));

        wallHits.Clear();
        collisionWorld.SphereCastAll(bumperCentre, bumperRadius, travelDirection, stepLength, ref wallHits,
            wallFilter, QueryInteraction.IgnoreTriggers);

        float allowedFraction = 1f;
        for (int hitIndex = 0; hitIndex < wallHits.Length; hitIndex++)
        {
            ColliderCastHit wallHit = wallHits[hitIndex];
            if (wallHit.Entity == caravanEntity || wallHit.Entity == driverEntity) continue;
            if (itemLookup.HasComponent(wallHit.Entity)) continue;
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
        TryGroundHeightUnder(collisionWorld, groundFilter, ref groundHits, position, caravanEntity, driverEntity, out float groundHeight);
        return groundHeight;
    }

    // Body-local ground contact under the wheel (y = 0 is the body origin, where the wheels touch on flat ground).
    private static float3 WheelContactLocal(ref VehicleProfileBlob profile, int wheelIndex)
    {
        bool isRightWheel = wheelIndex == WHEEL_FRONT_RIGHT || wheelIndex == WHEEL_REAR_RIGHT;
        bool isFrontWheel = wheelIndex == WHEEL_FRONT_LEFT || wheelIndex == WHEEL_FRONT_RIGHT;
        return new float3(isRightWheel ? profile.wheelHalfTrack : -profile.wheelHalfTrack, 0f,
            isFrontWheel ? profile.wheelFrontAxleLocalZ : profile.wheelRearAxleLocalZ);
    }

    // groundHeight is position.y when nothing was hit.
    private static bool TryGroundHeightUnder(
        in CollisionWorld collisionWorld,
        CollisionFilter groundFilter,
        ref NativeList<Unity.Physics.RaycastHit> groundHits,
        float3 position,
        Entity caravanEntity,
        Entity driverEntity,
        out float groundHeight)
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
        if (!HasGroundHit(ref groundHits, caravanEntity, driverEntity))
        {
            groundRay.Start = position + up * REGROUND_PROBE_METRES;
            groundRay.End = position - up * REGROUND_PROBE_METRES;
            groundHits.Clear();
            collisionWorld.CastRay(groundRay, ref groundHits);
        }

        float bestFraction = 2f;
        groundHeight = position.y;
        bool foundGround = false;
        for (int hitIndex = 0; hitIndex < groundHits.Length; hitIndex++)
        {
            Unity.Physics.RaycastHit groundHit = groundHits[hitIndex];
            if (groundHit.Entity == caravanEntity || groundHit.Entity == driverEntity) continue;
            if (groundHit.Fraction >= bestFraction) continue;
            bestFraction = groundHit.Fraction;
            groundHeight = groundHit.Position.y;
            foundGround = true;
        }
        return foundGround;
    }

    private static bool HasGroundHit(ref NativeList<Unity.Physics.RaycastHit> groundHits, Entity caravanEntity, Entity driverEntity)
    {
        for (int hitIndex = 0; hitIndex < groundHits.Length; hitIndex++)
        {
            Entity hitEntity = groundHits[hitIndex].Entity;
            if (hitEntity != caravanEntity && hitEntity != driverEntity) return true;
        }
        return false;
    }
}
