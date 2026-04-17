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
    public BehaviourType behaviourType;

    public SpatialInteractionKey(int2 cell, BehaviourType behaviourType)
    {
        this.cell = cell;
        this.behaviourType = behaviourType;
    }

    public bool Equals(SpatialInteractionKey other)
    {
        return cell.Equals(other.cell) && behaviourType == other.behaviourType;
    }

    public override int GetHashCode()
    {
        return cell.GetHashCode() ^ ((int)behaviourType * 397);
    }
}