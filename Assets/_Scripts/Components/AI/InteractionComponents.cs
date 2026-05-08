using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;

public struct InteractionProvider : IComponentData, IEnableableComponent { }
public struct Interaction : IComponentData
{
    public ActionType     actionType;
    public MotivationType motivationType;
    public int            priority;
    public float          utilityScore;       // base satisfaction points granted on completion
    public float          range;              // arrival distance for NPC to start interacting
    public uint           allowedUnitTypeMask;// 0 = any unit; bit N = UnitType N is allowed
    public int            maxOccupants;
    public int            occupantCount;
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