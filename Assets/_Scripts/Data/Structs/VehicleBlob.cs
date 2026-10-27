using DotsAnimationToolkit;
using Unity.Entities;
using Unity.Mathematics;

public struct VehicleLibraryBlob
{
    // Indexed by (int)VehicleType; every value has an entry (unauthored ones are zeroed).
    public BlobArray<VehicleProfileBlob> vehicles;
}

// VehicleProfileSO, baked. Field meanings are documented on the SO.
public struct VehicleProfileBlob
{
    public VehicleType vehicleType;
    public VehiclePropulsion propulsion;

    public float moveSpeed;
    public float turnSpeed;
    public float forwardAcceleration;
    public float forwardDeceleration;
    public float idleTurnSpeedFactor;
    public float turnSmoothTime;
    public float parkMaxSpeed;
    public float wheelRadius;

    public float frontBumperLocalZ;
    public float rearBumperLocalZ;
    public float halfWidth;
    public float bumperGroundClearance;

    public float minimumImpactSpeed;
    public float damagePerMetrePerSecond;
    public float stunSecondsPerMetrePerSecond;
    public float shoveDistance;
    public float shoveSeconds;
    public int selfDamagePerHit;
    public float speedLossPerHit;
    public float rehitCooldownSeconds;

    public float rideMaxSpeed;
    public float speedTiltDegrees;
    public float rideNoiseFrequency;
    public float turnTiltDegrees;
    public float maxYawRate;
    public float pitchRangeDegrees;
    public float rollRangeDegrees;

    // Length 8, indexed by (int)Direction. pullerOffsets is empty for a SelfPropelled vehicle.
    public BlobArray<float3> driverSeatOffsets;
    public BlobArray<float3> pullerOffsets;
    public float walkThreshold;
    public float runThreshold;

    public float3 DriverSeatOffset(Direction facing)
    {
        return driverSeatOffsets.Length > (int)facing ? driverSeatOffsets[(int)facing] : float3.zero;
    }

    public bool HasPuller => propulsion == VehiclePropulsion.Drawn && pullerOffsets.Length == 8;

    public VehicleGait Gait(float forwardSpeed)
    {
        float speedFraction = moveSpeed > 0f ? forwardSpeed / moveSpeed : 0f;
        if (speedFraction <= walkThreshold) return VehicleGait.Idle;
        return speedFraction <= runThreshold ? VehicleGait.Walk : VehicleGait.Run;
    }
}
