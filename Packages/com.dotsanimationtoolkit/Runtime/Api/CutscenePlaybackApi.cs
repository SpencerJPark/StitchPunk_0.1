// Copyright (c) 2026 Spencer Park. All rights reserved.

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
                nextEventIndex = 0
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
                slotStates[slotIndex] = new CutsceneSlotRuntimeState { nextClipBlockIndex = 0 };
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
    }
}
