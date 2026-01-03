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
        
        var animatedPoseLookup = SystemAPI.GetComponentLookup<AnimationTargetPose>(false);
        var bodyPartLookup = SystemAPI.GetComponentLookup<AnimationTargetTag>(true);
        
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
    [NativeDisableParallelForRestriction] public ComponentLookup<AnimationTargetPose> animatedPoseLookup;
    
    public void Execute(
        ref AnimatorLayer layer,
        in DynamicBuffer<AnimatorTarget> parts)
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
            AnimationTarget partType = parts[i].target;
            
            if (!animatedPoseLookup.HasComponent(partEntity)) continue;
            
            AnimationTargetPose currentPose = animatedPoseLookup[partEntity];
            
            // Find and apply track for this part
            for (int t = 0; t < clip.animationTargetTracks.Length; t++)
            {
                ref AnimationTargetTrackBlob track = ref clip.animationTargetTracks[t];
                if (track.animationTarget != partType) continue;
                
                // Sample and apply with layer weight
                // ... (similar sampling logic, but multiplied by layer.weight)
                
                break;
            }
            
            animatedPoseLookup[partEntity] = currentPose;
        }
    }
}