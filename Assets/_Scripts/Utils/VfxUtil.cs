using Unity.Entities;
using Unity.Mathematics;

// Spawns one-frame VfxRequest signal entities (the SoundUtil pattern). VfxDrainSystem copies them
// into ResolvedVfx and destroys the query; VfxManager plays them.
public static class VfxUtil
{
    public static void Play(ref EntityCommandBuffer ecb, VfxType type, float3 position, float3 direction,
        float scale = 1f)
    {
        Entity entity = ecb.CreateEntity();
        ecb.AddComponent(entity, new VfxRequest
        {
            type      = type,
            position  = position,
            direction = direction,
            scale     = scale,
        });
    }
}
