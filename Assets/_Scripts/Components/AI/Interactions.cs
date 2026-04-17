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
    public BehaviourType behaviourType;
    public float score;
}

public struct InteractionHandled : IComponentData, IEnableableComponent
{
}


// Generic multiplier component baked onto every interaction entity alongside
// its specific XxxInteraction tag. The generic MotivationScoringSystem reads
// this instead of doing type-specific lookups per motivation.
public struct InteractionValue : IComponentData
{
    public float multiplier; // 1.0 = neutral, 1.2 = 20% boost, etc.
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