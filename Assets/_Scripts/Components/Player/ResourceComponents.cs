using Unity.Entities;

// Player ledger (CampLoop_System.md §3, PlayerResource_System.md §4). One row per ResourceType,
// baked at zero and never pruned, so row order is stable.
public struct ResourceStack : IBufferElementData
{
    public ResourceType resourceType;
    public int amount;
}

// On the Player: which entity holds the ledger buffers. Entity.Null = the player holds them; the caravan
// takes them over when it links (Caravan_System.md §5). Resolve it with ResourceLedger.HolderOf.
public struct ResourceLedgerLink : IComponentData
{
    public Entity ledgerHolder;
}

// Missing row = uncapped.
public struct StorageCapacity : IBufferElementData
{
    public ResourceType resourceType;
    public int cap;
}

// The only mutation path into ResourceStack. ResourceChangeSystem applies and clears it.
public struct ResourceChangeRequest : IBufferElementData
{
    public ResourceType resourceType;
    public ResourceChangeType changeType;   // Add: += value; Set: = value. Both clamp to [0, cap].
    public int value;
}

// Stamped by any failed spend. Never cleared: the HUD compares stampedTime to the last one it showed.
public struct ResourceShortfall : IComponentData, IEnableableComponent
{
    public ResourceType resourceType;
    public int missing;
    public double stampedTime;
}

// Blittable mirror of ResourceStack for IPersist (buffers are not saveable).
public struct PlayerResourcesSnapshot : IComponentData, IPersist
{
    public int wood;
    public int scrapMetal;
    public int corpse;
    public int electricity;
    public int currency;
    public int revivalRound;
}
