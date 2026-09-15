using Unity.Entities;

// Enable to request teardown; DespawnSystem pools (PoolOwner present) or destroys, then re-disables it on pooled entities.
public struct Despawn : IComponentData, IEnableableComponent
{
    public DespawnMode mode;
}

// Ticked down by LifetimeSystem, which enables Despawn and disables this at zero.
public struct Lifetime : IComponentData, IEnableableComponent
{
    public float secondsRemaining;
}

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

// Baked disabled on all unit body prefabs by UnitAuthoring.
// Enabled by UnitSpawnerSystem whenever a body entity is freshly instantiated
// or reclaimed from the pool. Downstream init systems in SpawnSystemGroup filter
// on [WithAll<NewlySpawned>] to run only on that frame's new arrivals.
// Disabled (not removed) by SpawnInitCleanupSystem at the end of SpawnSystemGroup
// so the component persists across pool cycles.
public struct NewlySpawned : IComponentData, IEnableableComponent { }