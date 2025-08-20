using UnityEngine;

public abstract class InputProviderBase : MonoBehaviour, IInputProvider
{
    // ON–FOOT defaults
    public virtual Vector2 MoveInput     => Vector2.zero;
    public virtual bool    ActionFired   => false;
    public virtual bool    InteractFired => false;

    // IN–VEHICLE defaults
    public virtual Vector2 SteerInput        => Vector2.zero;
    public virtual bool    ExitVehicleFired  => false;
}
