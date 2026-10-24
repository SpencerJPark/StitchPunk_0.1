using Unity.Collections;
using Unity.Entities;

// Gathers this frame's one-shot VfxRequest signals into the ResolvedVfx singleton that VfxManager reads in
// LateUpdate, then destroys them (the PlaySound -> VoiceSelectionSystem -> AudioManager lifecycle).
// The list is cleared here, not by the reader, so it can never grow in a scene without a VfxManager.
// Not [BurstCompile]: DestroyEntity on a query is a structural change.
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct VfxDrainSystem : ISystem
{
    private EntityQuery vfxRequestQuery;
    private Entity resolvedVfxSingleton;

    public void OnCreate(ref SystemState state)
    {
        vfxRequestQuery = state.GetEntityQuery(ComponentType.ReadOnly<VfxRequest>());

        resolvedVfxSingleton = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(resolvedVfxSingleton, new ResolvedVfx
        {
            requests = new NativeList<VfxRequest>(16, Allocator.Persistent),
        });
    }

    public void OnDestroy(ref SystemState state)
    {
        if (!state.EntityManager.Exists(resolvedVfxSingleton)) return;
        if (!state.EntityManager.HasComponent<ResolvedVfx>(resolvedVfxSingleton)) return;

        ResolvedVfx resolvedVfx = state.EntityManager.GetComponentData<ResolvedVfx>(resolvedVfxSingleton);
        if (resolvedVfx.requests.IsCreated)
            resolvedVfx.requests.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        ResolvedVfx resolvedVfx = state.EntityManager.GetComponentData<ResolvedVfx>(resolvedVfxSingleton);
        resolvedVfx.requests.Clear();

        if (vfxRequestQuery.IsEmpty) return;

        NativeArray<VfxRequest> pendingRequests = vfxRequestQuery.ToComponentDataArray<VfxRequest>(Allocator.Temp);
        resolvedVfx.requests.AddRange(pendingRequests);
        pendingRequests.Dispose();

        state.EntityManager.DestroyEntity(vfxRequestQuery);
    }
}
