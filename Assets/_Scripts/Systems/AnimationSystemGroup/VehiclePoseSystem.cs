using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Moves a baked vehicle's code-driven parts (Caravan_System.md CV-D21; formerly CaravanPresenter): rolling
// wheels, the steering front axle, the ride bounce and lean (the original VehicleShakerAnimator feel), the horse
// per facing, and the summon shake and broken slump when no clip is authored for them.
[BurstCompile]
[UpdateInGroup(typeof(AnimationSystemGroup))]
public partial struct VehiclePoseSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<VehicleRig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Main-thread lookup writes to the parts below wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();
        float elapsedSeconds = (float)SystemAPI.Time.ElapsedTime;
        SystemAPI.TryGetSingleton(out VehicleLibrary vehicleLibrary);

        foreach ((RefRO<VehicleRig> vehicleRig, DynamicBuffer<VehicleWheel> wheels, RefRO<CaravanDriveState> caravanDriveState,
                     RefRO<Vehicle> vehicle, RefRO<CaravanShake> caravanShake, RefRO<VehicleAnimationKeys> vehicleAnimationKeys,
                     Entity vehicleEntity)
                 in SystemAPI.Query<RefRO<VehicleRig>, DynamicBuffer<VehicleWheel>, RefRO<CaravanDriveState>, RefRO<Vehicle>,
                     RefRO<CaravanShake>, RefRO<VehicleAnimationKeys>>().WithEntityAccess())
        {
            VehicleRig rig = vehicleRig.ValueRO;
            CaravanDriveState driveState = caravanDriveState.ValueRO;

            for (int wheelIndex = 0; wheelIndex < wheels.Length; wheelIndex++)
            {
                VehicleWheel wheel = wheels[wheelIndex];
                SetLocalRotation(ref state, wheel.wheel,
                    math.mul(wheel.restRotation, quaternion.AxisAngle(math.normalizesafe(wheel.spinAxis), driveState.wheelSpinRadians)));
            }

            if (rig.frontAxle != Entity.Null)
                SetLocalRotation(ref state, rig.frontAxle, math.mul(rig.frontAxleRestRotation,
                    quaternion.AxisAngle(math.normalizesafe(rig.frontAxleYawAxis), math.radians(driveState.steerInput * rig.maxFrontAxleYawDegrees))));

            quaternion rideTilt = quaternion.identity;
            int vehicleIndex = (int)vehicle.ValueRO.vehicleType;
            bool hasProfile = vehicleLibrary.library.IsCreated && vehicleIndex > 0 && vehicleIndex < vehicleLibrary.library.Value.vehicles.Length;
            if (hasProfile)
            {
                ref VehicleProfileBlob profile = ref vehicleLibrary.library.Value.vehicles[vehicleIndex];
                rideTilt = RideTilt(in driveState, ref profile, elapsedSeconds);
                if (rig.puller != Entity.Null && profile.HasPuller && SystemAPI.HasComponent<LocalTransform>(rig.puller))
                    SystemAPI.GetComponentRW<LocalTransform>(rig.puller).ValueRW.Position = profile.pullerOffsets[(int)driveState.facing];
            }

            if (rig.ride == Entity.Null || !SystemAPI.HasComponent<LocalTransform>(rig.ride)) continue;
            VehicleAnimationKeys animationKeys = vehicleAnimationKeys.ValueRO;
            bool broken = SystemAPI.HasComponent<CaravanBroken>(vehicleEntity) && SystemAPI.IsComponentEnabled<CaravanBroken>(vehicleEntity);
            quaternion sag = broken && animationKeys.broken == 0 ? quaternion.RotateZ(math.radians(rig.brokenSagDegrees)) : quaternion.identity;

            CaravanShake shake = caravanShake.ValueRO;
            uint shakeClip = shake.kind == CaravanShakeKind.Burst ? animationKeys.shakeBurst : animationKeys.shakeBuildUp;
            float3 jitter = float3.zero;
            if (shake.remainingSeconds > 0f && shakeClip == 0)
            {
                float noiseTime = elapsedSeconds * rig.shakeFrequency;
                jitter = new float3(
                    noise.cnoise(new float2(noiseTime, 0.1f)),
                    noise.cnoise(new float2(0.7f, noiseTime)),
                    noise.cnoise(new float2(noiseTime, 3.3f))) * shake.amplitude;
            }

            RefRW<LocalTransform> ridePose = SystemAPI.GetComponentRW<LocalTransform>(rig.ride);
            ridePose.ValueRW.Position = rig.rideRestPosition + jitter;
            ridePose.ValueRW.Rotation = math.mul(math.mul(rig.rideRestRotation, math.mul(sag, rideTilt)),
                quaternion.RotateZ(math.radians(jitter.x * 40f)));
        }
    }

    private void SetLocalRotation(ref SystemState state, Entity part, quaternion rotation)
    {
        if (part != Entity.Null && SystemAPI.HasComponent<LocalTransform>(part))
            SystemAPI.GetComponentRW<LocalTransform>(part).ValueRW.Rotation = rotation;
    }

    // A bounce that grows with speed plus a lean into turns, clamped to the original pitch/roll ranges.
    private static quaternion RideTilt(in CaravanDriveState driveState, ref VehicleProfileBlob profile, float elapsedSeconds)
    {
        float speedFraction = math.saturate(driveState.forwardSpeed / math.max(profile.rideMaxSpeed, 0.0001f));
        float noiseTime = elapsedSeconds * profile.rideNoiseFrequency;
        float pitchNoise = 0.5f * noise.cnoise(new float2(noiseTime, 0f));
        float rollNoise = 0.5f * noise.cnoise(new float2(1f, noiseTime));
        float yawFraction = math.clamp(math.radians(driveState.yawRateDegrees) / math.max(profile.maxYawRate, 0.0001f), -1f, 1f);

        float pitchDegrees = math.clamp(pitchNoise * 2f * profile.speedTiltDegrees * speedFraction, -profile.pitchRangeDegrees, profile.pitchRangeDegrees);
        float rollDegrees = math.clamp(rollNoise * 2f * profile.speedTiltDegrees * speedFraction - yawFraction * profile.turnTiltDegrees,
            -profile.rollRangeDegrees, profile.rollRangeDegrees);
        return math.mul(quaternion.RotateX(math.radians(pitchDegrees)), quaternion.RotateZ(math.radians(rollDegrees)));
    }
}
