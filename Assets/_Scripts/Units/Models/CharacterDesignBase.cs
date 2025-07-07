using UnityEngine;

public abstract class CharacterDesignBase : MonoBehaviour
{
    [SerializeField] protected RiveAnimator animator;

    // Call this to apply customization. Must be implemented by subclasses.
    public abstract void ApplyCustomization();
}

