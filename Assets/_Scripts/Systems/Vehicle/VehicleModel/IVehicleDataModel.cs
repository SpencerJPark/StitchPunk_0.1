using UnityEngine;

public interface IVehicleDataModel
{
    VehicleData ImutableDate { get; }
    FacingOffsetProfile DriverOffset { get; }
    FacingOffsetProfile HorseOffset { get; }


    float MaxVehicleHealth { get; }
    float CurrentVehicleHealth { get; }


    bool ElectricYesNo { get; }
    float MaxVoltage { get; }
    float CurrentVoltage { get; }


    // Vehicle Movement Data
    float MoveSpeed { get; }
    float TurnSpeed { get; }
    float ForwardAcceleration { get; }
    float ForwardDeceleration { get; }
    float TurnAcceleration { get; }
    float TurnDeceleration { get; }
    float IdleTurnSpeedFactor { get; }
    float TurnSmoothTime { get; }

    float WalkThreshold { get; }
    float RunThreshold { get; }


    // HorseData
    bool CoachmenUpgrade { get; }
    HorseColor FurColor { get; }
    HairColor ManeColor { get; }


    // Manipulated States
    bool Active { get; }
    bool ExitTriggeredThisPress { get; }
    Direction CurrentDirection { get; }
    Vector3 MovementVector { get; }
    float CurrentSpeed { get; }
    float TurnVelocity { get; }
    Vector3 Position { get; }

    GameObject DriverObject { get; }
    CharacterControllerBase DriverController { get; }
}