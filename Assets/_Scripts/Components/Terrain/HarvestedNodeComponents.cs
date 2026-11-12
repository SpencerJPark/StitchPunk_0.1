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
