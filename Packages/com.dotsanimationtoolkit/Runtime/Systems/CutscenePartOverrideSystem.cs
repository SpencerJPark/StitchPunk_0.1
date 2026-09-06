// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs between <c>TransformSampleSystem</c> and <c>TransformApplySystem</c>: composes a
    /// cutscene's per-part override tracks onto <see cref="TargetPose"/> after clip composition but
    /// before the pose reaches <c>LocalTransform</c>/<c>PostTransformMatrix</c>.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(TransformSampleSystem))]
    [UpdateBefore(typeof(TransformApplySystem))]
    public partial struct CutscenePartOverrideSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CutscenePlay>();
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager entityManager = state.EntityManager;

            foreach ((RefRO<CutscenePlay> playRO, RefRO<CutscenePlaybackState> stateRO, Entity requestEntity) in
                SystemAPI.Query<RefRO<CutscenePlay>, RefRO<CutscenePlaybackState>>().WithEntityAccess())
            {
                if (stateRO.ValueRO.isComplete)
                {
                    continue;
                }
                ApplyPartOverrides(entityManager, playRO.ValueRO, stateRO.ValueRO, requestEntity);
            }
        }

        private static void ApplyPartOverrides(
            EntityManager entityManager, CutscenePlay play, CutscenePlaybackState playbackState, Entity requestEntity)
        {
            ref CutsceneBlob blob = ref play.blob.Value;
            ref CutsceneSegmentBlob segment = ref blob.segments[playbackState.segmentIndex];
            DynamicBuffer<CutsceneActorBinding> bindings = entityManager.GetBuffer<CutsceneActorBinding>(requestEntity);

            for (int slotIndex = 0; slotIndex < blob.slots.Length; slotIndex++)
            {
                if (blob.slots[slotIndex].kind != CutsceneSlotKind.Actor)
                {
                    continue;
                }
                Entity actorEntity;
                if (!TryResolveBinding(bindings, blob.slots[slotIndex].slotId, out actorEntity) ||
                    !entityManager.HasComponent<RigPartRef>(actorEntity))
                {
                    continue;
                }

                DynamicBuffer<RigPartRef> partRefs = entityManager.GetBuffer<RigPartRef>(actorEntity);
                ref CutsceneSlotSegmentBlob slotSegment = ref segment.slotTracks[slotIndex];
                for (int trackIndex = 0; trackIndex < slotSegment.partTracks.Length; trackIndex++)
                {
                    ref CutscenePartTrackBlob track = ref slotSegment.partTracks[trackIndex];
                    // targetIndex was resolved once at cutscene bake time against the slot's
                    // authored rig; it only agrees with RigPartRef below when the bound actor still
                    // uses that same rig. A slot recast to a different rig needs a rebake.
                    if (track.targetIndex < 0 || track.keys.Length == 0)
                    {
                        continue;
                    }

                    Entity partEntity = Entity.Null;
                    for (int partRefIndex = 0; partRefIndex < partRefs.Length; partRefIndex++)
                    {
                        if (partRefs[partRefIndex].targetIndex == track.targetIndex)
                        {
                            partEntity = partRefs[partRefIndex].part;
                            break;
                        }
                    }
                    if (partEntity == Entity.Null || !entityManager.HasComponent<TargetPose>(partEntity))
                    {
                        continue;
                    }

                    float3 sampledPosition;
                    float3 sampledRotation;
                    float3 sampledScale;
                    if (!CutsceneBlobSampler.TrySampleTransform(
                        ref track.keys, playbackState.timeInSegment,
                        out sampledPosition, out sampledRotation, out sampledScale))
                    {
                        continue;
                    }

                    TargetPose pose = entityManager.GetComponentData<TargetPose>(partEntity);

                    // An unmasked channel is left at the part's already-composited value, not its
                    // rest pose: the track's mask names only the channels it owns.
                    float3 position = pose.localPosition;
                    if ((track.channels & AnimatedChannels.PositionXY) != 0)
                    {
                        position.x = sampledPosition.x;
                        position.y = sampledPosition.y;
                    }
                    if ((track.channels & AnimatedChannels.PositionZ) != 0)
                    {
                        position.z = sampledPosition.z;
                    }
                    pose.localPosition = position;

                    if ((track.channels & AnimatedChannels.Rotation) != 0)
                    {
                        pose.rotation = sampledRotation;
                    }
                    if ((track.channels & AnimatedChannels.Scale) != 0)
                    {
                        pose.scale = sampledScale;
                    }

                    entityManager.SetComponentData(partEntity, pose);
                }
            }
        }

        private static bool TryResolveBinding(
            DynamicBuffer<CutsceneActorBinding> bindings, uint slotId, out Entity boundEntity)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].slotId == slotId)
                {
                    boundEntity = bindings[i].actorEntity;
                    return boundEntity != Entity.Null;
                }
            }
            boundEntity = Entity.Null;
            return false;
        }
    }
}
