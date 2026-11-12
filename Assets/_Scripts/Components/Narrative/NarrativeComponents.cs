using Unity.Entities;

public struct NarrativeEventTag : IComponentData { }

// Enabled by proximity or dialogue systems; NarrativeEventManager consumes it each Update and disables it so it fires once.
public struct OnNarrativeEvent : IComponentData, IEnableableComponent
{
    public int eventId;
}

// Set by NarrativeEventManager while an event runs; stops NarrativeProximitySystem starting another.
public struct ActiveNarrativeEvent : IComponentData, IEnableableComponent { }

// Proximity trigger; disables itself on fire. repeatable makes NarrativeEventManager re-enable it after the event.
public struct NarrativeTrigger : IComponentData, IEnableableComponent
{
    public int eventId;

    // World units.
    public float range;

    public bool repeatable;
}

// Enabled while an event with blockPlayerInput runs; input and action-selection systems skip actors while set.
// Baked disabled on the NarrativeEventTag entity.
public struct CutsceneActiveTag : IComponentData, IEnableableComponent { }

// Stable id so NarrativeEventSO can reference an entity without an Entity handle; must match NarrativeIds.Entities.
public struct NarrativeEntityId : IComponentData
{
    public int id;
}
