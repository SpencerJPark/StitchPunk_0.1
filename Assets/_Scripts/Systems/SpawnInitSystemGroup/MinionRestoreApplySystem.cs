using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Patches restored minions the frame after <c>PersistentLoadSystem</c> instantiated them.
///
/// Runs in SpawnInitSystemGroup AFTER <c>SpawnStateInitSystem</c> (which resets Minion→off,
/// Dead→off on NewlySpawned) and <c>BodyPartInitSystem</c> (which rebuilds the body-part
/// buffer), so the generically-restored component data + enabled bits stick instead of being
/// clobbered. The saved record travels via <see cref="MinionRestoreQueue"/>, keyed by the
/// <c>RestorePending.recordId</c> stamped at instantiate time.
///
/// [BurstCompile] intentionally omitted — SaveSerialization is managed reflection.
/// </summary>
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
[UpdateAfter(typeof(SpawnStateInitSystem))]
[UpdateAfter(typeof(BodyPartInitSystem))]
public partial struct MinionRestoreApplySystem : ISystem
{
    // Built once here rather than per-update: building a query is a lookup against every archetype
    // in the world, which is what Unity's "create queries in OnCreate" warning is about.
    private EntityQuery restorePendingQuery;

    public void OnCreate(ref SystemState state)
    {
        restorePendingQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<RestorePending>()
            .Build(ref state);

        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<RestorePending>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;

        NativeArray<Entity> restored = restorePendingQuery.ToEntityArray(Allocator.Temp);

        foreach (Entity entity in restored)
        {
            RestorePending pending = entityManager.GetComponentData<RestorePending>(entity);

            if (MinionRestoreQueue.TryConsume(pending.recordId, out EntityRecord record))
                SaveSerialization.ApplyEntity(entityManager, entity, record);

            entityManager.RemoveComponent<RestorePending>(entity);
        }

        restored.Dispose();
    }

    public void OnDestroy(ref SystemState state) { }
}
