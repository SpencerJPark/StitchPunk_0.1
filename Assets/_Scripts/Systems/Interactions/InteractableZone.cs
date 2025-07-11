using UnityEngine;

[RequireComponent(typeof(Collider))]
public abstract class InteractableZone : MonoBehaviour, IInteractableZone
{
    [SerializeField, Tooltip("Higher = wins when overlapping multiple")] 
    private int priority = 0;
    public int Priority => priority;

    protected virtual void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        InteractionManager.Instance.RegisterZone(this);
    }

    protected virtual void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        InteractionManager.Instance.UnregisterZone(this);
    }

    public abstract void OnInteract();
}
