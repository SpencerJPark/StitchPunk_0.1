using Unity.Entities;
using Unity.Rendering;

// Building Blocks
public struct InteractionProvider : IComponentData, IEnableableComponent
{
}

// Marks this interaction entity as targetable by the player's targeting system.
// Add to any interactable object/NPC the player should be able to interact with directly.
// Disable (not remove) when the entity is no longer interactable (e.g. item picked up).
public struct PlayerInteractable : IComponentData, IEnableableComponent
{
}

public struct Interaction : IComponentData
{
    public float interactionRange;
    public ActionType actionType;
    public int maxOccupants;
}

[MaterialProperty("_IsInteractable")]
public struct InteractableVisual : IComponentData
{
    public float Value; // 0 = not interactable, 1 = interactable
}

public struct InteractionTimer : IComponentData, IEnableableComponent
{
    public float maxTime;
    public float duration;
    public float elapsed;
}

public struct InteractionOccupant : IBufferElementData
{
    public Entity entity;
    public MotivationType motivationType;
    public float score;
}

public struct InteractionHandled : IComponentData, IEnableableComponent
{
}


// Interaction Types
public struct SocialInteraction : IComponentData {
    public int value;
}

public struct SafetyInteraction : IComponentData
{
    public int value;
}

public struct MovementInteraction : IComponentData
{
    public int value;
}

public struct HungerInteraction : IComponentData {
    public int value;
}

public struct FunInteraction : IComponentData
{
    public int value;
}

public struct ComfortInteraction : IComponentData
{
    public int value;
}

public struct EnergyInteraction : IComponentData {
    public int value;
}

public struct BladderInteraction : IComponentData
{
    public int value;
}