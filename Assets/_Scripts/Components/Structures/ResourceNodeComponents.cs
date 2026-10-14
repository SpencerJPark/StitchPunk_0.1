using Unity.Entities;

// A harvestable world object: tree (Wood), wreckage (ScrapMetal). Corpses are gathered without one —
// any dead non-player unit counts as a one-load Corpse node (CampLoop Phase 4, CL-D26).
public struct ResourceNode : IComponentData
{
    public ResourceType resourceType;
    public int remaining;
    public float harvestSeconds;
    public int loadPerTrip;
}
