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
    public float value; // 0 = not interactable, 1 = interactable
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
    public BehaviourType behaviourType;
    public float multiplier; // 1.0 = neutral, 1.2 = 20% boost, etc.
}

// On the unit (occupant). Shared data carrier for whichever kind-specific task is active.
// Written by InteractionAssignmentSystem when the unit wins a slot; read by the per-kind
// execution system to know which interaction entity it's working on and which behaviour
// motivated the pick (so the completion step can restore the right behaviour value).
public struct CurrentInteraction : IComponentData
{
    public Entity interaction;
    public BehaviourType drivingBehaviour;
}

// Per-kind task components on the unit. Enableable so each small execution system can
// query [WithAll(typeof(KindTask))] and only iterate units actively performing that kind.
// Kept separate (rather than one enum flag) so each system's query is a clean archetype
// filter at the ECS level — matches the codebase pattern (NeedsAction, ProductionProgress).
public struct PickupTask       : IComponentData, IEnableableComponent { }
public struct BuildTask        : IComponentData, IEnableableComponent { }
public struct HarvestTask       : IComponentData, IEnableableComponent { }
public struct DrinkTask        : IComponentData, IEnableableComponent { }
public struct EatTask          : IComponentData, IEnableableComponent { }
public struct TouchAnimateTask : IComponentData, IEnableableComponent { }
public struct WanderAreaTask   : IComponentData, IEnableableComponent { }
public struct SitTask          : IComponentData, IEnableableComponent { }
public struct RepairTask       : IComponentData, IEnableableComponent { }
