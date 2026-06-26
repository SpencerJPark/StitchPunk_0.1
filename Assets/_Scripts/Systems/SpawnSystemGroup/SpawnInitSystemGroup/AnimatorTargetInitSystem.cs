using Unity.Collections;
using Unity.Entities;

// Rebuilds the AnimatorTarget buffer on newly spawned unit entities.
//
// Entity refs inside DynamicBuffer<AnimatorTarget> are NOT reliably remapped by
// ECB.Instantiate, so the buffer is cleared and rebuilt from BaseParent lookups
// (BaseParent is IComponentData and IS remapped correctly by ECB.Instantiate).
//
// Filters on [WithAll<NewlySpawned>] — runs only on spawn frames.
// SpawnInitCleanupSystem handles disabling NewlySpawned at end of SpawnSystemGroup.
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
public partial struct AnimatorTargetInitSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Collect roots that need initialisation this frame.
        var roots = new NativeHashSet<Entity>(8, Allocator.Temp);
        foreach (var (_, entity) in
            SystemAPI.Query<RefRO<NewlySpawned>>().WithEntityAccess())
        {
            roots.Add(entity);
        }

        if (roots.IsEmpty)
        {
            roots.Dispose();
            return;
        }

        // Clear stale buffer contents on those roots.
        foreach (var (buffer, entity) in
            SystemAPI.Query<DynamicBuffer<AnimatorTarget>>().WithEntityAccess())
        {
            if (roots.Contains(entity))
                buffer.Clear();
        }

        // For each body-part entity whose remapped BaseParent points to one of
        // our roots, register it in the parent's AnimatorTarget buffer.
        foreach (var (partTag, baseParent, entity) in
            SystemAPI.Query<RefRO<AnimationTargetTag>, RefRO<BaseParent>>()
                .WithEntityAccess())
        {
            Entity parentEntity = baseParent.ValueRO.baseParentEntity;
            if (!roots.Contains(parentEntity))
                continue;

            SystemAPI.GetBuffer<AnimatorTarget>(parentEntity).Add(new AnimatorTarget
            {
                entity = entity,
                target = partTag.ValueRO.target
            });
        }

        roots.Dispose();
    }
}
