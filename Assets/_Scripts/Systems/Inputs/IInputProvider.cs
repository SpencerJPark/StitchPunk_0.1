using UnityEngine;

public abstract class IInputProvider : MonoBehaviour
{
    public abstract Vector2 MoveInput { get; }
    public abstract bool ActionFired { get; }
}
