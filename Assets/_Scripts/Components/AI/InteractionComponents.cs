using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;

public struct Interaction : IComponentData, IEnableableComponent
{
    public ActionType      actionType;
    public int             currentOccupants;
}

public struct PlayerInteractable : IComponentData, IEnableableComponent { }

[MaterialProperty("_IsInteractable")]
public struct InteractableVisual : IComponentData
{
    public float value; // 0 = not interactable, 1 = interactable
}

// Requests
public struct ReleaseRequest : IComponentData, IEnableableComponent
{
    public Entity interactionEntity;
}