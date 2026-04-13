/// <summary>
/// Central registry of all dialogue IDs used across the project.
///
/// Add a constant to Sequences whenever you create a new DialogueSequenceSO asset.
/// Add a constant to Events whenever a dialogue Event node needs to trigger a new in-game effect.
///
/// All int values must be unique within their class and never reused after assignment.
/// </summary>
public static class DialogueIds
{
    /// <summary>
    /// Sequence IDs — match the sequenceId field on each DialogueSequenceSO asset.
    /// Add one constant per SO you create. Assign it to the SO's sequenceId field in the inspector.
    /// </summary>
    public static class Sequences
    {
        // Add one constant per DialogueSequenceSO asset you create. Example:
        // public const int ProfessorIntroduction = 1;
        // public const int LunchRoomGreeting     = 2;
    }

    /// <summary>
    /// Event IDs — fired by Event nodes in the dialogue graph, consumed by game systems.
    ///
    /// When an Event node's eventId matches one of these constants, the DialogueUIManager
    /// enables OnDialogueEvent on the manager entity with that ID. Downstream ECS systems
    /// (e.g. a camera system, an unlock system) check IsComponentEnabled&lt;OnDialogueEvent&gt;
    /// and react accordingly.
    ///
    /// Use -1 (or leave eventId at its default) to pass through an Event node silently.
    /// </summary>
    public static class Events
    {
        public const int None = -1;

        // Add one constant per in-game effect a dialogue Event node can trigger. Example:
        // public const int UnlockCarpenterBeetle = 1;
        // public const int SwitchToOverheadCamera = 2;
        // public const int OpenFactoryDoor        = 3;
    }
}
