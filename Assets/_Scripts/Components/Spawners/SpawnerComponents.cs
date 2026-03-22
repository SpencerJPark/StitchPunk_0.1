using Unity.Entities;

public struct UnitSpawner : IComponentData, IEnableableComponent
{
    public UnitType unitType;
    public int spawnCount;
    public float range;
}

// Attached to every entity managed by the pool (active or dormant).
// When the entity also has the Disabled component it is sitting in the pool.
public struct PoolOwner : IComponentData
{
    public UnitType unitType;
}

// Added to a newly instantiated root entity so AnimatorTargetInitSystem rebuilds
// its AnimatorTarget buffer from live BaseParent lookups.
// Removed by AnimatorTargetInitSystem on the same frame it is processed.
public struct NeedsAnimatorInit : IComponentData { }