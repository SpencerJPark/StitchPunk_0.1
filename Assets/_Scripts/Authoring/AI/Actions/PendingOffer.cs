using Unity.Entities;

public struct PendingOffer : IBufferElementData
{
    public Entity interactable;
    public InteractableType type;
    public float distance;
}