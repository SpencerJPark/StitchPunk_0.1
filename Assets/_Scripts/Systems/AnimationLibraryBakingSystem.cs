// =====================================
// BAKING SYSTEM - Builds Blob Assets
// =====================================

using Unity.Entities;
using Unity.Collections;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct AnimationLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationLibraryReference>();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        // Get the library SO
        AnimationLibrarySO librarySO = null;
        foreach (var reference in SystemAPI.Query<RefRO<AnimationLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }
        
        if (librarySO == null) return;
        
        // Build blob asset
        using var builder = new BlobBuilder(Allocator.Temp);
        ref AnimationLibraryBlob libraryBlob = ref builder.ConstructRoot<AnimationLibraryBlob>();
        
        int clipCount = System.Enum.GetValues(typeof(AnimationType)).Length;
        var clipsBuilder = builder.Allocate(ref libraryBlob.clips, clipCount);
        
        // Initialize all clips to empty
        for (int i = 0; i < clipCount; i++)
        {
            clipsBuilder[i].animationType = (AnimationType)i;
            clipsBuilder[i].duration = 0;
            builder.Allocate(ref clipsBuilder[i].partTracks, 0);
        }
        
        // Fill in clips we have data for
        foreach (var clipSO in librarySO.clips)
        {
            if (clipSO == null) continue;
            
            int clipIndex = (int)clipSO.animationType;
            ref AnimationClipBlob clipBlob = ref clipsBuilder[clipIndex];
            
            clipBlob.animationType = clipSO.animationType;
            clipBlob.duration = clipSO.duration;
            clipBlob.looping = clipSO.looping;
            clipBlob.allowBlendIn = clipSO.allowBlendIn;
            clipBlob.allowBlendOut = clipSO.allowBlendOut;
            
            var tracksBuilder = builder.Allocate(ref clipBlob.partTracks, clipSO.partTracks.Count);
            
            for (int t = 0; t < clipSO.partTracks.Count; t++)
            {
                var trackSO = clipSO.partTracks[t];
                ref PartTrackBlob trackBlob = ref tracksBuilder[t];
                
                trackBlob.bodyPart = trackSO.bodyPart;
                trackBlob.blendMode = trackSO.blendMode;
                trackBlob.animatedProperties = trackSO.animatedProperties;
                trackBlob.defaultInterpolation = trackSO.interpolation;
                
                var keyframesBuilder = builder.Allocate(ref trackBlob.keyframes, trackSO.keyframes.Count);
                
                for (int k = 0; k < trackSO.keyframes.Count; k++)
                {
                    var kfSO = trackSO.keyframes[k];
                    keyframesBuilder[k] = new KeyframeBlob
                    {
                        normalizedTime = kfSO.normalizedTime,
                        position = kfSO.position,
                        rotation = kfSO.rotation,
                        scale = kfSO.scale,
                        imageIndex = kfSO.imageIndex,
                        interpolation = kfSO.overrideInterpolation ? kfSO.interpolationOverride : trackSO.interpolation,
                    };
                }
            }
        }
        
        // Assign to holder
        foreach (var holder in SystemAPI.Query<RefRW<AnimationLibrary>>())
        {
            holder.ValueRW.library = builder.CreateBlobAssetReference<AnimationLibraryBlob>(Allocator.Persistent);
        }
    }
    
    public void OnDestroy(ref SystemState state)
    {
        foreach (var holder in SystemAPI.Query<RefRW<AnimationLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
            {
                holder.ValueRW.library.Dispose();
            }
        }
    }
}

// Also need a baking system to populate CharacterBodyPart buffers
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct CharacterBodyPartBakingSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        
        foreach (var (partTag, parentChar, entity) in 
            SystemAPI.Query<RefRO<BodyPartTag>, RefRO<ParentCharacter>>().WithEntityAccess())
        {
            Entity characterEntity = parentChar.ValueRO.character;
            
            if (SystemAPI.HasBuffer<CharacterBodyPart>(characterEntity))
            {
                var buffer = SystemAPI.GetBuffer<CharacterBodyPart>(characterEntity);
                buffer.Add(new CharacterBodyPart
                {
                    entity = entity,
                    part = partTag.ValueRO.part
                });
            }
        }
        
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}