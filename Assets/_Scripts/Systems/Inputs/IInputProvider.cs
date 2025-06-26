// IInputProvider.cs
using UnityEngine;

public class InputProviderBase : MonoBehaviour
{
    // ON–FOOT
    public virtual Vector2 MoveInput       => Vector2.zero;
    public virtual bool    ActionFired     => false;
    public virtual bool    InteractFired   => false;

    // IN–VEHICLE
    public virtual float   SteerInput      => 0f;
    
}
