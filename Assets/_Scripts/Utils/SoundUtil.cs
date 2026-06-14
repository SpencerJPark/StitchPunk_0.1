using Unity.Entities;
using Unity.Mathematics;

// Spawns one-frame PlaySound signal entities (the LogUtil/LogMessage pattern). VoiceSelectionSystem
// reads them all each frame, feeds voice selection, then destroys the whole query.
public static class SoundUtil
{
    // Positional one-shot at a fixed world position.
    public static void Play(ref EntityCommandBuffer ecb, SoundType type, float3 position,
        float volumeMul = 1f, float pitchMul = 1f)
    {
        Entity entity = ecb.CreateEntity();
        ecb.AddComponent(entity, new PlaySound
        {
            type         = type,
            source       = Entity.Null,
            position     = position,
            followSource = false,
            volumeMul    = volumeMul,
            pitchMul     = pitchMul,
        });
    }

    // One-shot that follows an emitter entity (e.g. a footstep on a walking unit).
    public static void PlayOn(ref EntityCommandBuffer ecb, SoundType type, Entity source,
        float volumeMul = 1f, float pitchMul = 1f)
    {
        Entity entity = ecb.CreateEntity();
        ecb.AddComponent(entity, new PlaySound
        {
            type         = type,
            source       = source,
            followSource = true,
            volumeMul    = volumeMul,
            pitchMul     = pitchMul,
        });
    }

    // ParallelWriter overloads for use inside jobs (sortKey = entityInQueryIndex).
    public static void Play(ref EntityCommandBuffer.ParallelWriter ecb, int sortKey, SoundType type,
        float3 position, float volumeMul = 1f, float pitchMul = 1f)
    {
        Entity entity = ecb.CreateEntity(sortKey);
        ecb.AddComponent(sortKey, entity, new PlaySound
        {
            type         = type,
            source       = Entity.Null,
            position     = position,
            followSource = false,
            volumeMul    = volumeMul,
            pitchMul     = pitchMul,
        });
    }

    public static void PlayOn(ref EntityCommandBuffer.ParallelWriter ecb, int sortKey, SoundType type,
        Entity source, float volumeMul = 1f, float pitchMul = 1f)
    {
        Entity entity = ecb.CreateEntity(sortKey);
        ecb.AddComponent(sortKey, entity, new PlaySound
        {
            type         = type,
            source       = source,
            followSource = true,
            volumeMul    = volumeMul,
            pitchMul     = pitchMul,
        });
    }
}
