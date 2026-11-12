// Int values must be unique within their class and never reused after assignment.
public static class NarrativeIds
{
    // Must match the eventId field on each NarrativeEventSO asset.
    public static class Events
    {
        public const int None = -1;

        public const int RendezvousTest = 1;
    }

    // Baked onto NPC and waypoint GameObjects via NarrativeEntityIdAuthoring.
    public static class Entities
    {
    }

    // NarrativeDialogueBridgeSystem enables OnNarrativeEvent when OnDialogueEvent fires an id listed here.
    public static class DialogueBridge
    {
        public static readonly (int dialogueEventId, int narrativeEventId)[] Pairs =
        {
        };
    }
}
