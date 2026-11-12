using Unity.Entities;

public static class HarvestedNodeMath
{
    public static bool IsRespawnDue(double harvestedAtWorldSeconds, double nowWorldSeconds, float respawnAfterGameDays, double worldSecondsPerGameDay)
    {
        if (respawnAfterGameDays <= 0f || worldSecondsPerGameDay <= 0.0)
        {
            return false;
        }
        return nowWorldSeconds - harvestedAtWorldSeconds >= respawnAfterGameDays * worldSecondsPerGameDay;
    }

    public static int IndexOfKey(in DynamicBuffer<HarvestedNodeLedgerEntry> ledger, uint nodeKey)
    {
        for (int index = 0; index < ledger.Length; index++)
        {
            if (ledger[index].nodeKey == nodeKey)
            {
                return index;
            }
        }
        return -1;
    }
}
