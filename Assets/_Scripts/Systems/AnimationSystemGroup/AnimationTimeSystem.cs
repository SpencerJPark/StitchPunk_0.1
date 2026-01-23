// =====================================
// ANIMATION TIME SYSTEM
// =====================================

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct AnimationTimeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationLibrary>();
        state.RequireForUpdate<GameSceneTag>();
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<AnimationLibraryBlob> library = SystemAPI.GetSingleton<AnimationLibrary>().library;
        float deltaTime = SystemAPI.Time.DeltaTime;
        
        new UpdateLayerTimeJob
        {
            library = library,
            deltaTime = deltaTime
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct UpdateLayerTimeJob : IJobEntity
{
    public BlobAssetReference<AnimationLibraryBlob> library;
    public float deltaTime;
    
    public void Execute(ref DynamicBuffer<AnimationLayer> layers)
    {
        for (int i = 0; i < layers.Length; i++)
        {
            var layer = layers[i];
            if (!layer.active) continue;
            
            ref AnimationClipBlob clip = ref library.Value.clips[(int)layer.animation];
            
            layer.time += deltaTime * layer.speed;
            
            if (clip.duration > 0 && layer.time >= clip.duration)
            {
                if (layer.looping)
                {
                    layer.time = math.fmod(layer.time, clip.duration);
                }
                else
                {
                    layer.time = clip.duration;
                    layer.active = false;
                }
            }
            
            layers[i] = layer;
        }
    }
}