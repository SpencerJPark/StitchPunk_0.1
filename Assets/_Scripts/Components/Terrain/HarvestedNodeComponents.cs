using Unity.Entities;

public struct HarvestableNodeKey : IComponentData
{
    public uint nodeKey;
    public int fullAmount;
    public float respawnAfterGameDays;
}

public struct HarvestedNodeStump : IComponentData, IEnableableComponent { }

public struct HarvestedNodeLedger : IComponentData { }

public struct HarvestedNodeLedgerEntry : IBufferElementData
{
    public uint nodeKey;
    public double harvestedAtWorldSeconds;
}

// The cut-tree ledger rides in the save on the Player, beside WorldClockSnapshot (its times are totalWorldSeconds).
// hasData is 1 while a snapshot waits for HarvestedNodeLoadSyncSystem. About 250 entries fit; older cuts past that are dropped.
public struct HarvestedNodeLedgerSnapshot : IComponentData, IPersist
{
    public byte hasData;
    public Unity.Collections.FixedList4096Bytes<HarvestedNodeLedgerEntry> entries;
}
