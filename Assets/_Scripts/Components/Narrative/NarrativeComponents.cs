using Unity.Entities;

// ---------------------------------------------------------------------------
// Singleton entity components (baked by NarrativeEventAuthoring)
// Note: "Manager" suffix is reserved for MonoBehaviours in this project.
//       ECS singletons use "Tag" suffix.
// ---------------------------------------------------------------------------

/// <summary>
/// Singleton marker for the narrative event entity.
/// Use SystemAPI.GetSingletonEntity&lt;NarrativeEventTag&gt;() to locate the entity.
/// Baked by NarrativeEventAuthoring — place one per scene that uses narrative events.
/// </summary>
public struct NarrativeEventTag : IComponentData { }

/// <summary>
/// Enabled by ECS systems (proximity or dialogue bridge) when a narrative event should run.
/// NarrativeEventManager (MonoBehaviour) reads and consumes this each Update().
/// Disabled again after the MonoBehaviour reads it so it fires exactly once.
/// </summary>
public struct OnNarrativeEvent : IComponentData, IEnableableComponent
{
    /// <summary>Which narrative event to run. Maps to a constant in NarrativeIds.Events.</summary>
    public int eventId;
}

/// <summary>
/// Enabled by NarrativeEventManager while a narrative event is executing.
/// NarrativeProximitySystem checks this to prevent triggering a new event mid-sequence.
/// Disabled by NarrativeEventManager when the event finishes or is cancelled.
/// </summary>
public struct ActiveNarrativeEvent : IComponentData, IEnableableComponent { }

// ---------------------------------------------------------------------------
// Trigger entity components (baked by NarrativeEventTriggerAuthoring)
// ---------------------------------------------------------------------------

/// <summary>
/// Marks an entity as a proximity trigger for a narrative event.
/// When the player enters range, NarrativeProximitySystem enables OnNarrativeEvent
/// on the singleton entity and disables this component to prevent re-firing.
///
/// Set repeatable = true if the event should be retriggerable after it finishes.
/// NarrativeEventManager re-enables the trigger once the event is complete.
/// </summary>
public struct NarrativeTrigger : IComponentData, IEnableableComponent
{
    /// <summary>Which narrative event to fire. Maps to a constant in NarrativeIds.Events.</summary>
    public int eventId;

    /// <summary>How close (in world units) the player must be for the trigger to fire.</summary>
    public float range;

    /// <summary>
    /// When true, NarrativeEventManager re-enables this trigger after the event completes
    /// so it can fire again. When false the trigger stays disabled after the first fire.
    /// </summary>
    public bool repeatable;
}

// ---------------------------------------------------------------------------
// Addressable entity component (baked by NarrativeEntityIdAuthoring)
// ---------------------------------------------------------------------------

/// <summary>
/// Gives an NPC or waypoint a stable integer ID so NarrativeEventSO actions can
/// reference it without storing an Entity handle in the ScriptableObject.
///
/// IDs must match constants in NarrativeIds.Entities.
/// NarrativeEventManager builds a Dictionary&lt;int, Entity&gt; at startup using these.
/// </summary>
public struct NarrativeEntityId : IComponentData
{
    /// <summary>Unique entity ID. Must match a constant in NarrativeIds.Entities.</summary>
    public int id;
}
