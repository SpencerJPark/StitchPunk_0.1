using Unity.Burst;
using Unity.Entities;

// Fires animation-locked SFX: when a clip's playback crosses a SoundMarker's normalizedTime this
// frame, emits a PlaySound (following the animating unit). Runs after AnimationTimeSystem (which
// advanced layer.time), reconstructing the previous time from deltaTime * speed and handling the
// loop wrap. Emitted PlaySound entities are collected + culled by VoiceSelectionSystem later.
[BurstCompile]
[UpdateInGroup(typeof(AnimationExecutionSystemGroup))]
[UpdateAfter(typeof(AnimationTimeSystem))]
public partial struct AnimationSoundMarkerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<AnimationLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<AnimationLibraryBlob> library = SystemAPI.GetSingleton<AnimationLibrary>().library;

        EntityCommandBuffer.ParallelWriter ecb =
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        state.Dependency = new AnimationSoundMarkerJob
        {
            library   = library,
            deltaTime = SystemAPI.Time.DeltaTime,
            ecb       = ecb,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct AnimationSoundMarkerJob : IJobEntity
{
    public BlobAssetReference<AnimationLibraryBlob> library;
    public float deltaTime;
    public EntityCommandBuffer.ParallelWriter ecb;

    public void Execute([EntityIndexInQuery] int sortKey, Entity entity, in DynamicBuffer<AnimationLayer> layers)
    {
        for (int i = 0; i < layers.Length; i++)
        {
            AnimationLayer layer = layers[i];
            if (!layer.active) continue;

            ref AnimationClipBlob clip = ref library.Value.clips[(int)layer.animation];
            if (clip.duration <= 0f || clip.soundMarkers.Length == 0) continue;

            float advance = deltaTime * layer.speed;
            if (advance <= 0f) continue;

            float duration = clip.duration;
            float newNorm  = layer.time / duration;
            float prevNorm = (layer.time - advance) / duration;

            if (layer.looping && prevNorm < 0f)
            {
                // Wrapped this frame: window is (prevNorm+1, 1] ∪ [0, newNorm].
                float wrappedPrev = prevNorm + 1f;
                for (int m = 0; m < clip.soundMarkers.Length; m++)
                {
                    float t = clip.soundMarkers[m].normalizedTime;
                    if ((t > wrappedPrev && t <= 1f) || (t >= 0f && t <= newNorm))
                        SoundUtil.PlayOn(ref ecb, sortKey, clip.soundMarkers[m].type, entity);
                }
            }
            else
            {
                for (int m = 0; m < clip.soundMarkers.Length; m++)
                {
                    float t = clip.soundMarkers[m].normalizedTime;
                    if (t > prevNorm && t <= newNorm)
                        SoundUtil.PlayOn(ref ecb, sortKey, clip.soundMarkers[m].type, entity);
                }
            }
        }
    }
}
