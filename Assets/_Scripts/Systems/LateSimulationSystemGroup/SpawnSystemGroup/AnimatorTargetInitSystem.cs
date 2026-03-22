using Unity.Collections;
using Unity.Entities;

// Rebuilds the AnimatorTarget buffer on newly instantiated unit entities.
//
// Entity refs inside DynamicBuffer<AnimatorTarget> are NOT reliably remapped by
// ECB.Instantiate, so the buffer is cleared and rebuilt from BaseParent lookups
// (BaseParent is IComponentData and IS remapped correctly by ECB.Instantiate).
[UpdateInGroup(typeof(SpawnSystemGroup))]
[UpdateAfter(typeof(UnitSpawnerSystem))]
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
            SystemAPI.Query<RefRO<NeedsAnimatorInit>>().WithEntityAccess())
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

        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var rootsArray = roots.ToNativeArray(Allocator.Temp);
        for (int i = 0; i < rootsArray.Length; i++)
            ecb.RemoveComponent<NeedsAnimatorInit>(rootsArray[i]);

        rootsArray.Dispose();
        roots.Dispose();
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
