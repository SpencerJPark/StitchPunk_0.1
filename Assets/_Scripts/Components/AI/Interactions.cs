using Unity.Entities;
using Unity.Rendering;

// Building Blocks


// Marks this interaction entity as targetable by the player's targeting system.
// Add to any interactable object/NPC the player should be able to interact with directly.
// Disable (not remove) when the entity is no longer interactable (e.g. item picked up).
public struct PlayerInteractable : IComponentData, IEnableableComponent
{
}



[MaterialProperty("_IsInteractable")]
public struct InteractableVisual : IComponentData
{
    public float value; // 0 = not interactable, 1 = interactable
}


public struct InteractionHandled : IComponentData, IEnableableComponent
{
}

// What kind of execution an interaction requires once a unit has committed to it.
// Each kind gets its own small execution system on the unit that reads the matching
// enableable task component (PickupTask, BuildTask, ...). Kind = None is handled by
// the universal InteractionExecutionSystem as a fallback.
public enum InteractionKind : byte
{
    None,
    Pickup,
    Build,
    Drink,
    Eat,
    TouchAnimate,
    WanderArea,
    Sit,
    Repair,
}

// Single discriminator baked on every interaction entity. Assignment reads this to
// decide which per-kind task component to enable on the winning occupant.
public struct InteractionKindData : IComponentData
{
    public InteractionKind kind;
}

// Buffer of (behaviour, multiplier) pairs on an interaction entity — one entry per
// BehaviourType this provider satisfies. Replaces the old per-motivation tag components
// (HungerInteraction, SocialInteraction, ...) and the global InteractionValue multiplier.
// SpatialHashSystem reads this buffer to register the provider under each relevant key;
// BehaviourScoringSystem reads it to pick the per-behaviour multiplier during scoring.
public struct BehaviourSatisfaction : IBufferElementData
{
    public MotivationType motivationType;
    public float multiplier; // 1.0 = neutral, 1.2 = 20% boost, etc.
}


