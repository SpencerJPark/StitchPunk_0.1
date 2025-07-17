using UnityEngine;

public interface IInputProvider
{
    // ON–FOOT
    Vector2 MoveInput      { get; }
    bool    ActionFired    { get; }
    bool    InteractFired  { get; }

    // IN–VEHICLE
    Vector2 SteerInput         { get; }
    bool    ExitVehicleFired   { get; }
}

