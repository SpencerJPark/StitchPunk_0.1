using UnityEngine;

public abstract class CharacterInputBase : MonoBehaviour
{
    public abstract Vector2 MoveInput { get; }
    public abstract bool ActionPressed { get; }
}
