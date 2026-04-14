using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One scripted narrative event: an ordered list of action groups.
///
/// Groups execute sequentially — the next group does not start until every action
/// in the current group has completed. Within a group, all actions run in parallel.
///
/// Assign this to NarrativeEventManager in the inspector and set eventId to match
/// a constant in NarrativeIds.Events. Place a NarrativeEventTriggerAuthoring (for
/// proximity trigger) or wire a dialogue event via NarrativeIds.DialogueBridge to fire it.
/// </summary>
[CreateAssetMenu(fileName = "NarrativeEvent_New", menuName = "Stitch Punk/Narrative/Narrative Event")]
public class NarrativeEventSO : ScriptableObject
{
    [Tooltip("Unique integer ID. Must match a constant in NarrativeIds.Events. " +
             "Never reuse or change this value once the SO has been referenced.")]
    public int eventId;

    [Tooltip("Ordered list of action groups. Groups run one at a time. " +
             "All actions within a group run simultaneously. " +
             "Managed via the inspector — use [SerializeReference] polymorphism.")]
    [SerializeReference]
    public List<NarrativeActionGroup> groups = new List<NarrativeActionGroup>();
}

// ---------------------------------------------------------------------------
// Action group
// ---------------------------------------------------------------------------

/// <summary>
/// One step in a narrative event. All actions inside run in parallel.
/// The next group does not start until every action in this group is done.
/// </summary>
[Serializable]
public class NarrativeActionGroup
{
    [Tooltip("All actions that fire simultaneously for this step of the sequence.")]
    [SerializeReference]
    public List<NarrativeActionBase> actions = new List<NarrativeActionBase>();
}

// ---------------------------------------------------------------------------
// Action base and concrete types
// ---------------------------------------------------------------------------

/// <summary>
/// Base class for all narrative actions. Every action can optionally target an entity
/// identified by its NarrativeEntityId baked in the scene.
/// </summary>
[Serializable]
public abstract class NarrativeActionBase
{
    [Tooltip("NarrativeIds.Entities constant for the entity this action targets. " +
             "Set -1 if the action does not target a specific entity.")]
    public int targetEntityId = -1;
}

/// <summary>
/// Triggers a dialogue sequence on the target NPC.
/// The group waits until the dialogue finishes before advancing.
/// </summary>
[Serializable]
public class DialogueTriggerAction : NarrativeActionBase
{
    [Tooltip("The dialogue sequence to play on the target NPC. " +
             "The NPC must have a DialogueProvider component (added by DialogueProviderAuthoring). " +
             "This action temporarily overrides the provider's sequenceId with this sequence.")]
    public DialogueSequenceSO sequence;
}

/// <summary>
/// Moves the target NPC to a waypoint entity's world position.
/// The group waits until the NPC's Movement.isMoving becomes false before advancing.
/// </summary>
[Serializable]
public class MoveNPCAction : NarrativeActionBase
{
    [Tooltip("NarrativeIds.Entities constant for the waypoint entity this NPC should walk to. " +
             "The waypoint entity must have NarrativeEntityIdAuthoring with the matching ID.")]
    public int waypointEntityId = -1;
}

/// <summary>
/// Forces a specific animation clip on the target entity.
/// If duration is greater than zero the group waits that many seconds before advancing.
/// If duration is zero the animation starts and the group advances immediately.
/// </summary>
[Serializable]
public class PlayAnimationAction : NarrativeActionBase
{
    [Tooltip("Which animation to play.")]
    public AnimationType animationType;

    [Tooltip("Which animation layer to set. Use Override to take full control during cutscenes.")]
    public AnimationLayerType layer;

    [Tooltip("How many seconds to wait before considering this action complete. " +
             "Use 0 to fire-and-forget (the group advances without waiting).")]
    public float duration;

    [Tooltip("Whether the animation should loop. Set false for one-shot actions like death or emotes.")]
    public bool looping;
}

/// <summary>
/// Toggles an IEnableableComponent on the target entity.
/// This action completes instantly — it does not block the group from advancing.
/// </summary>
[Serializable]
public class EnableComponentAction : NarrativeActionBase
{
    [Tooltip("Which component type to toggle on or off.")]
    public NarrativeToggleType componentType;

    [Tooltip("True to enable the component, false to disable it.")]
    public bool enable;
}

/// <summary>
/// Moves the cinematic camera to look at the target entity (or leaves the cinematic target
/// in place if targetEntityId is -1), holds for holdDuration seconds, then returns to the
/// gameplay camera. The group waits for the full hold before advancing.
/// </summary>
[Serializable]
public class CinematicCameraAction : NarrativeActionBase
{
    [Tooltip("Seconds to hold the shot before returning to gameplay camera. " +
             "Use 0 to fire-and-forget (the group advances without waiting for the camera to return).")]
    public float holdDuration = 2f;

    [Tooltip("World-space offset applied on top of the target entity's position to frame the shot " +
             "(e.g. raise Y to show the face rather than the feet). Ignored when targetEntityId is -1.")]
    public Vector3 targetOffset;
}

// ---------------------------------------------------------------------------
// Toggle type enum
// ---------------------------------------------------------------------------

/// <summary>
/// Known IEnableableComponent types that narrative actions can toggle.
/// Add new entries here when a new toggleable component needs to be wired up.
/// Resolved to ComponentType at runtime in NarrativeEventManager.
/// </summary>
public enum NarrativeToggleType
{
    DialogueProvider,
    InteractionProvider,
}
