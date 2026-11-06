using Unity.Entities;

public static class DecisionHistoryRing
{
    public const int Capacity = 20;

    public static void Append(ref DynamicBuffer<DecisionHistoryEntry> history, DecisionHistoryEntry entry)
    {
        while (history.Length >= Capacity)
        {
            history.RemoveAt(0);
        }
        history.Add(entry);
    }
}
