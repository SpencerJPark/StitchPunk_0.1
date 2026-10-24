using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// One-shot particle request, spawned as a one-frame entity (the PlaySound lifecycle).
// VfxDrainSystem copies it into ResolvedVfx and destroys the query; VfxManager plays it.
public struct VfxRequest : IComponentData
{
    public VfxType type;
    public float3 position;
    public float3 direction;
    public float scale;
}

// Singleton. The list is owned by VfxDrainSystem (Persistent); VfxManager reads and clears it each LateUpdate.
public struct ResolvedVfx : IComponentData
{
    public NativeList<VfxRequest> requests;
}
