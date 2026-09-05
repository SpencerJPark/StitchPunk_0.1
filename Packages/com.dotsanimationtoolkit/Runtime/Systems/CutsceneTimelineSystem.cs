// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Advances every running cutscene's elastic clock, issues clip-block Play commands through the
    /// existing <see cref="AnimationCommand"/> API, writes root/prop transforms and the camera
    /// singleton, fires events, and handles hold-pause/release and skip (Phase G §6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Deliberately not <c>[BurstCompile]</c>, and not scheduled as a job.</strong> A handful
    /// of cutscenes ever run at once — this is nothing like the per-part sampling hot path — and the
    /// logic reaches across entities (a cutscene's own state, every bound actor's command buffer,
    /// one world camera singleton) in a way a single <c>IJobEntity</c> query cannot express cleanly.
    /// Plain <c>SystemAPI</c> calls in <c>OnUpdate</c> are not the banned pattern: CLAUDE.md forbids
    /// <c>.Run()</c> on a job, and there is no job object here to call it on.
    /// </para>
    /// <para>
    /// <strong>Part-track overrides are not applied here.</strong> They need to land after
    /// <c>TransformSampleSystem</c> composites the clip pose and before <c>TransformApplySystem</c>
    /// writes it — a Presentation-group ordering this Logic-group system cannot reach. See
    /// <see cref="CutscenePartOverrideSystem"/>.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitLogicSystemGroup))]
    public partial struct CutsceneTimelineSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CutscenePlay>();
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager entityManager = state.EntityManager;

            if (!SystemAPI.HasSingleton<CutsceneCameraPose>())
            {
                entityManager.CreateEntity(typeof(CutsceneCameraPose));
            }
            Entity cameraPoseEntity = SystemAPI.GetSingletonEntity<CutsceneCameraPose>();

            // Cleared every frame (amendment A62 defect 6) so a segment with no camera lane, or a
            // cutscene that just completed, reads as "not driven" rather than holding the last
            // frame's flag along with its stale pose.
            CutsceneCameraPose cameraPose = entityManager.GetComponentData<CutsceneCameraPose>(cameraPoseEntity);
            cameraPose.isDriven = false;
            entityManager.SetComponentData(cameraPoseEntity, cameraPose);

            float deltaTime = SystemAPI.Time.DeltaTime;

            // Structural changes are illegal inside a SystemAPI.Query loop, and an attach is nothing
            // but structural changes (amendment A63 §6). Every cutscene appends its operations here
            // and they are applied once, after the loop, in the order they were collected.
            NativeList<PendingAttachOp> pendingAttachOps = new NativeList<PendingAttachOp>(Allocator.Temp);
            try
            {
                foreach ((RefRO<CutscenePlay> _, Entity requestEntity) in
                    SystemAPI.Query<RefRO<CutscenePlay>>().WithEntityAccess())
                {
                    ProcessCutscene(entityManager, requestEntity, cameraPoseEntity, deltaTime, pendingAttachOps);
                }

                ApplyPendingAttachOps(entityManager, pendingAttachOps);
            }
            finally
            {
                pendingAttachOps.Dispose();
            }
        }

        private static void ProcessCutscene(
            EntityManager entityManager, Entity requestEntity, Entity cameraPoseEntity, float deltaTime,
            NativeList<PendingAttachOp> pendingAttachOps)
        {
            CutscenePlaybackState playbackState = entityManager.GetComponentData<CutscenePlaybackState>(requestEntity);
            if (playbackState.isComplete)
            {
                return;
            }

            CutscenePlay play = entityManager.GetComponentData<CutscenePlay>(requestEntity);
            ref CutsceneBlob blob = ref play.blob.Value;
            byte layerIndex = play.layerIndex;

            DynamicBuffer<CutsceneActorBinding> bindings = entityManager.GetBuffer<CutsceneActorBinding>(requestEntity);
            DynamicBuffer<CutsceneSlotRuntimeState> slotStates = entityManager.GetBuffer<CutsceneSlotRuntimeState>(requestEntity);
            DynamicBuffer<AnimEventOutput> eventOutput = entityManager.GetBuffer<AnimEventOutput>(requestEntity);

            CutsceneControl control = entityManager.GetComponentData<CutsceneControl>(requestEntity);

            // Speed/pause reach every bound actor's clip layer every frame, independent of hold
            // state (amendment A62 defect 4, decision A62-D4): a hold freezes only the clock, never
            // layer speed — looping clips keep cycling under it by owner call (Phase G §2).
            float effectiveLayerSpeed = control.paused ? 0f : math.max(0f, control.speed);
            if (effectiveLayerSpeed != playbackState.appliedLayerSpeed)
            {
                ApplyLayerSpeedToAllActorSlots(entityManager, ref blob, layerIndex, bindings, effectiveLayerSpeed);
                playbackState.appliedLayerSpeed = effectiveLayerSpeed;
            }

            if (control.skipRequested)
            {
                PerformSkip(
                    entityManager, ref blob, layerIndex, bindings, slotStates, ref playbackState,
                    eventOutput, requestEntity, pendingAttachOps);
                control.skipRequested = false;
                entityManager.SetComponentData(requestEntity, control);
                entityManager.SetComponentData(requestEntity, playbackState);
                ApplyCameraPose(entityManager, cameraPoseEntity, ref blob, ref playbackState);
                return;
            }

            if (playbackState.isPausedOnHold)
            {
                ref CutsceneSegmentBlob heldSegment = ref blob.segments[playbackState.segmentIndex];
                bool releasedThisFrame = false;
                if (entityManager.IsComponentEnabled<CutsceneHoldRelease>(requestEntity))
                {
                    CutsceneHoldRelease holdRelease = entityManager.GetComponentData<CutsceneHoldRelease>(requestEntity);
                    if (holdRelease.holdId == heldSegment.holdId)
                    {
                        AdvanceToNextSegment(slotStates, ref playbackState);
                        entityManager.SetComponentEnabled<CutsceneHoldRelease>(requestEntity, false);
                        releasedThisFrame = true;
                    }
                }

                if (!releasedThisFrame)
                {
                    ApplyPose(entityManager, ref blob, bindings, slotStates, ref playbackState);
                    ApplyCameraPose(entityManager, cameraPoseEntity, ref blob, ref playbackState);
                    entityManager.SetComponentData(requestEntity, playbackState);
                    return;
                }

                // Released this frame (amendment A62 defect 5): fall through to the normal path
                // with zero elapsed time instead of returning, so ProcessClipBlocks/ProcessEvents
                // still fire everything authored at the new segment's own time 0 on this exact
                // frame rather than waiting one frame for it.
                deltaTime = 0f;
            }

            if (!control.paused && effectiveLayerSpeed > 0f)
            {
                playbackState.timeInSegment += deltaTime * effectiveLayerSpeed;

                ProcessClipBlocks(entityManager, ref blob, layerIndex, effectiveLayerSpeed, bindings, slotStates, ref playbackState);
                ProcessEvents(entityManager, ref blob, ref playbackState, eventOutput, requestEntity);
                ProcessAttachMarkers(ref blob, bindings, slotStates, ref playbackState, pendingAttachOps);

                ref CutsceneSegmentBlob currentSegment = ref blob.segments[playbackState.segmentIndex];
                if (playbackState.timeInSegment >= currentSegment.duration)
                {
                    playbackState.timeInSegment = currentSegment.duration;
                    bool isFinalSegment = playbackState.segmentIndex == blob.segments.Length - 1;
                    if (isFinalSegment)
                    {
                        CompleteNaturally(entityManager, ref blob, layerIndex, bindings, ref playbackState);
                    }
                    else
                    {
                        playbackState.isPausedOnHold = true;
                    }
                }
            }

            ApplyPose(entityManager, ref blob, bindings, slotStates, ref playbackState);
            ApplyCameraPose(entityManager, cameraPoseEntity, ref blob, ref playbackState);
            entityManager.SetComponentData(requestEntity, playbackState);
        }

        // -----------------------------------------------------------------------------------
        // Binding resolution.
        // -----------------------------------------------------------------------------------

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

        // -----------------------------------------------------------------------------------
        // Clip blocks → the existing AnimationCommand API (spec §6: "no second animation pipeline").
        // -----------------------------------------------------------------------------------

        private static void ProcessClipBlocks(
            EntityManager entityManager, ref CutsceneBlob blob, byte layerIndex, float layerSpeed,
            DynamicBuffer<CutsceneActorBinding> bindings, DynamicBuffer<CutsceneSlotRuntimeState> slotStates,
            ref CutscenePlaybackState playbackState)
        {
            ref CutsceneSegmentBlob segment = ref blob.segments[playbackState.segmentIndex];
            for (int slotIndex = 0; slotIndex < blob.slots.Length; slotIndex++)
            {
                if (blob.slots[slotIndex].kind != CutsceneSlotKind.Actor)
                {
                    continue;
                }
                Entity actorEntity;
                if (!TryResolveBinding(bindings, blob.slots[slotIndex].slotId, out actorEntity) ||
                    !entityManager.HasComponent<AnimationCommand>(actorEntity))
                {
                    continue;
                }

                ref CutsceneSlotSegmentBlob slotSegment = ref segment.slotTracks[slotIndex];
                CutsceneSlotRuntimeState slotState = slotStates[slotIndex];
                DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(actorEntity);
                bool issuedAny = false;

                while (slotState.nextClipBlockIndex < slotSegment.clipBlocks.Length &&
                       slotSegment.clipBlocks[slotState.nextClipBlockIndex].start <= playbackState.timeInSegment)
                {
                    CutsceneClipBlockBlob block = slotSegment.clipBlocks[slotState.nextClipBlockIndex];

                    // The crossfade window from this block's true predecessor on the slot's flat
                    // lane (amendment A62 defect 3, decision A62-D3) — baked by CutsceneBlobBuilder,
                    // never derived here from "the previous block in this segment", which would
                    // always read 0 for the first block after a hold even when its real predecessor
                    // overlaps it.
                    commands.Add(new AnimationCommand
                    {
                        kind = CommandKind.Play,
                        layerIndex = layerIndex,
                        clip = new ClipId(block.clipId),
                        // The layer's currently-applied speed (amendment A62 defect 4), not a flat
                        // 1 — a block issued while the host has slowed or paused playback must not
                        // silently resume at normal speed.
                        speed = layerSpeed,
                        loop = block.loop ? LoopMode.Loop : LoopMode.Once,
                        blendDuration = block.blendDuration,
                        time = 0f
                    });
                    issuedAny = true;
                    slotState.nextClipBlockIndex++;
                }

                if (issuedAny)
                {
                    entityManager.SetComponentEnabled<AnimationCommandPending>(actorEntity, true);
                }
                slotStates[slotIndex] = slotState;
            }
        }

        private static void StopActorLayers(
            EntityManager entityManager, ref CutsceneBlob blob, byte layerIndex, DynamicBuffer<CutsceneActorBinding> bindings)
        {
            for (int slotIndex = 0; slotIndex < blob.slots.Length; slotIndex++)
            {
                if (blob.slots[slotIndex].kind != CutsceneSlotKind.Actor)
                {
                    continue;
                }
                Entity actorEntity;
                if (!TryResolveBinding(bindings, blob.slots[slotIndex].slotId, out actorEntity) ||
                    !entityManager.HasComponent<AnimationCommand>(actorEntity))
                {
                    continue;
                }

                DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(actorEntity);
                commands.Add(new AnimationCommand
                {
                    kind = CommandKind.Stop,
                    layerIndex = layerIndex,
                    clip = default,
                    speed = 0f,
                    loop = LoopMode.UseClipDefault,
                    blendDuration = 0f,
                    time = 0f
                });
                entityManager.SetComponentEnabled<AnimationCommandPending>(actorEntity, true);
            }
        }

        /// <summary>
        /// Issues <c>SetSpeed</c> to every bound Actor slot's clip layer (amendment A62 defect 4).
        /// Not gated on the layer being active — a block issued later on a currently-idle layer
        /// must still inherit the speed already in effect, not the command API's own speed-1 default.
        /// </summary>
        private static void ApplyLayerSpeedToAllActorSlots(
            EntityManager entityManager, ref CutsceneBlob blob, byte layerIndex,
            DynamicBuffer<CutsceneActorBinding> bindings, float layerSpeed)
        {
            for (int slotIndex = 0; slotIndex < blob.slots.Length; slotIndex++)
            {
                if (blob.slots[slotIndex].kind != CutsceneSlotKind.Actor)
                {
                    continue;
                }
                Entity actorEntity;
                if (!TryResolveBinding(bindings, blob.slots[slotIndex].slotId, out actorEntity) ||
                    !entityManager.HasComponent<AnimationCommand>(actorEntity))
                {
                    continue;
                }

                DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(actorEntity);
                commands.Add(new AnimationCommand
                {
                    kind = CommandKind.SetSpeed,
                    layerIndex = layerIndex,
                    clip = default,
                    speed = layerSpeed,
                    loop = LoopMode.UseClipDefault,
                    blendDuration = float.NaN,
                    time = 0f
                });
                entityManager.SetComponentEnabled<AnimationCommandPending>(actorEntity, true);
            }
        }

        // -----------------------------------------------------------------------------------
        // Events → the same AnimEventOutput shape a clip's own events use (spec §6), on the
        // cutscene request entity itself rather than any one bound actor.
        // -----------------------------------------------------------------------------------

        private static void ProcessEvents(
            EntityManager entityManager, ref CutsceneBlob blob, ref CutscenePlaybackState playbackState,
            DynamicBuffer<AnimEventOutput> eventOutput, Entity requestEntity)
        {
            ref CutsceneSegmentBlob segment = ref blob.segments[playbackState.segmentIndex];
            bool firedAny = false;
            while (playbackState.nextEventIndex < segment.events.Length &&
                   segment.events[playbackState.nextEventIndex].time <= playbackState.timeInSegment)
            {
                CutsceneEventMarkerBlob eventMarker = segment.events[playbackState.nextEventIndex];
                eventOutput.Add(new AnimEventOutput
                {
                    eventKey = eventMarker.eventKey,
                    layerIndex = 0,
                    clip = default,
                    intParam = eventMarker.intParam,
                    floatParam = eventMarker.floatParam
                });
                firedAny = true;
                playbackState.nextEventIndex++;
            }
            if (firedAny)
            {
                entityManager.SetComponentEnabled<AnimEventsPending>(requestEntity, true);
            }
        }

        // -----------------------------------------------------------------------------------
        // Segment advance, completion, and skip.
        // -----------------------------------------------------------------------------------

        private static void AdvanceToNextSegment(
            DynamicBuffer<CutsceneSlotRuntimeState> slotStates, ref CutscenePlaybackState playbackState)
        {
            playbackState.segmentIndex++;
            playbackState.timeInSegment = 0f;
            playbackState.isPausedOnHold = false;
            playbackState.nextEventIndex = 0;
            for (int i = 0; i < slotStates.Length; i++)
            {
                // Cursors rebase onto the new segment's own arrays; the attachment fields do not
                // reset — a rider that boarded before a hold is still aboard after it (§3.3's
                // "attachments are left in place").
                CutsceneSlotRuntimeState slotState = slotStates[i];
                slotState.nextClipBlockIndex = 0;
                slotState.nextAttachMarkerIndex = 0;
                slotStates[i] = slotState;
            }
        }

        private static void CompleteNaturally(
            EntityManager entityManager, ref CutsceneBlob blob, byte layerIndex,
            DynamicBuffer<CutsceneActorBinding> bindings, ref CutscenePlaybackState playbackState)
        {
            StopActorLayers(entityManager, ref blob, layerIndex, bindings);
            playbackState.isComplete = true;
        }

        /// <summary>
        /// Jumps straight to the cutscene's final instant (spec §4). Reaches the exact same
        /// <c>(segmentIndex, timeInSegment)</c> — and therefore the exact same sampled pose — a full
        /// play-through eventually settles on, which is what makes skipped and watched end states
        /// identical rather than merely close.
        /// </summary>
        private static void PerformSkip(
            EntityManager entityManager, ref CutsceneBlob blob, byte layerIndex,
            DynamicBuffer<CutsceneActorBinding> bindings, DynamicBuffer<CutsceneSlotRuntimeState> slotStates,
            ref CutscenePlaybackState playbackState, DynamicBuffer<AnimEventOutput> eventOutput,
            Entity requestEntity, NativeList<PendingAttachOp> pendingAttachOps)
        {
            bool firedAny = false;
            for (int segmentIndex = playbackState.segmentIndex; segmentIndex < blob.segments.Length; segmentIndex++)
            {
                ref CutsceneSegmentBlob segment = ref blob.segments[segmentIndex];
                int startEventIndex = segmentIndex == playbackState.segmentIndex ? playbackState.nextEventIndex : 0;
                for (int eventIndex = startEventIndex; eventIndex < segment.events.Length; eventIndex++)
                {
                    CutsceneEventMarkerBlob eventMarker = segment.events[eventIndex];
                    if (!eventMarker.fireOnSkip)
                    {
                        continue;
                    }
                    eventOutput.Add(new AnimEventOutput
                    {
                        eventKey = eventMarker.eventKey,
                        layerIndex = 0,
                        clip = default,
                        intParam = eventMarker.intParam,
                        floatParam = eventMarker.floatParam
                    });
                    firedAny = true;
                }
            }
            if (firedAny)
            {
                entityManager.SetComponentEnabled<AnimEventsPending>(requestEntity, true);
            }

            // Every remaining attach marker applies, in order, so a skipped run and a watched one
            // leave the same world (decision A63-D3) — including the detach signals a host may have
            // been waiting on.
            SkipAttachMarkers(ref blob, bindings, slotStates, ref playbackState, pendingAttachOps);

            playbackState.segmentIndex = blob.segments.Length - 1;
            ref CutsceneSegmentBlob finalSegment = ref blob.segments[playbackState.segmentIndex];
            playbackState.timeInSegment = finalSegment.duration;
            playbackState.isPausedOnHold = false;
            playbackState.nextEventIndex = finalSegment.events.Length;
            for (int slotIndex = 0; slotIndex < slotStates.Length; slotIndex++)
            {
                CutsceneSlotRuntimeState slotState = slotStates[slotIndex];
                slotState.nextAttachMarkerIndex = finalSegment.slotTracks[slotIndex].attachMarkers.Length;
                slotStates[slotIndex] = slotState;
            }

            ApplyPose(entityManager, ref blob, bindings, slotStates, ref playbackState);
            CompleteNaturally(entityManager, ref blob, layerIndex, bindings, ref playbackState);
        }

        // -----------------------------------------------------------------------------------
        // Attach lane (amendment A63). Collected here, applied after the query loop: every one of
        // these operations is a structural change, which SystemAPI.Query forbids mid-iteration.
        // -----------------------------------------------------------------------------------

        private struct PendingAttachOp
        {
            public CutsceneAttachKind kind;
            public Entity entity;
            public Entity host;
            public uint socketId;
            public float3 localOffset;
            public quaternion localRotation;
            public bool hide;
            public float3 detachImpulse;
        }

        /// <summary>
        /// Walks each slot's attach cursor up to the playhead, updating the slot's own bookkeeping
        /// immediately (so <see cref="ApplyPose"/> already suppresses an attached root this frame)
        /// and queuing the structural half for after the loop.
        /// </summary>
        private static void ProcessAttachMarkers(
            ref CutsceneBlob blob, DynamicBuffer<CutsceneActorBinding> bindings,
            DynamicBuffer<CutsceneSlotRuntimeState> slotStates, ref CutscenePlaybackState playbackState,
            NativeList<PendingAttachOp> pendingAttachOps)
        {
            ref CutsceneSegmentBlob segment = ref blob.segments[playbackState.segmentIndex];
            for (int slotIndex = 0; slotIndex < blob.slots.Length; slotIndex++)
            {
                ref CutsceneSlotSegmentBlob slotSegment = ref segment.slotTracks[slotIndex];
                CutsceneSlotRuntimeState slotState = slotStates[slotIndex];
                while (slotState.nextAttachMarkerIndex < slotSegment.attachMarkers.Length &&
                       slotSegment.attachMarkers[slotState.nextAttachMarkerIndex].time <= playbackState.timeInSegment)
                {
                    ApplyMarkerToSlotState(
                        ref blob, ref slotSegment.attachMarkers[slotState.nextAttachMarkerIndex],
                        slotIndex, bindings, ref slotState, pendingAttachOps);
                    slotState.nextAttachMarkerIndex++;
                }
                slotStates[slotIndex] = slotState;
            }
        }

        /// <summary>Decision A63-D3: a skip replays every marker it jumped over, in order.</summary>
        private static void SkipAttachMarkers(
            ref CutsceneBlob blob, DynamicBuffer<CutsceneActorBinding> bindings,
            DynamicBuffer<CutsceneSlotRuntimeState> slotStates, ref CutscenePlaybackState playbackState,
            NativeList<PendingAttachOp> pendingAttachOps)
        {
            for (int segmentIndex = playbackState.segmentIndex; segmentIndex < blob.segments.Length; segmentIndex++)
            {
                ref CutsceneSegmentBlob segment = ref blob.segments[segmentIndex];
                for (int slotIndex = 0; slotIndex < blob.slots.Length; slotIndex++)
                {
                    ref CutsceneSlotSegmentBlob slotSegment = ref segment.slotTracks[slotIndex];
                    CutsceneSlotRuntimeState slotState = slotStates[slotIndex];
                    int startMarkerIndex =
                        segmentIndex == playbackState.segmentIndex ? slotState.nextAttachMarkerIndex : 0;
                    for (int markerIndex = startMarkerIndex; markerIndex < slotSegment.attachMarkers.Length; markerIndex++)
                    {
                        ApplyMarkerToSlotState(
                            ref blob, ref slotSegment.attachMarkers[markerIndex], slotIndex, bindings,
                            ref slotState, pendingAttachOps);
                    }
                    slotStates[slotIndex] = slotState;
                }
            }
        }

        /// <summary>
        /// One marker's effect on its slot: the bookkeeping now, the structural work queued. An
        /// Attach on an already-attached slot is a hand-over — silent, with no signal and no impulse
        /// — because the queued op removes whichever mechanism the slot was using before adding the
        /// new one.
        /// </summary>
        private static void ApplyMarkerToSlotState(
            ref CutsceneBlob blob, ref CutsceneAttachMarkerBlob marker, int slotIndex,
            DynamicBuffer<CutsceneActorBinding> bindings, ref CutsceneSlotRuntimeState slotState,
            NativeList<PendingAttachOp> pendingAttachOps)
        {
            Entity boundEntity;
            if (!TryResolveBinding(bindings, blob.slots[slotIndex].slotId, out boundEntity))
            {
                return;
            }

            if (marker.kind == CutsceneAttachKind.Attach)
            {
                Entity hostEntity;
                if (marker.hostSlotIndex < 0 || marker.hostSlotIndex >= blob.slots.Length ||
                    !TryResolveBinding(bindings, blob.slots[marker.hostSlotIndex].slotId, out hostEntity))
                {
                    // Warned at bake (§3.2); silently skipped here, rule T2's shape.
                    return;
                }

                pendingAttachOps.Add(new PendingAttachOp
                {
                    kind = CutsceneAttachKind.Attach,
                    entity = boundEntity,
                    host = hostEntity,
                    socketId = marker.socketId,
                    localOffset = marker.localOffset,
                    localRotation = marker.localRotation,
                    hide = marker.hideWhileAttached
                });

                slotState.attachedHostSlotIndex = marker.hostSlotIndex;
                slotState.attachedSocketId = marker.socketId;
                slotState.isHiddenByAttachment = marker.hideWhileAttached;
                return;
            }

            if (slotState.attachedHostSlotIndex < 0)
            {
                // Nothing to release. Not an error: a cutscene may author a defensive Detach.
                return;
            }

            Entity previousHostEntity;
            TryResolveBinding(bindings, blob.slots[slotState.attachedHostSlotIndex].slotId, out previousHostEntity);
            pendingAttachOps.Add(new PendingAttachOp
            {
                kind = CutsceneAttachKind.Detach,
                entity = boundEntity,
                host = previousHostEntity,
                socketId = slotState.attachedSocketId,
                detachImpulse = marker.detachImpulse
            });

            slotState.attachedHostSlotIndex = -1;
            slotState.attachedSocketId = 0u;
            slotState.isHiddenByAttachment = false;
        }

        private static void ApplyPendingAttachOps(
            EntityManager entityManager, NativeList<PendingAttachOp> pendingAttachOps)
        {
            for (int opIndex = 0; opIndex < pendingAttachOps.Length; opIndex++)
            {
                PendingAttachOp op = pendingAttachOps[opIndex];
                if (!entityManager.Exists(op.entity))
                {
                    continue;
                }

                if (op.kind == CutsceneAttachKind.Attach)
                {
                    ApplyAttach(entityManager, op);
                }
                else
                {
                    ApplyDetach(entityManager, op);
                }
            }
        }

        private static void ApplyAttach(EntityManager entityManager, in PendingAttachOp op)
        {
            // A socket attach needs the host to expose sockets at all; without a SocketRegistry
            // nothing would ever write the transform, so the attachment falls back to the host root
            // rather than freezing the prop wherever it happened to be standing.
            bool useSocket = op.socketId != 0u && entityManager.HasComponent<SocketRegistry>(op.host);

            // Both mechanisms are cleared first, always: Parent and SocketAttachment on one entity
            // transform it twice (SocketAttachment's own remark), and a hand-over routinely swaps
            // one for the other.
            if (entityManager.HasComponent<Parent>(op.entity))
            {
                entityManager.RemoveComponent<Parent>(op.entity);
            }
            if (entityManager.HasComponent<SocketAttachment>(op.entity))
            {
                entityManager.RemoveComponent<SocketAttachment>(op.entity);
            }

            if (useSocket)
            {
                entityManager.AddComponentData(op.entity, new SocketAttachment
                {
                    actorRoot = op.host,
                    socketId = op.socketId,
                    localOffset = op.localOffset
                });
            }
            else
            {
                entityManager.AddComponentData(op.entity, new Parent { Value = op.host });
                if (entityManager.HasComponent<LocalTransform>(op.entity))
                {
                    LocalTransform localTransform = entityManager.GetComponentData<LocalTransform>(op.entity);
                    localTransform.Position = op.localOffset;
                    localTransform.Rotation = op.localRotation;
                    entityManager.SetComponentData(op.entity, localTransform);
                }
            }

            SetRenderingDisabled(entityManager, op.entity, op.hide);
        }

        private static void ApplyDetach(EntityManager entityManager, in PendingAttachOp op)
        {
            bool wasParented = entityManager.HasComponent<Parent>(op.entity);
            quaternion hostRotation = quaternion.identity;
            LocalTransform hostTransform = LocalTransform.Identity;
            bool hasHostTransform = op.host != Entity.Null
                && entityManager.Exists(op.host)
                && entityManager.HasComponent<LocalTransform>(op.host);
            if (hasHostTransform)
            {
                hostTransform = entityManager.GetComponentData<LocalTransform>(op.host);
                hostRotation = hostTransform.Rotation;
            }

            if (wasParented)
            {
                // A parented entity's LocalTransform is host-relative, so the world pose it must
                // keep is the composition. A socket attachment's already is world — SocketResolveSystem
                // writes it there — so that case needs no rewrite at all.
                if (hasHostTransform && entityManager.HasComponent<LocalTransform>(op.entity))
                {
                    LocalTransform localTransform = entityManager.GetComponentData<LocalTransform>(op.entity);
                    entityManager.RemoveComponent<Parent>(op.entity);
                    entityManager.SetComponentData(op.entity, hostTransform.TransformTransform(localTransform));
                }
                else
                {
                    entityManager.RemoveComponent<Parent>(op.entity);
                }
            }

            if (entityManager.HasComponent<SocketAttachment>(op.entity))
            {
                entityManager.RemoveComponent<SocketAttachment>(op.entity);
            }

            SetRenderingDisabled(entityManager, op.entity, false);

            if (!entityManager.HasComponent<CutsceneDetachSignal>(op.entity))
            {
                entityManager.AddComponent<CutsceneDetachSignal>(op.entity);
            }
            entityManager.SetComponentData(op.entity, new CutsceneDetachSignal
            {
                worldImpulse = math.rotate(hostRotation, op.detachImpulse),
                previousHost = op.host
            });
            entityManager.SetComponentEnabled<CutsceneDetachSignal>(op.entity, true);
        }

        /// <summary>
        /// Hides or reveals an attached entity and every rendering member of its linked group
        /// (decision A63-D4: <c>DisableRendering</c>, never <c>AnimVisible</c>, which a host's own
        /// visibility system rewrites every frame). The member list is read fresh each time — a
        /// spawned actor's <c>LinkedEntityGroup</c> is rebuilt by its host's spawn-init (§6).
        /// </summary>
        private static void SetRenderingDisabled(EntityManager entityManager, Entity entity, bool disable)
        {
            SetOneEntityRenderingDisabled(entityManager, entity, disable);
            if (!entityManager.HasBuffer<LinkedEntityGroup>(entity))
            {
                return;
            }

            DynamicBuffer<LinkedEntityGroup> linkedGroup = entityManager.GetBuffer<LinkedEntityGroup>(entity);
            NativeArray<LinkedEntityGroup> members = linkedGroup.ToNativeArray(Allocator.Temp);
            try
            {
                for (int memberIndex = 0; memberIndex < members.Length; memberIndex++)
                {
                    SetOneEntityRenderingDisabled(entityManager, members[memberIndex].Value, disable);
                }
            }
            finally
            {
                members.Dispose();
            }
        }

        private static void SetOneEntityRenderingDisabled(EntityManager entityManager, Entity entity, bool disable)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity)
                || !entityManager.HasComponent<MaterialMeshInfo>(entity))
            {
                return;
            }

            bool isDisabled = entityManager.HasComponent<DisableRendering>(entity);
            if (disable && !isDisabled)
            {
                entityManager.AddComponent<DisableRendering>(entity);
            }
            else if (!disable && isDisabled)
            {
                entityManager.RemoveComponent<DisableRendering>(entity);
            }
        }

        // -----------------------------------------------------------------------------------
        // Root/prop transform and camera output.
        // -----------------------------------------------------------------------------------

        private static void ApplyPose(
            EntityManager entityManager, ref CutsceneBlob blob,
            DynamicBuffer<CutsceneActorBinding> bindings, DynamicBuffer<CutsceneSlotRuntimeState> slotStates,
            ref CutscenePlaybackState playbackState)
        {
            ref CutsceneSegmentBlob segment = ref blob.segments[playbackState.segmentIndex];
            for (int slotIndex = 0; slotIndex < blob.slots.Length; slotIndex++)
            {
                // An attached slot's transform belongs to its host (§3.1) — SocketResolveSystem or
                // Unity's own parent hierarchy writes it, and a root key written here would fight
                // that every frame.
                if (slotStates[slotIndex].attachedHostSlotIndex >= 0)
                {
                    continue;
                }

                Entity boundEntity;
                if (!TryResolveBinding(bindings, blob.slots[slotIndex].slotId, out boundEntity) ||
                    !entityManager.HasComponent<LocalTransform>(boundEntity))
                {
                    continue;
                }

                ref CutsceneSlotSegmentBlob slotSegment = ref segment.slotTracks[slotIndex];
                float3 position;
                float3 rotationEuler;
                float3 scale;
                if (!CutsceneBlobSampler.TrySampleTransform(
                    ref slotSegment.transformKeys, playbackState.timeInSegment, out position, out rotationEuler, out scale))
                {
                    // No root keys authored for this slot (amendment A62 defect 2): leave the bound
                    // entity's transform exactly as it is rather than snapping it to the world origin.
                    continue;
                }

                LocalTransform localTransform = entityManager.GetComponentData<LocalTransform>(boundEntity);
                localTransform.Position = position;
                // Euler → quaternion at the last possible step, matching TransformApplySystem's own
                // quaternion.Euler(pose.rotation) conversion.
                localTransform.Rotation = quaternion.Euler(rotationEuler);
                // A root/prop carries a single uniform LocalTransform.Scale, unlike a part's
                // PostTransformMatrix (TransformApplySystem's own split). A non-uniform authored
                // scale on a root is unusual but legal data; its largest axis wins rather than the
                // value being silently dropped.
                localTransform.Scale = math.cmax(math.abs(scale));
                entityManager.SetComponentData(boundEntity, localTransform);
            }
        }

        private static void ApplyCameraPose(
            EntityManager entityManager, Entity cameraPoseEntity, ref CutsceneBlob blob, ref CutscenePlaybackState playbackState)
        {
            ref CutsceneSegmentBlob segment = ref blob.segments[playbackState.segmentIndex];
            if (segment.cameraKeys.Length == 0)
            {
                return;
            }

            float3 position;
            quaternion rotation;
            float fieldOfView;
            bool isCut;
            CutsceneBlobSampler.SampleCamera(
                ref segment.cameraKeys, ref segment.cameraCutTimes, playbackState.timeInSegment,
                out position, out rotation, out fieldOfView, out isCut);

            entityManager.SetComponentData(cameraPoseEntity, new CutsceneCameraPose
            {
                position = position,
                rotation = rotation,
                fieldOfView = fieldOfView,
                isCut = isCut,
                // False once the cutscene has completed (amendment A62 defect 6) even though a
                // pose is still written here — a host's exit transition must fire exactly once, not
                // keep re-triggering on a stale-but-still-"driven" pose every frame after the end.
                isDriven = !playbackState.isComplete
            });
        }
    }
}
