// =====================================
// ANIMATION SAMPLING SYSTEM
// =====================================

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AnimationSystemGroup))]
[UpdateAfter(typeof(AnimationTimeSystem))]
public partial struct AnimationSamplingSystem : ISystem
{
    ComponentLookup<AnimationTargetRestPose> restPoseLookup;
    ComponentLookup<AnimationTargetPose> animatedPoseLookup;
    
    private int lastSampledFrame;
    private float accumulatedTime;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationLibrary>();
        state.RequireForUpdate<GameSceneTag>();
        
        restPoseLookup = SystemAPI.GetComponentLookup<AnimationTargetRestPose>(true);
        animatedPoseLookup = SystemAPI.GetComponentLookup<AnimationTargetPose>(false);
        
        lastSampledFrame = -1;
        accumulatedTime = 0f;
    }
    
    public void OnUpdate(ref SystemState state)
    {
        int frameRate = GlobalGameData.Instance != null ? GlobalGameData.Instance.animationFrameRate : 24;
        
        accumulatedTime += SystemAPI.Time.DeltaTime;
        int currentFrame = (int)(accumulatedTime * frameRate);
        
        if (currentFrame == lastSampledFrame)
        {
            return;
        }
        
        lastSampledFrame = currentFrame;
        
        BlobAssetReference<AnimationLibraryBlob> library = SystemAPI.GetSingleton<AnimationLibrary>().library;
        
        restPoseLookup.Update(ref state);
        animatedPoseLookup.Update(ref state);
        
        new SampleLayeredAnimationJob
        {
            library = library,
            restPoseLookup = restPoseLookup,
            animatedPoseLookup = animatedPoseLookup,
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct SampleLayeredAnimationJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<AnimationLibraryBlob> library;
    [ReadOnly] public ComponentLookup<AnimationTargetRestPose> restPoseLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<AnimationTargetPose> animatedPoseLookup;
    
    public void Execute(
        in DynamicBuffer<AnimationLayer> layers,
        in DynamicBuffer<AnimatorTarget> targets)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            Entity targetEntity = targets[i].entity;
            AnimationTarget partType = targets[i].target;
            
            if (!animatedPoseLookup.HasComponent(targetEntity)) continue;
            if (!restPoseLookup.HasComponent(targetEntity)) continue;
            
            AnimationTargetRestPose restPose = restPoseLookup[targetEntity];
            
            float3 finalPosition = restPose.localPosition;
            float finalRotation = restPose.rotation;
            float2 finalScale = restPose.scale;
            int finalImageIndex = restPose.baseImageIndex;
            
            // Track which properties have been set by higher layers
            AnimatedProperties appliedProperties = AnimatedProperties.None;
            
            // Process layers in reverse order (highest priority first)
            for (int layerIdx = layers.Length - 1; layerIdx >= 0; layerIdx--)
            {
                var layer = layers[layerIdx];
                if (!layer.active) continue;
                
                ref AnimationClipBlob clip = ref library.Value.clips[(int)layer.animation];
                if (clip.duration <= 0) continue;
                
                float normalizedTime = layer.time / clip.duration;
                
                // Try to sample this part from this layer
                SampleClipForPart(
                    ref clip,
                    partType,
                    normalizedTime,
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
    
    private void SampleClipForPart(
        ref AnimationClipBlob clip,
        AnimationTarget target,
        float normalizedTime,
        ref float3 position,
        ref float rotation,
        ref float2 scale,
        ref int imageIndex,
        ref AnimatedProperties appliedProperties)
    {
        for (int t = 0; t < clip.animationTargetTracks.Length; t++)
        {
            ref AnimationTargetTrackBlob track = ref clip.animationTargetTracks[t];
            if (track.animationTarget != target) continue;
            if (track.keyframes.Length == 0) continue;
            
            // Only apply properties not already set by higher layers
            AnimatedProperties propsToApply = track.animatedProperties & ~appliedProperties;
            if (propsToApply == AnimatedProperties.None) break;
            
            KeyframeBlob sampled = SampleKeyframes(ref track, normalizedTime);
            ApplyTrackToPose(ref track, ref sampled, propsToApply, ref position, ref rotation, ref scale, ref imageIndex);
            
            // Mark these properties as applied
            appliedProperties |= track.animatedProperties;
            
            break;
        }
    }
    
    private KeyframeBlob SampleKeyframes(ref AnimationTargetTrackBlob track, float normalizedTime)
    {
        ref BlobArray<KeyframeBlob> keyframes = ref track.keyframes;
        
        int prevIndex = 0;
        int nextIndex = 0;
        
        for (int i = 0; i < keyframes.Length; i++)
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
        
        ref KeyframeBlob prev = ref keyframes[prevIndex];
        ref KeyframeBlob next = ref keyframes[nextIndex];
        
        if (prevIndex == nextIndex || prev.interpolation == InterpolationMode.Step)
        {
            return prev;
        }
        
        float range = next.normalizedTime - prev.normalizedTime;
        float t = range > 0 ? (normalizedTime - prev.normalizedTime) / range : 0f;
        t = ApplyEasing(t, prev.interpolation);
        
        return new KeyframeBlob
        {
            normalizedTime = normalizedTime,
            position = math.lerp(prev.position, next.position, t),
            rotation = math.lerp(prev.rotation, next.rotation, t),
            scale = math.lerp(prev.scale, next.scale, t),
            imageIndex = t < 0.5f ? prev.imageIndex : next.imageIndex,
            interpolation = prev.interpolation
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
        ref AnimationTargetTrackBlob track,
        ref KeyframeBlob sampled,
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