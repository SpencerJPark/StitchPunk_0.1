using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AnimationTimeSystem))]
[UpdateBefore(typeof(ApplyAnimatedPoseSystem))]
public partial struct AnimationSamplingSystem : ISystem
{
    ComponentLookup<AnimationTargetRestPose> animationTargetRestPoseLookup;
    ComponentLookup<AnimationTargetPose> animatedPoseLookup;
    ComponentLookup<AnimationTargetTag> animationTargetLookup;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationLibrary>();
        state.RequireForUpdate<GameSceneTag>();
        
        animationTargetRestPoseLookup = SystemAPI.GetComponentLookup<AnimationTargetRestPose>(true);
        animatedPoseLookup = SystemAPI.GetComponentLookup<AnimationTargetPose>(false);
        animationTargetLookup = SystemAPI.GetComponentLookup<AnimationTargetTag>(true);
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<AnimationLibraryBlob> library = SystemAPI.GetSingleton<AnimationLibrary>().library;
        
        animationTargetRestPoseLookup.Update(ref state);
        animatedPoseLookup.Update(ref state);
        animationTargetLookup.Update(ref state);
        
        new SampleAnimationJob
        {
            library = library,
            restPoseLookup = animationTargetRestPoseLookup,
            animatedPoseLookup = animatedPoseLookup,
            bodyPartLookup = animationTargetLookup,
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct SampleAnimationJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<AnimationLibraryBlob> library;
    [ReadOnly] public ComponentLookup<AnimationTargetRestPose> restPoseLookup;
    [ReadOnly] public ComponentLookup<AnimationTargetTag> bodyPartLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<AnimationTargetPose> animatedPoseLookup;
    
    public void Execute(in Animator anim, in DynamicBuffer<AnimatorTarget> animationTargets)
    {
        UnityEngine.Debug.Log($"[SampleJob] anim.time={anim.time}, targets={animationTargets.Length}");
        ref AnimationClipBlob clip = ref library.Value.clips[(int)anim.currentAnimation];
        float normalizedTime = clip.duration > 0 ? anim.time / clip.duration : 0f;
        
        // Process each body part
        for (int i = 0; i < animationTargets.Length; i++)
        {
            Entity targetEntity = animationTargets[i].entity;
            AnimationTarget partType = animationTargets[i].target;
            
            if (!animatedPoseLookup.HasComponent(targetEntity)) continue;
            
            AnimationTargetRestPose restPose = restPoseLookup[targetEntity];
            
            // Start with rest pose
            float3 finalPosition = restPose.localPosition;
            float finalRotation = restPose.rotation;
            float2 finalScale = restPose.scale;
            int finalImageIndex = restPose.baseImageIndex;
            
            // Sample primary animation
            SampleClipForPart(ref clip, partType, normalizedTime,
                ref finalPosition, ref finalRotation, ref finalScale, ref finalImageIndex);
            
            // Handle blending from previous animation
            if (anim.isBlending && anim.blendWeight < 1f)
            {
                ref AnimationClipBlob blendFromClip = ref library.Value.clips[(int)anim.blendFromAnimation];
                float blendFromNormTime = blendFromClip.duration > 0 ? anim.blendFromTime / blendFromClip.duration : 0f;
                
                float3 blendFromPos = restPose.localPosition;
                float blendFromRot = restPose.rotation;
                float2 blendFromScale = restPose.scale;
                int blendFromImage = restPose.baseImageIndex;
                
                SampleClipForPart(ref blendFromClip, partType, blendFromNormTime,
                    ref blendFromPos, ref blendFromRot, ref blendFromScale, ref blendFromImage);
                
                // Lerp between blend-from and current
                finalPosition = math.lerp(blendFromPos, finalPosition, anim.blendWeight);
                finalRotation = math.lerp(blendFromRot, finalRotation, anim.blendWeight);
                finalScale = math.lerp(blendFromScale, finalScale, anim.blendWeight);
                // For image index, snap at 50% blend
                if (anim.blendWeight < 0.5f) finalImageIndex = blendFromImage;
            }
            
            // Write final pose
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
        ref int imageIndex)
    {
        for (int t = 0; t < clip.animationTargetTracks.Length; t++)
        {
            ref AnimationTargetTrackBlob track = ref clip.animationTargetTracks[t];
            if (track.animationTarget != target) continue;
            if (track.keyframes.Length == 0) continue;
        
            // Check keyframe scale values
            for (int k = 0; k < track.keyframes.Length; k++)
            {
                ref KeyframeBlob kf = ref track.keyframes[k];
            }
        
            KeyframeBlob sampled = SampleKeyframes(ref track, normalizedTime);
        
            ApplyTrackTopose(ref track, ref sampled, ref position, ref rotation, ref scale, ref imageIndex);
        
            break;
        }
    }
    
    private KeyframeBlob SampleKeyframes(ref AnimationTargetTrackBlob track, float normalizedTime)
    {
        ref BlobArray<KeyframeBlob> keyframes = ref track.keyframes;
        
        // Find surrounding keyframes
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
        
        // Same keyframe or step interpolation
        if (prevIndex == nextIndex || prev.interpolation == InterpolationMode.Step)
        {
            return prev;
        }
        
        // Calculate t value for interpolation
        float range = next.normalizedTime - prev.normalizedTime;
        float t = range > 0 ? (normalizedTime - prev.normalizedTime) / range : 0f;
        
        // Apply easing
        t = ApplyEasing(t, prev.interpolation);
        
        return new KeyframeBlob
        {
            normalizedTime = normalizedTime,
            position = math.lerp(prev.position, next.position, t),
            rotation = math.lerp(prev.rotation, next.rotation, t),
            scale = math.lerp(prev.scale, next.scale, t),
            imageIndex = t < 0.5f ? prev.imageIndex : next.imageIndex, // Snap at midpoint
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
    
    private void ApplyTrackTopose(
        ref AnimationTargetTrackBlob track,
        ref KeyframeBlob sampled,
        ref float3 position,
        ref float rotation,
        ref float2 scale,
        ref int imageIndex)
    {
        AnimatedProperties props = track.animatedProperties;
        bool isAdditive = track.blendMode == BlendMode.Additive;
        
        if ((props & AnimatedProperties.PositionX) != 0)
        {
            position.x = isAdditive ? position.x + sampled.position.x : sampled.position.x;
        }
        if ((props & AnimatedProperties.PositionY) != 0)
        {
            position.y = isAdditive ? position.y + sampled.position.y : sampled.position.y;
        }
        if ((props & AnimatedProperties.PositionZ) != 0)
        {
            position.z = isAdditive ? position.z + sampled.position.z : sampled.position.z;
        }
        if ((props & AnimatedProperties.Rotation) != 0)
        {
            rotation = isAdditive ? rotation + sampled.rotation : sampled.rotation;
        }
        if ((props & AnimatedProperties.ScaleX) != 0)
        {
            scale.x = isAdditive ? scale.x * sampled.scale.x : sampled.scale.x;
        }
        if ((props & AnimatedProperties.ScaleY) != 0)
        {
            scale.y = isAdditive ? scale.y * sampled.scale.y : sampled.scale.y;
        }
        if ((props & AnimatedProperties.ImageIndex) != 0 && sampled.imageIndex >= 0)
        {
            imageIndex = sampled.imageIndex; // Image index is always override
        }
    }
}






