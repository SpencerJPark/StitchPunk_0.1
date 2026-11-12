using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

// Groups run one at a time (next starts when every action in the current one completes); actions inside a group run in parallel.
[CreateAssetMenu(fileName = "NarrativeEvent_New", menuName = "Dots Animation/Narrative Event")]
public class NarrativeEventSO : ScriptableObject
{
    [Tooltip("Unique integer ID. Must match a constant in NarrativeIds.Events. " +
             "Never reuse or change this value once the SO has been referenced.")]
    public int eventId;

    [Tooltip("When true, player input is suppressed for the duration of this event. " +
             "Use for cutscenes where the player should not be able to issue commands.")]
    public bool blockPlayerInput;

    [Tooltip("Ordered list of action groups. Groups run one at a time. " +
             "All actions within a group run simultaneously.")]
    public List<NarrativeActionGroup> groups = new List<NarrativeActionGroup>();
}

[Serializable]
public class NarrativeActionGroup
{
    [Tooltip("Label for this step — for authoring clarity only. " +
             "Example: 'Walk to door', 'Open door', 'Dialogue gate'.")]
    public string groupName = "";

    [Tooltip("All actions that fire simultaneously for this step of the sequence.")]
    [SerializeReference]
    public List<NarrativeActionBase> actions = new List<NarrativeActionBase>();
}

[Serializable]
public abstract class NarrativeActionBase
{
    [Tooltip("NarrativeIds.Entities constant for the entity this action targets. " +
             "Set -1 if the action does not target a specific entity.")]
    public int targetEntityId = -1;
}

// The group waits until the dialogue finishes.
[Serializable]
public class DialogueTriggerAction : NarrativeActionBase
{
    [Tooltip("The dialogue sequence to play on the target NPC. " +
             "The NPC must have a DialogueProvider component (added by DialogueProviderAuthoring). " +
             "This action temporarily overrides the provider's sequenceId with this sequence.")]
    public DialogueSequenceSO sequence;
}

// The group waits until the NPC's Movement.isMoving becomes false.
[Serializable]
public class MoveNPCAction : NarrativeActionBase
{
    [Tooltip("NarrativeIds.Entities constant for the waypoint entity this NPC should walk to. " +
             "The waypoint entity must have NarrativeEntityIdAuthoring with the matching ID.")]
    public int waypointEntityId = -1;
}

// duration > 0 holds the group that many seconds; 0 advances immediately.
[Serializable]
public class PlayAnimationAction : NarrativeActionBase
{
    [Tooltip("Which animation to play, picked by name (drawn as a popup in NarrativeEventSOEditor). " +
             "The toolkit resolves which layer it lives on from the actor's profile.")]
    public uint animationKey;

    [Tooltip("When true, the group waits until the animation clip finishes playing before advancing. " +
             "Works only for non-looping clips — the AnimationTimeSystem sets the layer inactive on completion. " +
             "Takes precedence over duration when both are set.")]
    public bool waitForCompletion;

    [Tooltip("How many seconds to wait before considering this action complete. " +
             "Use 0 to fire-and-forget (the group advances without waiting). " +
             "Ignored when waitForCompletion is true.")]
    public float duration;

    [Tooltip("Whether the animation should loop. Set false for one-shot actions like death or emotes.")]
    public bool looping;
}

// Completes instantly; never blocks the group.
[Serializable]
public class EnableComponentAction : NarrativeActionBase
{
    [Tooltip("Which component type to toggle on or off.")]
    public NarrativeToggleType componentType;

    [Tooltip("True to enable the component, false to disable it.")]
    public bool enable;
}

// Only units baked by UnitBakingUtil carry ZombifyRequest; corpses convert through revive instead.
[Serializable]
public class ZombifyAction : NarrativeActionBase
{
    [Tooltip("Which unit type to convert into. Leave as None to use the unit's own " +
             "UnitSO.becomesUnitType — the same mapping the revive path converts through.")]
    [SearchableEnum] public UnitType targetUnitType;

    [Tooltip("Seconds between the request and the conversion. 0 converts on the same frame.")]
    public float delaySeconds;

    [Tooltip("When true, the group waits until the conversion has actually been applied before " +
             "advancing — the honest wait when delaySeconds is set.")]
    public bool waitForConversion;
}

// targetEntityId -1 leaves the cinematic target in place. The group waits for the full hold.
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

// Runtime identity is CutsceneAsset.StableId (CutsceneApi.TryFindStage), never the asset reference, which exists only in the editor.
[Serializable]
public class PlayCutsceneAction : NarrativeActionBase
{
    [Tooltip("The cutscene to play.")]
    public CutsceneAsset cutscene;

    [Tooltip("Initial playback speed; 1 is normal.")]
    public float speed = 1f;

    [Tooltip("When true, the group waits until the cutscene completes before advancing.")]
    public bool waitForCompletion = true;

    [Tooltip("Binds a cutscene slot to a scene entity by its NarrativeEntityId, overriding the " +
             "stage's own baked binding for that slot — for actors spawned rather than staged.")]
    public List<CutsceneSlotEntityOverride> overrides = new List<CutsceneSlotEntityOverride>();
}

[Serializable]
public class CutsceneSlotEntityOverride
{
    [Tooltip("The CutsceneSlot.SlotId this entry binds.")]
    public uint slotId;

    [Tooltip("NarrativeIds.Entities constant for the entity to bind.")]
    public int narrativeEntityId = -1;
}

// Resolved to ComponentType in NarrativeEventManager; add an entry per newly toggleable component.
public enum NarrativeToggleType
{
    DialogueProvider,
    InteractionProvider,
}
