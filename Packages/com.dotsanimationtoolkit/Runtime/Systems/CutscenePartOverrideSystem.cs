// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Composes a cutscene's per-part override tracks onto <see cref="TargetPose"/> — the Override
    /// layer spec §2 calls for, applied the same way <c>ApplyHeldTargetPose</c> applies a held edit
    /// in the editor: written directly onto the composited pose, after composition, before it
    /// reaches a renderer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Ordering is the whole mechanism.</strong> Running after <c>TransformSampleSystem</c>
    /// means every part already holds this frame's clip-composited pose; running before
    /// <c>TransformApplySystem</c> means the override is what actually reaches
    /// <c>LocalTransform</c>/<c>PostTransformMatrix</c>. No second sampler, no new component the
    /// render path has to know about — see <see cref="CutsceneTimelineSystem"/>'s own remarks for
    /// why that system (a Logic-group concern: time, commands, events) cannot reach this point in
    /// the frame itself.
    /// </para>
    /// <para>
    /// <strong>An unmasked channel is left at the part's already-composited value, not its rest
    /// pose.</strong> A track's <see cref="AnimatedChannels"/> mask names only the channels it
    /// owns; the rest of the pose is whatever the actor's own clip layers already decided this
    /// frame, exactly like a clip transform track's own <c>TrackBlendOp.Override</c> only replaces
    /// its masked channels rather than the whole pose.
    /// </para>
    /// <para>
    /// <strong>Recast caveat (decision G-D9).</strong> <see cref="CutscenePartTrackBlob.targetIndex"/>
    /// was resolved once at cutscene bake time against the slot's authored rig; it is matched here
    /// against the bound actor's own <see cref="RigPartRef"/> buffer, which only agrees when the
    /// bound actor still uses that same rig. A slot recast to a different rig for the runtime player
    /// needs a rebake — see the blob's own remarks.
    /// </para>
    /// </remarks>
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
                    CutsceneBlobSampler.SampleTransform(
                        ref track.keys, playbackState.timeInSegment,
                        out sampledPosition, out sampledRotation, out sampledScale);

                    TargetPose pose = entityManager.GetComponentData<TargetPose>(partEntity);

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
