using UnityEngine;

[RequireComponent(typeof(Collider))]
public abstract class InteractableZone : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag your PlayerManager GameObject here")]
    [SerializeField] protected PlayerManager playerManager;

    bool inZone;
    bool hasTriggeredThisPress;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        inZone = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        inZone = false;
        hasTriggeredThisPress = false;
    }

    void Update()
    {
        if (!inZone) return;

        // Pull from your IInputProvider
        var input = playerManager.InputHandler;  
        if (input.InteractPressed && !hasTriggeredThisPress)
        {
            hasTriggeredThisPress = true;
            OnInteract();
        }
        else if (!input.InteractPressed)
        {
            // reset once button is released
            hasTriggeredThisPress = false;
        }
    }

    /// <summary>Override this in subclasses for the actual behavior.</summary>
    protected abstract void OnInteract();
}
