using DotsAnimationToolkit;
using UnityEngine;

// Everything about how one kind of vehicle drives, hits and rides, baked into the VehicleLibrary blob
// (Caravan_System.md CV-D18). Horse-drawn and self-propelled vehicles share one handling model and differ
// only in these numbers and in whether there is a puller.
[CreateAssetMenu(menuName = "Stitch Punk/Vehicles/Vehicle Profile")]
public class VehicleProfileSO : ScriptableObject
{
    [SearchableEnum] public VehicleType vehicleType;
    [Tooltip("Drawn: pulled by a horse (or other puller) at the Puller offsets. SelfPropelled: a car, no puller.")]
    public VehiclePropulsion propulsion = VehiclePropulsion.Drawn;

    [Header("Driving (the stick is a world direction to head toward; its tilt is the target speed)")]
    public float moveSpeed = 7f;
    [Tooltip("Degrees per second: caps both the heading and how fast the body swings round after it.")]
    public float turnSpeed = 50f;
    public float forwardAcceleration = 1.2f;
    public float forwardDeceleration = 3f;
    [Tooltip("Fraction of turnSpeed allowed at a standstill.")]
    [Range(0f, 1f)] public float idleTurnSpeedFactor = 0.4f;
    [Tooltip("SmoothDampAngle time for the heading: higher = lazier, wider turns.")]
    public float turnSmoothTime = 2.44f;
    [Tooltip("E only parks below this speed.")]
    public float parkMaxSpeed = 0.5f;
    [Tooltip("Larger spins the wheels slower.")]
    public float wheelRadius = 1.2f;

    [Header("Footprint (vehicle-local; the wall check and crash box run between the bumpers)")]
    public float frontBumperLocalZ = 7f;
    public float rearBumperLocalZ = -3f;
    public float halfWidth = 1.75f;
    [Tooltip("The wall-check sphere rides this high, so kerbs and slopes don't stop it.")]
    public float bumperGroundClearance = 0.4f;

    [Header("Crash")]
    [Tooltip("Below this speed units are only pushed aside.")]
    public float minimumImpactSpeed = 2f;
    public float damagePerMetrePerSecond = 6f;
    public float stunSecondsPerMetrePerSecond = 0.25f;
    [Tooltip("Far enough that a unit hit dead centre clears the half-width.")]
    public float shoveDistance = 2.5f;
    public float shoveSeconds = 0.25f;
    public int selfDamagePerHit = 1;
    [Tooltip("Fraction of speed lost ramming a unit whose UnitSO is marked heavy. Light units cost nothing.")]
    [Range(0f, 1f)] public float speedLossPerHeavyUnit = 0.35f;
    public float rehitCooldownSeconds = 1f;

    [Header("Taking hits")]
    [Tooltip("Thrown things (rocks) barely scratch a vehicle: their damage is multiplied by this. They never slow it.")]
    [Range(0f, 1f)] public float thrownDamageMultiplier = 0.1f;
    [Tooltip("A single hit this strong or stronger starts eating the vehicle's momentum.")]
    public int heavyHitDamageThreshold = 30;
    [Tooltip("A single hit this strong stops it dead; between the two, speed is lost in proportion.")]
    public int fullStopHitDamage = 120;

    [Header("Ride (bounce with speed, lean into turns)")]
    [Tooltip("Speed at which the bounce is at full strength.")]
    public float rideMaxSpeed = 10f;
    public float speedTiltDegrees = 5f;
    public float rideNoiseFrequency = 1f;
    public float turnTiltDegrees = 10f;
    [Tooltip("Radians per second of body turn that gives the full lean.")]
    public float maxYawRate = 2f;
    public float pitchRangeDegrees = 8f;
    public float rollRangeDegrees = 12f;

    [Header("Seats and puller, per facing direction (vehicle-local positions)")]
    public DirectionalOffsets driverSeat = DirectionalOffsets.All(new Vector3(0f, 2.42f, 2.72f));
    [Tooltip("Where the puller (horse) stands for each facing. Ignored when SelfPropelled.")]
    public DirectionalOffsets puller = DirectionalOffsets.All(new Vector3(0f, 0f, 5.98f));
    [Tooltip("Fraction of moveSpeed where the puller starts walking, and where it breaks into a run.")]
    [Range(0f, 1f)] public float walkThreshold = 0.1f;
    [Range(0f, 1f)] public float runThreshold = 0.7f;
}

// One offset per DotsAnimationToolkit.Direction (the facing the vehicle shows the camera).
[System.Serializable]
public struct DirectionalOffsets
{
    public Vector3 north;
    public Vector3 northEast;
    public Vector3 east;
    public Vector3 southEast;
    public Vector3 south;
    public Vector3 southWest;
    public Vector3 west;
    public Vector3 northWest;

    public static DirectionalOffsets All(Vector3 offset)
    {
        return new DirectionalOffsets
        {
            north = offset, northEast = offset, east = offset, southEast = offset,
            south = offset, southWest = offset, west = offset, northWest = offset,
        };
    }

    public Vector3 Get(Direction direction)
    {
        switch (direction)
        {
            case Direction.North: return north;
            case Direction.NorthEast: return northEast;
            case Direction.East: return east;
            case Direction.SouthEast: return southEast;
            case Direction.South: return south;
            case Direction.SouthWest: return southWest;
            case Direction.West: return west;
            default: return northWest;
        }
    }
}
