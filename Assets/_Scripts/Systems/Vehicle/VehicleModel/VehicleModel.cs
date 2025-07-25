using UnityEngine;

public class VehicleModel : IVehicleDataModel
{
    protected VehicleData baseData;

    public VehicleModel(VehicleData vehicleData)
    {
        this.baseData = vehicleData;

        CurrentVehicleHealth = baseData.MaxVehicleHealth;
        CurrentVoltage = baseData.MaxVoltage;
    }


    public VehicleData ImutableDate => baseData;
    public FacingOffsetProfile DriverOffset => baseData.driverOffsetProfile;
    public FacingOffsetProfile HorseOffset => baseData.horseOffsetProfile;


    public float MaxVehicleHealth => baseData.MaxVehicleHealth;
    public float CurrentVehicleHealth { get; protected set; }


    public bool ElectricYesNo => baseData.ElectricYesNo;
    public float MaxVoltage => baseData.MaxVoltage;
    public float CurrentVoltage { get; protected set; }


    // Vehicle Movement Data
    public float MoveSpeed => baseData.vehicleMovementData.moveSpeed;
    public float TurnSpeed => baseData.vehicleMovementData.turnSpeed;
    public float ForwardAcceleration => baseData.vehicleMovementData.forwardAcceleration;
    public float ForwardDeceleration => baseData.vehicleMovementData.forwardDeceleration;
    public float IdleTurnSpeedFactor => baseData.vehicleMovementData.idleTurnSpeedFactor;
    public float TurnSmoothTime => baseData.vehicleMovementData.turnSmoothTime;

    public float WalkThreshold => baseData.vehicleMovementData.walkThreshold;
    public float RunThreshold => baseData.vehicleMovementData.runThreshold;


    // HorseData
    public bool CoachmenUpgrade { get; protected set; }
    public HorseColor FurColor { get; protected set; }
    public HairColor ManeColor { get; protected set; }


    // Manipulated States
    public bool Active { get; protected set; }
    public bool ExitTriggeredThisPress { get; protected set; }
    public Direction CurrentDirection { get; protected set; }
    public Vector3 MovementVector { get; protected set; }
    public float CurrentSpeed { get; protected set; }
    public float TurnVelocity { get; protected set; }
    public Vector3 Position { get; protected set; }

    public GameObject DriverObject { get; protected set; }
    public CharacterControllerBase DriverController { get; protected set; }


    public virtual void SetActive(bool newActive)
    {
        Active = newActive;
    }

    public virtual void SetExitTriggered(bool press)
    {
        ExitTriggeredThisPress = press;
    }

    public virtual void SetMovementVector(Vector3 newVec)
    {
        MovementVector = newVec;
    }

    public virtual void SetCurrentSpeed(float newSpeed)
    {
        CurrentSpeed = newSpeed;
    }

    public virtual void SetTurnVelocity(float newVel)
    {
        TurnVelocity = newVel;
    }

    public virtual void SetDriver(GameObject newDriver)
    {
        DriverObject = newDriver;
    }

    public virtual void SetDriverController(CharacterControllerBase newController)
    {
        DriverController = newController;
    }
}