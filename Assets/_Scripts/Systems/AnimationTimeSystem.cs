using Unity.Burst;
using Unity.Collections;
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
        var library = SystemAPI.GetSingleton<AnimationLibrary>().library;
        float dt = SystemAPI.Time.DeltaTime;
        
        new UpdateAnimationTimeJob
        {
            library = library,
            deltaTime = dt
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct UpdateAnimationTimeJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<AnimationLibraryBlob> library;
    public float deltaTime;
    
    public void Execute(ref CharacterAnimation anim)
    {
        ref AnimationClipBlob clip = ref library.Value.clips[(int)anim.currentAnimation];
        
        // Handle blend transition
        if (anim.isBlending)
        {
            anim.blendWeight += deltaTime / anim.blendDuration;
            anim.blendFromTime += deltaTime * anim.speed;
            
            if (anim.blendWeight >= 1f)
            {
                anim.blendWeight = 1f;
                anim.isBlending = false;
            }
        }
        
        // Update current animation time
        anim.time += deltaTime * anim.speed;
        
        // Handle looping / completion
        if (clip.duration > 0 && anim.time >= clip.duration)
        {
            if (clip.looping)
            {
                anim.time = math.fmod(anim.time, clip.duration);
            }
            else
            {
                anim.time = clip.duration;
            }
        }
        
        // Handle animation change request
        if (anim.requestedAnimation != AnimationType.None && 
            anim.requestedAnimation != anim.currentAnimation)
        {
            ref AnimationClipBlob currentClip = ref library.Value.clips[(int)anim.currentAnimation];
            ref AnimationClipBlob nextClip = ref library.Value.clips[(int)anim.requestedAnimation];
            
            // Check if we should blend
            if (currentClip.allowBlendOut && nextClip.allowBlendIn && anim.blendDuration > 0)
            {
                anim.blendFromAnimation = anim.currentAnimation;
                anim.blendFromTime = anim.time;
                anim.blendWeight = 0f;
                anim.isBlending = true;
            }
            
            anim.currentAnimation = anim.requestedAnimation;
            anim.time = 0f;
            anim.requestedAnimation = AnimationType.None;
        }
    }
}


