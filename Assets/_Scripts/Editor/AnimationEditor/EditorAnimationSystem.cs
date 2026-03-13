// =====================================
// EDITOR ANIMATION SYSTEM (Live SO Sampling with Layers)
// =====================================

using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct EditorAnimationSystem : ISystem
{
    private int lastSampledFrame;
    private float accumulatedTime;
    
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationEditorActive>();
        state.RequireForUpdate<EditorAnimationTimeControl>();
        
        lastSampledFrame = -1;
        accumulatedTime = 0f;
    }
    
    public void OnUpdate(ref SystemState state)
    {
        try
        {
            Entity timeControlEntity = SystemAPI.GetSingletonEntity<EditorAnimationTimeControl>();
            
            if (!state.EntityManager.HasComponent<EditorAnimationLibraryManaged>(timeControlEntity))
            {
                return;
            }
            
            var libraryManaged = state.EntityManager.GetComponentObject<EditorAnimationLibraryManaged>(timeControlEntity);
            
            if (libraryManaged?.library == null)
            {
                return;
            }
            
            var timeControl = SystemAPI.GetSingleton<EditorAnimationTimeControl>();
            float dt = SystemAPI.Time.DeltaTime;
            
            int frameRate = GlobalGameData.Instance != null ? GlobalGameData.Instance.animationFrameRate : 24;
            
            // Frame rate limiting
            accumulatedTime += dt;
            int currentFrame = (int)(accumulatedTime * frameRate);
            
            bool shouldSample = currentFrame != lastSampledFrame;
            if (shouldSample)
            {
                lastSampledFrame = currentFrame;
            }
            
            // Update layer times
            if (!timeControl.isPaused)
            {
                foreach (var (layers, entity) in SystemAPI.Query<DynamicBuffer<AnimationLayer>>().WithEntityAccess())
                {
                    var writableLayers = state.EntityManager.GetBuffer<AnimationLayer>(entity);
                    
                    for (int i = 0; i < writableLayers.Length; i++)
                    {
                        var layer = writableLayers[i];
                        if (!layer.active) continue;
                        
                        AnimationClipSO clipSO = libraryManaged.library.GetClip(layer.animation);
                        if (clipSO == null || clipSO.duration <= 0) continue;
                        
                        layer.time += dt * layer.speed * timeControl.playbackSpeed;
                        
                        if (layer.time >= clipSO.duration)
                        {
                            if (layer.looping || timeControl.forceLoop)
                            {
                                layer.time = math.fmod(layer.time, clipSO.duration);
                            }
                            else
                            {
                                layer.time = clipSO.duration;
                                layer.active = false;
                            }
                        }
                        
                        writableLayers[i] = layer;
                    }
                }
            }
            
            // Only sample on frame boundaries
            if (!shouldSample) return;
            
            // Sample and apply poses
            var animatedPoseLookup = SystemAPI.GetComponentLookup<AnimationTargetPose>(false);
            var restPoseLookup = SystemAPI.GetComponentLookup<AnimationTargetRestPose>(true);
            
            foreach (var (layers, targets) in SystemAPI.Query<DynamicBuffer<AnimationLayer>, DynamicBuffer<AnimatorTarget>>())
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    Entity targetEntity = targets[i].entity;
                    AnimationTarget partType = targets[i].target;
                    
                    if (!animatedPoseLookup.HasComponent(targetEntity)) continue;
                    if (!restPoseLookup.HasComponent(targetEntity)) continue;
                    
                    var restPose = restPoseLookup[targetEntity];
                    
                    float3 finalPosition = restPose.localPosition;
                    float finalRotation = restPose.rotation;
                    float2 finalScale = restPose.scale;
                    int finalImageIndex = restPose.baseImageIndex;
                    
                    AnimatedProperties appliedProperties = AnimatedProperties.None;
                    
                    // Process layers in reverse order (highest priority first)
                    for (int layerIdx = layers.Length - 1; layerIdx >= 0; layerIdx--)
                    {
                        var layer = layers[layerIdx];
                        if (!layer.active) continue;
                        
                        // Solo mode - only process the solo layer
                        if (timeControl.soloLayerIndex >= 0 && layerIdx != timeControl.soloLayerIndex)
                        {
                            continue;
                        }
                        
                        AnimationClipSO clipSO = libraryManaged.library.GetClip(layer.animation);
                        if (clipSO == null || clipSO.duration <= 0) continue;
                        
                        float normalizedTime = layer.time / clipSO.duration;
                        float quantizedTime = QuantizeTime(normalizedTime, clipSO.duration, frameRate);
                        
                        SampleClipSO(
                            clipSO,
                            partType,
                            quantizedTime,
                            ref finalPosition,
                            ref finalRotation,
                            ref finalScale,
                            ref finalImageIndex,
                            ref appliedProperties);
                    }
                    
                    animatedPoseLookup[targetEntity] = new AnimationTargetPose
                    {
                        localPosition = finalPosition,
                        rotation = finalRotation,
                        scale = finalScale,
                        imageIndex = finalImageIndex
                    };
                }
            }
            
            // Update normalized time for UI (based on base layer or solo layer)
            if (!timeControl.isPaused)
            {
                foreach (var layers in SystemAPI.Query<DynamicBuffer<AnimationLayer>>())
                {
                    int targetLayerIdx = timeControl.soloLayerIndex >= 0 ? timeControl.soloLayerIndex : 0;
                    
                    if (targetLayerIdx < layers.Length && layers[targetLayerIdx].active)
                    {
                        var layer = layers[targetLayerIdx];
                        AnimationClipSO clipSO = libraryManaged.library.GetClip(layer.animation);
                        
                        if (clipSO != null && clipSO.duration > 0)
                        {
                            SystemAPI.SetSingleton(new EditorAnimationTimeControl
                            {
                                isPaused = timeControl.isPaused,
                                normalizedTime = layer.time / clipSO.duration,
                                playbackSpeed = timeControl.playbackSpeed,
                                forceLoop = timeControl.forceLoop,
                                soloLayerIndex = timeControl.soloLayerIndex
                            });
                        }
                    }
                    break;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[EditorAnim] Exception: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private float QuantizeTime(float normalizedTime, float duration, int frameRate)
    {
        if (frameRate <= 0 || duration <= 0) return normalizedTime;
        
        float totalFrames = duration * frameRate;
        float currentFrame = math.floor(normalizedTime * totalFrames);
        return currentFrame / totalFrames;
    }
    
    private void SampleClipSO(
        AnimationClipSO clip,
        AnimationTarget target,
        float normalizedTime,
        ref float3 position,
        ref float rotation,
        ref float2 scale,
        ref int imageIndex,
        ref AnimatedProperties appliedProperties)
    {
        if (clip.partTracks == null) return;
        
        foreach (var track in clip.partTracks)
        {
            if (track == null) continue;
            if (track.animationTarget != target) continue;
            if (track.keyframes == null || track.keyframes.Count == 0) continue;
            
            AnimatedProperties propsToApply = track.animatedProperties & ~appliedProperties;
            if (propsToApply == AnimatedProperties.None) break;
            
            var sampled = SampleKeyframesSO(track, normalizedTime);
            ApplyTrackToPose(track, sampled, propsToApply, ref position, ref rotation, ref scale, ref imageIndex);
            
            appliedProperties |= track.animatedProperties;
            break;
        }
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
        AnimatedProperties propsToApply,
        ref float3 position,
        ref float rotation,
        ref float2 scale,
        ref int imageIndex)
    {
        bool isAdditive = track.blendMode == BlendMode.Additive;
        
        if ((propsToApply & AnimatedProperties.PositionX) != 0)
            position.x = isAdditive ? position.x + sampled.position.x : sampled.position.x;
        if ((propsToApply & AnimatedProperties.PositionY) != 0)
            position.y = isAdditive ? position.y + sampled.position.y : sampled.position.y;
        if ((propsToApply & AnimatedProperties.PositionZ) != 0)
            position.z = isAdditive ? position.z + sampled.position.z : sampled.position.z;
        if ((propsToApply & AnimatedProperties.Rotation) != 0)
            rotation = isAdditive ? rotation + sampled.rotation : sampled.rotation;
        if ((propsToApply & AnimatedProperties.ScaleX) != 0)
            scale.x = isAdditive ? scale.x * sampled.scale.x : sampled.scale.x;
        if ((propsToApply & AnimatedProperties.ScaleY) != 0)
            scale.y = isAdditive ? scale.y * sampled.scale.y : sampled.scale.y;
        if ((propsToApply & AnimatedProperties.ImageIndex) != 0 && sampled.imageIndex >= 0)
            imageIndex = sampled.imageIndex;
    }
}