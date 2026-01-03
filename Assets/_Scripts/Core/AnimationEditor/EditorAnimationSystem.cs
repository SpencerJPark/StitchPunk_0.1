// =====================================
// EDITOR ANIMATION SYSTEM (Live SO Sampling)
// =====================================

using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ApplyAnimatedPoseSystem))]
public partial struct EditorAnimationSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationEditorActive>();
        state.RequireForUpdate<EditorAnimationTimeControl>();
    }
    
    public void OnUpdate(ref SystemState state)
    { 
        try 
        {
            Entity timeControlEntity = SystemAPI.GetSingletonEntity<EditorAnimationTimeControl>();
        
        if (!state.EntityManager.HasComponent<EditorAnimationLibraryManaged>(timeControlEntity))
        {
            Debug.LogWarning("[EditorAnim] Missing EditorAnimationLibraryManaged");
            return;
        }
        
        var libraryManaged = state.EntityManager.GetComponentObject<EditorAnimationLibraryManaged>(timeControlEntity);
        
        if (libraryManaged?.library == null)
        {
            Debug.LogWarning("[EditorAnim] Library is null");
            return;
        }
        
        var timeControl = SystemAPI.GetSingleton<EditorAnimationTimeControl>();
        float dt = SystemAPI.Time.DeltaTime;
        
        // Use the helper method from the SO instead
        AnimationClipSO clipSO = libraryManaged.library.GetClip(timeControl.currentAnimation);
        
        if (clipSO == null)
        {
            Debug.LogWarning($"[EditorAnim] No clip found for: {timeControl.currentAnimation}");
            return;
        }
        
        // Update time
        float newNormalizedTime = timeControl.normalizedTime;
        bool needsWriteBack = false;
        
        if (!timeControl.isPaused)
        {
            newNormalizedTime += (dt * timeControl.playbackSpeed) / clipSO.duration;
            
            if (newNormalizedTime >= 1f)
            {
                if (clipSO.looping || timeControl.forceLoop)
                {
                    newNormalizedTime = math.fmod(newNormalizedTime, 1f);
                }
                else
                {
                    newNormalizedTime = 1f;
                }
            }
            needsWriteBack = true;
        }
        
        // Sample and apply poses
        var animatedPoseLookup = SystemAPI.GetComponentLookup<AnimationTargetPose>(false);
        var restPoseLookup = SystemAPI.GetComponentLookup<AnimationTargetRestPose>(true);
        
        int animatorCount = 0;
        int sampledCount = 0;
        
        foreach (var (targets, animator) in SystemAPI.Query<DynamicBuffer<AnimatorTarget>, RefRO<Animator>>())
        {
            animatorCount++;
            
            for (int i = 0; i < targets.Length; i++)
            {
                Entity targetEntity = targets[i].entity;
                AnimationTarget targetType = targets[i].target;
                
                if (!animatedPoseLookup.HasComponent(targetEntity)) continue;
                if (!restPoseLookup.HasComponent(targetEntity)) continue;
                
                var restPose = restPoseLookup[targetEntity];
                
                float3 finalPosition = restPose.localPosition;
                float finalRotation = restPose.rotation;
                float2 finalScale = restPose.scale;
                int finalImageIndex = restPose.baseImageIndex;
                
                bool hadTrack = SampleClipSO(clipSO, targetType, newNormalizedTime,
                    ref finalPosition, ref finalRotation, ref finalScale, ref finalImageIndex);
                
                if (hadTrack) sampledCount++;
                
                animatedPoseLookup[targetEntity] = new AnimationTargetPose
                {
                    localPosition = finalPosition,
                    rotation = finalRotation,
                    scale = finalScale,
                    imageIndex = finalImageIndex
                };
            }
        }
        
        if (needsWriteBack)
        {
            SystemAPI.SetSingleton(new EditorAnimationTimeControl
            {
                isPaused = timeControl.isPaused,
                normalizedTime = newNormalizedTime,
                playbackSpeed = timeControl.playbackSpeed,
                forceLoop = timeControl.forceLoop,
                currentAnimation = timeControl.currentAnimation
            });
        }
    }
    catch (System.Exception e)
    {
        Debug.LogError($"[EditorAnim] Exception: {e.Message}\n{e.StackTrace}");
    }
}
    
    private bool SampleClipSO(
        AnimationClipSO clip,
        AnimationTarget target,
        float normalizedTime,
        ref float3 position,
        ref float rotation,
        ref float2 scale,
        ref int imageIndex)
    {
        if (clip.partTracks == null) return false;
        
        foreach (var track in clip.partTracks)
        {
            if (track == null) continue;
            if (track.animationTarget != target) continue;
            if (track.keyframes == null || track.keyframes.Count == 0) continue;
            
            var sampled = SampleKeyframesSO(track, normalizedTime);
            ApplyTrackToPose(track, sampled, ref position, ref rotation, ref scale, ref imageIndex);
            return true;
        }
        
        return false;
    }
    
    private AnimationClipSO.Keyframe SampleKeyframesSO(AnimationClipSO.PartTrack track, float normalizedTime)
    {
        var keyframes = track.keyframes;
        
        int prevIndex = 0;
        int nextIndex = 0;
        
        for (int i = 0; i < keyframes.Count; i++)
        {
            if (keyframes[i].normalizedTime <= normalizedTime)
            {
                prevIndex = i;
            }
            if (keyframes[i].normalizedTime >= normalizedTime)
            {
                nextIndex = i;
                break;
            }
            nextIndex = i;
        }
        
        var prev = keyframes[prevIndex];
        var next = keyframes[nextIndex];
        
        var interpMode = prev.overrideInterpolation ? prev.interpolationOverride : track.interpolation;
        if (prevIndex == nextIndex || interpMode == InterpolationMode.Step)
        {
            return prev;
        }
        
        float range = next.normalizedTime - prev.normalizedTime;
        float t = range > 0 ? (normalizedTime - prev.normalizedTime) / range : 0f;
        t = ApplyEasing(t, interpMode);
        
        return new AnimationClipSO.Keyframe
        {
            normalizedTime = normalizedTime,
            position = Vector3.Lerp(prev.position, next.position, t),
            rotation = Mathf.Lerp(prev.rotation, next.rotation, t),
            scale = Vector2.Lerp(prev.scale, next.scale, t),
            imageIndex = t < 0.5f ? prev.imageIndex : next.imageIndex,
            overrideInterpolation = prev.overrideInterpolation,
            interpolationOverride = prev.interpolationOverride
        };
    }
    
    private float ApplyEasing(float t, InterpolationMode mode)
    {
        switch (mode)
        {
            case InterpolationMode.EaseIn:
                return t * t;
            case InterpolationMode.EaseOut:
                return 1f - (1f - t) * (1f - t);
            case InterpolationMode.EaseInOut:
                return t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
            default:
                return t;
        }
    }
    
    private void ApplyTrackToPose(
        AnimationClipSO.PartTrack track,
        AnimationClipSO.Keyframe sampled,
        ref float3 position,
        ref float rotation,
        ref float2 scale,
        ref int imageIndex)
    {
        AnimatedProperties props = track.animatedProperties;
        bool isAdditive = track.blendMode == BlendMode.Additive;
        
        if ((props & AnimatedProperties.PositionX) != 0)
            position.x = isAdditive ? position.x + sampled.position.x : sampled.position.x;
        if ((props & AnimatedProperties.PositionY) != 0)
            position.y = isAdditive ? position.y + sampled.position.y : sampled.position.y;
        if ((props & AnimatedProperties.PositionZ) != 0)
            position.z = isAdditive ? position.z + sampled.position.z : sampled.position.z;
        if ((props & AnimatedProperties.Rotation) != 0)
            rotation = isAdditive ? rotation + sampled.rotation : sampled.rotation;
        if ((props & AnimatedProperties.ScaleX) != 0)
            scale.x = isAdditive ? scale.x * sampled.scale.x : sampled.scale.x;
        if ((props & AnimatedProperties.ScaleY) != 0)
            scale.y = isAdditive ? scale.y * sampled.scale.y : sampled.scale.y;
        if ((props & AnimatedProperties.ImageIndex) != 0 && sampled.imageIndex >= 0)
            imageIndex = sampled.imageIndex;
    }
}