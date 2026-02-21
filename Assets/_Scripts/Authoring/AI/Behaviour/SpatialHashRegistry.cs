using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct SpatialHashRegistry : IComponentData
{
    // Original hash for general waypoint queries (if still needed)
    public NativeParallelMultiHashMap<int2, Entity> waypointCells;

    // New hash keyed by (cell, interactionType) for filtered queries
    public NativeParallelMultiHashMap<SpatialInteractionKey, Entity> interactionCells;
}

public struct SpatialInteractionKey : System.IEquatable<SpatialInteractionKey>
{
    public int2 cell;
    public MotivationType motivationType;

    public SpatialInteractionKey(int2 cell, MotivationType motivationType)
    {
        this.cell = cell;
        this.motivationType = motivationType;
    }

    public bool Equals(SpatialInteractionKey other)
    {
        return cell.Equals(other.cell) && motivationType == other.motivationType;
    }

    public override int GetHashCode()
    {
        return cell.GetHashCode() ^ ((int)motivationType * 397);
    }
}