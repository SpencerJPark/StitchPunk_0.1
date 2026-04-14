/// <summary>
/// Central registry of all narrative IDs used across the project.
///
/// Add a constant to Events whenever you create a new NarrativeEventSO asset.
/// Add a constant to Entities whenever an NPC or waypoint needs to be addressed by narrative actions.
/// Add a pair to DialogueBridge.Pairs whenever a dialogue event should trigger a narrative event.
///
/// All int values must be unique within their class and never reused after assignment.
/// </summary>
public static class NarrativeIds
{
    /// <summary>
    /// Narrative event IDs — match the eventId field on each NarrativeEventSO asset.
    /// Add one constant per SO you create. Assign it to the SO's eventId field in the inspector.
    /// </summary>
    public static class Events
    {
        public const int None = -1;

        // Add one constant per NarrativeEventSO asset you create. Example:
        // public const int HeadmasterDeath = 1;
        // public const int LabDoorOpens    = 2;
    }

    /// <summary>
    /// Entity IDs — baked onto NPC and waypoint GameObjects via NarrativeEntityIdAuthoring.
    /// Actions in NarrativeEventSO reference entities by these IDs rather than by entity handle.
    /// Add one constant per entity that any narrative action needs to address.
    /// </summary>
    public static class Entities
    {
        // Add one constant per entity that narrative actions address. Example:
        // public const int Headmaster   = 1;
        // public const int DormWaypoint = 2;
    }

    /// <summary>
    /// Maps dialogue event IDs to narrative event IDs.
    /// When OnDialogueEvent fires with a dialogueEventId that appears in Pairs,
    /// NarrativeDialogueBridgeSystem enables OnNarrativeEvent with the paired narrativeEventId.
    ///
    /// Format: (dialogueEventId, narrativeEventId)
    /// </summary>
    public static class DialogueBridge
    {
        /// <summary>
        /// Add a tuple here for each dialogue event that should trigger a narrative event.
        /// Example:
        ///   (DialogueIds.Events.HeadmasterFall, NarrativeIds.Events.HeadmasterDeath)
        /// </summary>
        public static readonly (int dialogueEventId, int narrativeEventId)[] Pairs =
        {
            // (DialogueIds.Events.SomeDialogueEvent, NarrativeIds.Events.SomeNarrativeEvent),
        };
    }
}
