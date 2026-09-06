// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The write side of starting and skipping a cutscene (Phase G §6). Everything else a host
    /// needs to steer a running cutscene is a direct field write on the public components
    /// (<see cref="CutsceneControl"/>, <see cref="CutsceneHoldRelease"/>) — this exists only for the
    /// one operation that has more than one field to get right: standing up a fresh request with
    /// its internal bookkeeping correctly sized.
    /// </summary>
    public static class CutscenePlaybackApi
    {
        /// <summary>
        /// Creates a cutscene play request: <see cref="CutscenePlay"/>, a fresh
        /// <see cref="CutsceneControl"/>, zeroed <see cref="CutscenePlaybackState"/>, an empty
        /// <see cref="CutsceneActorBinding"/> buffer for the host to fill, and the internal
        /// <see cref="CutsceneSlotRuntimeState"/> bookkeeping pre-sized to the blob's slot count.
        /// </summary>
        /// <param name="entityManager">The world to create the request in.</param>
        /// <param name="blob">The baked cutscene. The player never disposes it.</param>
        /// <param name="layerIndex">Which playback layer clip blocks target on every bound actor.</param>
        /// <param name="speed">Initial playback speed; 1 is normal.</param>
        /// <returns>The new request entity. The host must still fill <see cref="CutsceneActorBinding"/> before the player can do anything with an Actor/Prop slot.</returns>
        public static Entity CreatePlayRequest(
            EntityManager entityManager,
            BlobAssetReference<CutsceneBlob> blob,
            byte layerIndex = 0,
            float speed = 1f)
        {
            Entity requestEntity = entityManager.CreateEntity();

            entityManager.AddComponentData(requestEntity, new CutscenePlay
            {
                blob = blob,
                layerIndex = layerIndex
            });
            entityManager.AddComponentData(requestEntity, new CutsceneControl
            {
                paused = false,
                speed = speed,
                skipRequested = false
            });
            entityManager.AddComponentData(requestEntity, new CutscenePlaybackState
            {
                segmentIndex = 0,
                timeInSegment = 0f,
                isPausedOnHold = false,
                isComplete = false,
                nextEventIndex = 0,
                appliedLayerSpeed = -1f
            });
            entityManager.AddBuffer<CutsceneActorBinding>(requestEntity);
            entityManager.AddComponentData(requestEntity, default(CutsceneHoldRelease));
            entityManager.SetComponentEnabled<CutsceneHoldRelease>(requestEntity, false);

            // Same output shape a clip's own events use (spec §6), scoped to this request entity
            // rather than any one bound actor — a cutscene event is not about one slot.
            entityManager.AddBuffer<AnimEventOutput>(requestEntity);
            entityManager.AddComponent<AnimEventsPending>(requestEntity);
            entityManager.SetComponentEnabled<AnimEventsPending>(requestEntity, false);

            DynamicBuffer<CutsceneSlotRuntimeState> slotStates =
                entityManager.AddBuffer<CutsceneSlotRuntimeState>(requestEntity);
            int slotCount = blob.Value.slots.Length;
            slotStates.ResizeUninitialized(slotCount);
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                slotStates[slotIndex] = new CutsceneSlotRuntimeState
                {
                    nextClipBlockIndex = 0,
                    nextAttachMarkerIndex = 0,
                    // −1, not 0: 0 is a real slot index, so a zeroed struct would read as "riding
                    // slot 0" and suppress the root lane of every slot before anything attached.
                    attachedHostSlotIndex = -1,
                    // Same reason: 0 is a real segment index, and "no block playing yet" has to be
                    // distinguishable from "playing the first block of segment 0".
                    activeBlockSegmentIndex = -1,
                    activeBlockSpeed = 1f
                };
            }

            return requestEntity;
        }

        /// <summary>Requests an immediate jump to the cutscene's end (spec §4). Equivalent to writing <see cref="CutsceneControl.skipRequested"/> directly; a convenience for the common one-field case.</summary>
        public static void RequestSkip(EntityManager entityManager, Entity requestEntity)
        {
            CutsceneControl control = entityManager.GetComponentData<CutsceneControl>(requestEntity);
            control.skipRequested = true;
            entityManager.SetComponentData(requestEntity, control);
        }

        /// <summary>
        /// The id of the hold the cutscene is currently paused on (amendment A65 §3.1), so a host
        /// can answer "what is the clock waiting for?" without reaching into the blob — the hold a
        /// dialogue cue derives is named after the event, and the host learns that name only here.
        /// </summary>
        /// <returns>False whenever the request is not paused on a hold.</returns>
        public static bool TryGetCurrentHoldId(
            EntityManager entityManager, Entity requestEntity, out FixedString64Bytes holdId)
        {
            holdId = default;
            if (!entityManager.Exists(requestEntity)
                || !entityManager.HasComponent<CutscenePlay>(requestEntity)
                || !entityManager.HasComponent<CutscenePlaybackState>(requestEntity))
            {
                return false;
            }

            CutscenePlaybackState playbackState =
                entityManager.GetComponentData<CutscenePlaybackState>(requestEntity);
            if (!playbackState.isPausedOnHold)
            {
                return false;
            }

            CutscenePlay play = entityManager.GetComponentData<CutscenePlay>(requestEntity);
            if (!play.blob.IsCreated)
            {
                return false;
            }

            ref CutsceneBlob blob = ref play.blob.Value;
            if (playbackState.segmentIndex < 0 || playbackState.segmentIndex >= blob.segments.Length)
            {
                return false;
            }

            holdId = blob.segments[playbackState.segmentIndex].holdId;
            return true;
        }

        /// <summary>
        /// Creates a play request from a baked <see cref="CutsceneStage"/> (amendment A61): the same
        /// as <see cref="CreatePlayRequest"/>, plus every <see cref="CutsceneStageBinding"/> on
        /// <paramref name="stageEntity"/> copied into the new request's <see cref="CutsceneActorBinding"/>
        /// buffer. The host may still add or overwrite entries afterward for actors the stage's
        /// subscene never baked (spec §3.1's cross-scene trap) or that were spawned at runtime.
        /// </summary>
        public static Entity CreatePlayRequestFromStage(
            EntityManager entityManager,
            Entity stageEntity,
            byte layerIndex = 0,
            float speed = 1f)
        {
            CutsceneStage stage = entityManager.GetComponentData<CutsceneStage>(stageEntity);
            Entity requestEntity = CreatePlayRequest(entityManager, stage.blob, layerIndex, speed);

            DynamicBuffer<CutsceneStageBinding> stageBindings =
                entityManager.GetBuffer<CutsceneStageBinding>(stageEntity);
            DynamicBuffer<CutsceneActorBinding> actorBindings =
                entityManager.GetBuffer<CutsceneActorBinding>(requestEntity);
            for (int bindingIndex = 0; bindingIndex < stageBindings.Length; bindingIndex++)
            {
                actorBindings.Add(new CutsceneActorBinding
                {
                    slotId = stageBindings[bindingIndex].slotId,
                    actorEntity = stageBindings[bindingIndex].target
                });
            }

            return requestEntity;
        }

        /// <summary>
        /// Finds the <see cref="CutsceneStage"/> whose <see cref="CutsceneStage.cutsceneKey"/> matches
        /// <paramref name="cutsceneKey"/> (amendment A61, decision A61-D3 — identity by stable id,
        /// never asset path or name). A linear scan over a temporary query; a host with many stages
        /// should cache the result rather than call this every frame.
        /// </summary>
        public static bool TryFindStage(EntityManager entityManager, ulong cutsceneKey, out Entity stageEntity)
        {
            EntityQuery stageQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CutsceneStage>());
            using (NativeArray<Entity> stageEntities = stageQuery.ToEntityArray(Allocator.Temp))
            {
                for (int stageIndex = 0; stageIndex < stageEntities.Length; stageIndex++)
                {
                    CutsceneStage stage = entityManager.GetComponentData<CutsceneStage>(stageEntities[stageIndex]);
                    if (stage.cutsceneKey == cutsceneKey)
                    {
                        stageEntity = stageEntities[stageIndex];
                        return true;
                    }
                }
            }
            stageEntity = Entity.Null;
            return false;
        }
    }
}
