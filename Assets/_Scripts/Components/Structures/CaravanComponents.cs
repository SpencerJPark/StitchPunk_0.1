using Unity.Entities;
using Unity.Mathematics;

// The drivable caravan (Caravan_System.md). It sits on the same entity as Workstation: parking IS
// making camp, and its Health is the workstation's Health.
public struct Caravan : IComponentData
{
    public float3 driverSeatLocalOffset;
    public float3 boardPointLocalOffset;       // stand here and press E to drive; also where parking puts the player
    public float3 interiorStartLocalOffset;    // a walk-out summon appears here, inside the back doors
    public float3 tailgateEdgeLocalOffset;     // ...walks to here
    public float3 hopLandingLocalOffset;       // ...and hops down to here
    public float3 smokeBurstSpawnLocalOffset;  // a smoke-burst summon appears here, inside the cloud
    public float3 chimneyTopLocalOffset;
}

// Field names and defaults follow the original VehicleMovementData (CaravanMovementData.asset).
public struct CaravanDriveTuning : IComponentData
{
    public float moveSpeed;               // full-stick speed, metres per second
    public float turnSpeed;               // degrees per second, the cap on both the heading and the body
    public float forwardAcceleration;
    public float forwardDeceleration;
    public float idleTurnSpeedFactor;     // fraction of turnSpeed allowed at a standstill
    public float turnSmoothTime;          // SmoothDampAngle time for the heading
    public float parkMaxSpeed;
    public float wheelRadius;
    public float frontBumperLocalZ;
    public float rearBumperLocalZ;
    public float halfWidth;
    public float bumperGroundClearance;   // the wall-check sphere rides this high, so kerbs and slopes don't stop it
}

public struct CaravanDriveState : IComponentData
{
    public float forwardSpeed;            // metres per second along the heading; the caravan has no reverse
    public float headingDegrees;          // the direction it is travelling; the body turns after it
    public float headingTurnVelocity;     // SmoothDampAngle's velocity, degrees per second
    public float yawRateDegrees;          // how fast the body turned this frame, for the presenter's lean
    public float steerInput;              // -1..1, the front axle's yaw (heading ahead of the body)
    public float wheelSpinRadians;        // accumulated rolling angle, wrapped to one turn
}

// Exactly one of CaravanDriving / CaravanParked is enabled once CaravanLinkSystem has run.
public struct CaravanDriving : IComponentData, IEnableableComponent
{
    public Entity driver;
}

public struct CaravanParked : IComponentData, IEnableableComponent { }

public struct CaravanDoors : IComponentData
{
    public CaravanDoorPhase phase;
    public float phaseSeconds;
    public float openSeconds;
    public float closeSeconds;
    public float burstSeconds;
}

public struct CaravanShake : IComponentData
{
    public float remainingSeconds;
    public float amplitude;
}

// Counters the presenter compares against its last-seen value, so a one-off effect never needs an event entity.
public struct CaravanPresentationEvents : IComponentData
{
    public int smokeBurstCount;
}

public struct CaravanCrashTuning : IComponentData
{
    public float minimumImpactSpeed;
    public float damagePerMetrePerSecond;
    public float stunSecondsPerMetrePerSecond;
    public float shoveDistance;
    public float shoveSeconds;
    public int selfDamagePerHit;
    public float speedLossPerHit;
    public float rehitCooldownSeconds;
}

// One row per unit hit recently, so a unit dragged along the bumper isn't hit every frame.
public struct CaravanRecentHit : IBufferElementData
{
    public Entity unit;
    public double hitTime;
}

public struct CaravanExitTuning : IComponentData
{
    public float walkOutSeconds;
    public float hopSeconds;
    public float hopArcHeight;
    public float shakeBeforeBurstSeconds;
    public float shakeBeforeBurstAmplitude;
    public float burstShakeSeconds;
    public float burstShakeAmplitude;
    public float smokeCoverDelaySeconds;   // burst → unit appears, once the cloud hides the spot
    public float smokeLingerSeconds;
}

// Enabled while a finished summon is leaving the caravan; the summon queue holds its next build until it ends (CV-Q3).
public struct CaravanExitSequence : IComponentData, IEnableableComponent
{
    public CaravanExitStyle style;
    public UnitType unitType;
    public float elapsedSeconds;
    public bool unitReleased;
}

public struct CaravanChimneySmoking : IComponentData, IEnableableComponent { }

// Enabled while Health is 0 (CV-D13): no driving or summoning until wood repairs it. Losing the caravan is not
// losing the game; going down yourself while it is broken is (CV-D14, PlayerKnockoutSystem).
public struct CaravanBroken : IComponentData, IEnableableComponent { }

// On a summoned unit while it walks out of the back and hops down. Added at spawn, removed on landing;
// the unit only takes its rally move order once it is on the ground.
public struct CaravanExitWalker : IComponentData
{
    public float3 interiorPoint;
    public float3 tailgatePoint;
    public float3 landingPoint;
    public float walkOutSeconds;
    public float hopSeconds;
    public float hopArcHeight;
    public float elapsedSeconds;
}
