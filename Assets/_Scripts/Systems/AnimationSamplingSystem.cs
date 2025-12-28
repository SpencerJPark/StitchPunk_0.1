using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AnimationTimeSystem))]
public partial struct AnimationSamplingSystem : ISystem
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
        
        var restPoseLookup = SystemAPI.GetComponentLookup<PartRestPose>(true);
        var animatedPoseLookup = SystemAPI.GetComponentLookup<PartAnimatedPose>(false);
        var bodyPartLookup = SystemAPI.GetComponentLookup<BodyPartTag>(true);
        
        new SampleAnimationJob
        {
            library = library,
            restPoseLookup = restPoseLookup,
            animatedPoseLookup = animatedPoseLookup,
            bodyPartLookup = bodyPartLookup,
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct SampleAnimationJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<AnimationLibraryBlob> library;
    [ReadOnly] public ComponentLookup<PartRestPose> restPoseLookup;
    [ReadOnly] public ComponentLookup<BodyPartTag> bodyPartLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<PartAnimatedPose> animatedPoseLookup;
    
    public void Execute(in CharacterAnimation anim, in DynamicBuffer<CharacterBodyPart> parts)
    {
        ref AnimationClipBlob clip = ref library.Value.clips[(int)anim.currentAnimation];
        float normalizedTime = clip.duration > 0 ? anim.time / clip.duration : 0f;
        
        // Process each body part
        for (int i = 0; i < parts.Length; i++)
        {
            Entity partEntity = parts[i].entity;
            BodyPart partType = parts[i].part;
            
            if (!animatedPoseLookup.HasComponent(partEntity)) continue;
            
            PartRestPose restPose = restPoseLookup[partEntity];
            
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
            animatedPoseLookup[partEntity] = new PartAnimatedPose
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
        BodyPart partType,
        float normalizedTime,
        ref float3 position,
        ref float rotation,
        ref float2 scale,
        ref int imageIndex)
    {
        // Find track for this part
        for (int t = 0; t < clip.partTracks.Length; t++)
        {
            ref PartTrackBlob track = ref clip.partTracks[t];
            if (track.bodyPart != partType) continue;
            if (track.keyframes.Length == 0) continue;
            
            // Sample keyframes
            KeyframeBlob sampled = SampleKeyframes(ref track, normalizedTime);
            
            // Apply based on blend mode and animated properties
            ApplyTrackTopose(ref track, ref sampled, ref position, ref rotation, ref scale, ref imageIndex);
            
            break; // Found our track, done
        }
    }
    
    private KeyframeBlob SampleKeyframes(ref PartTrackBlob track, float normalizedTime)
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
        ref PartTrackBlob track,
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
