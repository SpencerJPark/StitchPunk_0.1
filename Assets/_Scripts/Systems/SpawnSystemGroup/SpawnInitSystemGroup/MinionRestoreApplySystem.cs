using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Patches restored minions the frame after <c>PersistentLoadSystem</c> instantiated them.
///
/// Runs in SpawnInitSystemGroup AFTER <c>SpawnStateInitSystem</c> (which resets Minion→off,
/// Dead→off on NewlySpawned) and <c>AnimatorTargetInitSystem</c> (which rebuilds the body-part
/// buffer), so the generically-restored component data + enabled bits stick instead of being
/// clobbered. The saved record travels via <see cref="MinionRestoreQueue"/>, keyed by the
/// <c>RestorePending.recordId</c> stamped at instantiate time.
///
/// [BurstCompile] intentionally omitted — SaveSerialization is managed reflection.
/// </summary>
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
[UpdateAfter(typeof(SpawnStateInitSystem))]
[UpdateAfter(typeof(AnimatorTargetInitSystem))]
public partial struct MinionRestoreApplySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<RestorePending>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;

        EntityQuery query = new EntityQueryBuilder(Allocator.Temp).WithAll<RestorePending>().Build(ref state);
        NativeArray<Entity> restored = query.ToEntityArray(Allocator.Temp);

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
