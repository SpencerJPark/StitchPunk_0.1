using Unity.Collections;
using Unity.Entities;

// Rebuilds the BodyPart buffer on newly spawned unit roots. Replaces AnimatorTargetInitSystem,
// generalized to carry partDef + flags through alongside the entity + target.
//
// Entity refs inside DynamicBuffer<BodyPart> are NOT reliably remapped by ECB.Instantiate, so the
// buffer is cleared and rebuilt from BaseParent lookups (BaseParent is IComponentData and IS remapped
// correctly by ECB.Instantiate). Filters on [WithAll<NewlySpawned>] — spawn frames only.
// SpawnInitCleanupSystem disables NewlySpawned at the end of the group.
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
public partial struct BodyPartInitSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        NativeHashSet<Entity> roots = new NativeHashSet<Entity>(8, Allocator.Temp);
        foreach (var (_, entity) in SystemAPI.Query<RefRO<NewlySpawned>>().WithEntityAccess())
            roots.Add(entity);

        if (roots.IsEmpty)
        {
            roots.Dispose();
            return;
        }

        // Clear stale buffer contents on the spawning roots.
        foreach (var (buffer, entity) in
            SystemAPI.Query<DynamicBuffer<BodyPart>>().WithEntityAccess())
        {
            if (roots.Contains(entity))
                buffer.Clear();
        }

        // For each part whose remapped BaseParent points to a spawning root, register it.
        foreach (var (info, baseParent, entity) in
            SystemAPI.Query<RefRO<BodyPartInfo>, RefRO<BaseParent>>().WithEntityAccess())
        {
            Entity rootEntity = baseParent.ValueRO.baseParentEntity;
            if (!roots.Contains(rootEntity))
                continue;

            SystemAPI.GetBuffer<BodyPart>(rootEntity).Add(new BodyPart
            {
                entity  = entity,
                target  = info.ValueRO.target,
                partDef = info.ValueRO.partDef,
                flags   = info.ValueRO.flags,
            });
        }

        roots.Dispose();
    }
}
