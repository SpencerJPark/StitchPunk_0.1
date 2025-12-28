// =====================================
// EDITOR ANIMATION TIME SYSTEM (Fixed)
// =====================================

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Handles animation time in the editor scene with full preview controls.
/// Only runs when AnimationEditorActive exists.
/// 
/// Key difference from runtime system:
/// - When paused, still applies normalizedTime to allow scrubbing
/// - When playing, advances time and writes back to normalizedTime for UI sync
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct EditorAnimationTimeSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationLibrary>();
        state.RequireForUpdate<AnimationEditorActive>();
        state.RequireForUpdate<EditorAnimationTimeControl>();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        var library = SystemAPI.GetSingleton<AnimationLibrary>().library;
        var timeControl = SystemAPI.GetSingleton<EditorAnimationTimeControl>();
        float dt = SystemAPI.Time.DeltaTime;
        
        // Track if we need to write back normalized time
        float newNormalizedTime = timeControl.normalizedTime;
        bool needsWriteBack = false;
        
        foreach (var anim in SystemAPI.Query<RefRW<CharacterAnimation>>())
        {
            ref AnimationClipBlob clip = ref library.Value.clips[(int)anim.ValueRO.currentAnimation];
            float duration = clip.duration;
            
            if (duration <= 0) continue;
            
            if (timeControl.isPaused)
            {
                // PAUSED: Apply normalized time directly (allows scrubbing)
                anim.ValueRW.time = timeControl.normalizedTime * duration;
            }
            else
            {
                // PLAYING: Advance time normally
                float effectiveDeltaTime = dt * timeControl.playbackSpeed * anim.ValueRO.speed;
                
                // Handle blend transition
                if (anim.ValueRO.isBlending)
                {
                    anim.ValueRW.blendWeight += dt * timeControl.playbackSpeed / anim.ValueRO.blendDuration;
                    anim.ValueRW.blendFromTime += effectiveDeltaTime;
                    
                    if (anim.ValueRO.blendWeight >= 1f)
                    {
                        anim.ValueRW.blendWeight = 1f;
                        anim.ValueRW.isBlending = false;
                    }
                }
                
                // Update time
                anim.ValueRW.time += effectiveDeltaTime;
                
                // Handle looping
                if (anim.ValueRO.time >= duration)
                {
                    if (clip.looping || timeControl.forceLoop)
                    {
                        anim.ValueRW.time = math.fmod(anim.ValueRO.time, duration);
                    }
                    else
                    {
                        anim.ValueRW.time = duration;
                    }
                }
                
                // Calculate normalized time to write back for UI
                newNormalizedTime = anim.ValueRO.time / duration;
                needsWriteBack = true;
            }
            
            // Handle animation change request
            if (anim.ValueRO.requestedAnimation != AnimationType.None && 
                anim.ValueRO.requestedAnimation != anim.ValueRO.currentAnimation)
            {
                ref AnimationClipBlob currentClip = ref library.Value.clips[(int)anim.ValueRO.currentAnimation];
                ref AnimationClipBlob nextClip = ref library.Value.clips[(int)anim.ValueRO.requestedAnimation];
                
                if (currentClip.allowBlendOut && nextClip.allowBlendIn && anim.ValueRO.blendDuration > 0)
                {
                    anim.ValueRW.blendFromAnimation = anim.ValueRO.currentAnimation;
                    anim.ValueRW.blendFromTime = anim.ValueRO.time;
                    anim.ValueRW.blendWeight = 0f;
                    anim.ValueRW.isBlending = true;
                }
                
                anim.ValueRW.currentAnimation = anim.ValueRO.requestedAnimation;
                anim.ValueRW.time = 0f;
                anim.ValueRW.requestedAnimation = AnimationType.None;
                
                newNormalizedTime = 0f;
                needsWriteBack = true;
            }
        }
        
        // Write back normalized time so preview controller UI stays in sync
        if (needsWriteBack)
        {
            var timeControlRW = SystemAPI.GetSingletonRW<EditorAnimationTimeControl>();
            timeControlRW.ValueRW.normalizedTime = newNormalizedTime;
        }
    }
}