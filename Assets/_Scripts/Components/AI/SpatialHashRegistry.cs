using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct SpatialHashRegistry : IComponentData
{
    // Original hash for general waypoint queries (if still needed)
    public NativeParallelMultiHashMap<int2, Entity> waypointCells;

    // New hash keyed by (cell, interactionType) for filtered queries
    public NativeParallelMultiHashMap<SpatialInteractionKey, Entity> interactionCells;

    // Loose items (EquipBy.owner == Entity.Null) keyed by cell for spatial item awareness
    public NativeParallelMultiHashMap<int2, Entity> itemCells;
}

public struct SpatialInteractionKey : System.IEquatable<SpatialInteractionKey>
{
    public int2     cell;
    public NeedType needType;

    public SpatialInteractionKey(int2 cell, NeedType needType)
    {
        this.cell     = cell;
        this.needType = needType;
    }

    public bool Equals(SpatialInteractionKey other)
    {
        return cell.Equals(other.cell) && needType == other.needType;
    }

    public override int GetHashCode()
    {
        return cell.GetHashCode() ^ ((int)needType * 397);
    }
}