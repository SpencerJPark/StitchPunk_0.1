// Int values must be unique within each class and never reused after assignment.
public static class DialogueIds
{
    // One constant per DialogueSequenceSO; copy it into the SO's sequenceId.
    public static class Sequences
    {
    }

    // DialogueUIManager enables OnDialogueEvent with the matching ID; game systems react to it.
    public static class Events
    {
        public const int None = -1;
    }
}
