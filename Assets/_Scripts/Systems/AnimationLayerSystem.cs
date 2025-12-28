using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

// =====================================
// ANIMATION LAYER SYSTEM (for overlays like facing direction)
// =====================================

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AnimationSamplingSystem))]
[UpdateBefore(typeof(ApplyAnimatedPoseSystem))]
public partial struct AnimationLayerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationLibrary>();
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var library = SystemAPI.GetSingleton<AnimationLibrary>().library;
        float dt = SystemAPI.Time.DeltaTime;
        
        var animatedPoseLookup = SystemAPI.GetComponentLookup<PartAnimatedPose>(false);
        var bodyPartLookup = SystemAPI.GetComponentLookup<BodyPartTag>(true);
        
        new ApplyAnimationLayerJob
        {
            library = library,
            deltaTime = dt,
            animatedPoseLookup = animatedPoseLookup,
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ApplyAnimationLayerJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<AnimationLibraryBlob> library;
    public float deltaTime;
    [NativeDisableParallelForRestriction] public ComponentLookup<PartAnimatedPose> animatedPoseLookup;
    
    public void Execute(
        ref CharacterAnimationLayer layer,
        in DynamicBuffer<CharacterBodyPart> parts)
    {
        if (!layer.active) return;
        if (layer.weight <= 0) return;
        
        ref AnimationClipBlob clip = ref library.Value.clips[(int)layer.animation];
        if (clip.duration <= 0) return;
        
        // Update layer time
        layer.time += deltaTime;
        if (clip.looping)
        {
            layer.time = math.fmod(layer.time, clip.duration);
        }
        
        float normalizedTime = layer.time / clip.duration;
        
        // Apply layer on top of base animation
        for (int i = 0; i < parts.Length; i++)
        {
            Entity partEntity = parts[i].entity;
            BodyPart partType = parts[i].part;
            
            if (!animatedPoseLookup.HasComponent(partEntity)) continue;
            
            PartAnimatedPose currentPose = animatedPoseLookup[partEntity];
            
            // Find and apply track for this part
            for (int t = 0; t < clip.partTracks.Length; t++)
            {
                ref PartTrackBlob track = ref clip.partTracks[t];
                if (track.bodyPart != partType) continue;
                
                // Sample and apply with layer weight
                // ... (similar sampling logic, but multiplied by layer.weight)
                
                break;
            }
            
            animatedPoseLookup[partEntity] = currentPose;
        }
    }
}