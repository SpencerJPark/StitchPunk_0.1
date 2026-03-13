// =====================================
// EDITOR ANIMATION SYSTEM (FULL DEBUG VERSION)
// =====================================

using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct EditorAnimationSystem : ISystem
{
    private int frameCount;
    
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationEditorActive>();
        state.RequireForUpdate<EditorAnimationTimeControl>();
        frameCount = 0;
    }
    
    public void OnUpdate(ref SystemState state)
    {
        frameCount++;
        bool log = frameCount % 120 == 1; // Log every 2 seconds
        
        if (log) Debug.Log("=== [EditorAnim] FRAME START ===");
        
        try
        {
            // Step 1: Get time control entity
            Entity timeControlEntity = SystemAPI.GetSingletonEntity<EditorAnimationTimeControl>();
            if (log) Debug.Log($"[EditorAnim] Step 1: Found timeControlEntity: {timeControlEntity}");
            
            // Step 2: Check for library
            if (!state.EntityManager.HasComponent<EditorAnimationLibraryManaged>(timeControlEntity))
            {
                if (log) Debug.LogError("[EditorAnim] Step 2: FAILED - No EditorAnimationLibraryManaged!");
                return;
            }
            
            var libraryManaged = state.EntityManager.GetComponentObject<EditorAnimationLibraryManaged>(timeControlEntity);
            if (libraryManaged?.library == null)
            {
                if (log) Debug.LogError("[EditorAnim] Step 2: FAILED - Library is null!");
                return;
            }
            if (log) Debug.Log($"[EditorAnim] Step 2: Library OK, has {libraryManaged.library.clips?.Count ?? 0} clips");
            
            // Step 3: Get time control data
            var timeControl = SystemAPI.GetSingleton<EditorAnimationTimeControl>();
            if (log) Debug.Log($"[EditorAnim] Step 3: TimeControl - paused={timeControl.isPaused}, time={timeControl.normalizedTime:F3}");
            
            float dt = SystemAPI.Time.DeltaTime;
            int frameRate = GlobalGameData.Instance != null ? GlobalGameData.Instance.animationFrameRate : 24;
            
            // Step 4: Count entities with AnimationLayer
            int layerEntityCount = 0;
            foreach (var layers in SystemAPI.Query<DynamicBuffer<AnimationLayer>>())
            {
                layerEntityCount++;
            }
            if (log) Debug.Log($"[EditorAnim] Step 4: Found {layerEntityCount} entities with AnimationLayer");
            
            // Step 5: Count entities with AnimationLayer AND AnimatorTarget
            int animatorEntityCount = 0;
            int totalTargets = 0;
            foreach (var (layers, targets) in SystemAPI.Query<DynamicBuffer<AnimationLayer>, DynamicBuffer<AnimatorTarget>>())
            {
                animatorEntityCount++;
                totalTargets += targets.Length;
                
                if (log)
                {
                    Debug.Log($"[EditorAnim] Step 5: Animator entity has {layers.Length} layers, {targets.Length} targets");
                    for (int i = 0; i < layers.Length; i++)
                    {
                        Debug.Log($"[EditorAnim]   Layer[{i}]: {layers[i].animation}, time={layers[i].time:F3}, active={layers[i].active}");
                    }
                }
            }
            if (log) Debug.Log($"[EditorAnim] Step 5: Found {animatorEntityCount} animator entities, {totalTargets} total targets");
            
            if (animatorEntityCount == 0)
            {
                if (log) Debug.LogError("[EditorAnim] Step 5: FAILED - No entities with both AnimationLayer AND AnimatorTarget!");
                return;
            }
            
            // Step 6: Update layer times (if not paused)
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
            if (log) Debug.Log("[EditorAnim] Step 6: Layer times updated");
            
            // Step 7: Get component lookups
            var animatedPoseLookup = SystemAPI.GetComponentLookup<AnimationTargetPose>(false);
            var restPoseLookup = SystemAPI.GetComponentLookup<AnimationTargetRestPose>(true);
            if (log) Debug.Log("[EditorAnim] Step 7: Got component lookups");
            
            // Step 8: Sample and apply poses
            int posesApplied = 0;
            int posesSkippedNoPose = 0;
            int posesSkippedNoRest = 0;
            
            foreach (var (layers, targets) in SystemAPI.Query<DynamicBuffer<AnimationLayer>, DynamicBuffer<AnimatorTarget>>())
            {
                if (log) Debug.Log($"[EditorAnim] Step 8: Processing animator with {targets.Length} targets");
                
                for (int i = 0; i < targets.Length; i++)
                {
                    Entity targetEntity = targets[i].entity;
                    AnimationTarget partType = targets[i].target;
                    
                    if (!animatedPoseLookup.HasComponent(targetEntity))
                    {
                        posesSkippedNoPose++;
                        if (log) Debug.LogWarning($"[EditorAnim] Target {partType} (entity {targetEntity}) missing AnimationTargetPose!");
                        continue;
                    }
                    if (!restPoseLookup.HasComponent(targetEntity))
                    {
                        posesSkippedNoRest++;
                        if (log) Debug.LogWarning($"[EditorAnim] Target {partType} (entity {targetEntity}) missing AnimationTargetRestPose!");
                        continue;
                    }
                    
                    var restPose = restPoseLookup[targetEntity];
                    
                    float3 finalPosition = restPose.localPosition;
                    float finalRotation = restPose.rotation;
                    float2 finalScale = restPose.scale;
                    int finalImageIndex = restPose.baseImageIndex;
                    
                    AnimatedProperties appliedProperties = AnimatedProperties.None;
                    
                    // Process layers
                    for (int layerIdx = layers.Length - 1; layerIdx >= 0; layerIdx--)
                    {
                        var layer = layers[layerIdx];
                        if (!layer.active) continue;
                        
                        if (timeControl.soloLayerIndex >= 0 && layerIdx != timeControl.soloLayerIndex)
                            continue;
                        
                        AnimationClipSO clipSO = libraryManaged.library.GetClip(layer.animation);
                        if (clipSO == null)
                        {
                            if (log) Debug.LogWarning($"[EditorAnim] No clip found for animation: {layer.animation}");
                            continue;
                        }
                        if (clipSO.duration <= 0) continue;
                        
                        // Check if clip has this part
                        bool hasTrack = false;
                        if (clipSO.partTracks != null)
                        {
                            foreach (var track in clipSO.partTracks)
                            {
                                if (track.animationTarget == partType)
                                {
                                    hasTrack = true;
                                    if (log) Debug.Log($"[EditorAnim] Found track for {partType} in {clipSO.name}, {track.keyframes?.Count ?? 0} keyframes");
                                    break;
                                }
                            }
                        }
                        
                        if (!hasTrack && log)
                        {
                            Debug.Log($"[EditorAnim] No track for {partType} in {clipSO.name}");
                        }
                        
                        float normalizedTime = layer.time / clipSO.duration;
                        float quantizedTime = QuantizeTime(normalizedTime, clipSO.duration, frameRate);
                        
                        float3 beforePos = finalPosition;
                        float beforeRot = finalRotation;
                        
                        SampleClipSO(
                            clipSO,
                            partType,
                            quantizedTime,
                            ref finalPosition,
                            ref finalRotation,
                            ref finalScale,
                            ref finalImageIndex,
                            ref appliedProperties);
                        
                        if (log && (math.any(beforePos != finalPosition) || math.abs(beforeRot - finalRotation) > 0.01f))
                        {
                            Debug.Log($"[EditorAnim] {partType} CHANGED: pos {beforePos} -> {finalPosition}, rot {beforeRot:F1} -> {finalRotation:F1}");
                        }
                    }
                    
                    // WRITE THE POSE
                    animatedPoseLookup[targetEntity] = new AnimationTargetPose
                    {
                        localPosition = finalPosition,
                        rotation = finalRotation,
                        scale = finalScale,
                        imageIndex = finalImageIndex
                    };
                    posesApplied++;
                }
            }
            
            if (log) Debug.Log($"[EditorAnim] Step 8 DONE: Applied {posesApplied} poses, skipped {posesSkippedNoPose} (no pose), {posesSkippedNoRest} (no rest)");
            
            // Step 9: Update time control for UI
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
            
            if (log) Debug.Log("=== [EditorAnim] FRAME END ===");
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