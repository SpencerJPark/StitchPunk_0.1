// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs in <see cref="AnimationToolkitLogicSystemGroup"/>: advances every running cutscene's
    /// elastic clock, issues clip-block Play commands by animation name against each bound actor's
    /// own <see cref="ActorProfile"/>, drives auto locomotion, writes facing (<see cref="CutsceneFacing"/>
    /// and, for a bound Actor slot, <see cref="ActorFacing"/>), writes root/prop transforms and the
    /// camera singleton, fires events, and handles hold-pause/release and skip. Part-track overrides
    /// are not applied here — they need to land between <c>TransformSampleSystem</c> and
    /// <c>TransformApplySystem</c> in the Presentation group; see <see cref="CutscenePartOverrideSystem"/>.
    /// </summary>
    // A facing this system writes this frame must be re-picked this frame, before
    // ActorFacingRepickSystem runs — the Play commands appended here still drain next frame, through
    // CommandApplySystem's own OrderFirst latency, exactly as they always have.
    [UpdateInGroup(typeof(AnimationToolkitLogicSystemGroup))]
    [UpdateBefore(typeof(ActorFacingRepickSystem))]
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

            // Cleared every frame so a segment with no camera lane, or a cutscene that just
            // completed, reads as "not driven" rather than holding the last frame's flag along
            // with its stale pose.
            CutsceneCameraPose cameraPose = entityManager.GetComponentData<CutsceneCameraPose>(cameraPoseEntity);
            cameraPose.isDriven = false;
            entityManager.SetComponentData(cameraPoseEntity, cameraPose);

            float deltaTime = SystemAPI.Time.DeltaTime;

            // Structural changes are illegal inside a SystemAPI.Query loop, and an attach is nothing
            // but structural changes. Every cutscene appends its operations here and they are
            // applied once, after the loop, in the order they were collected.
            NativeList<PendingAttachOp> pendingAttachOps = new NativeList<PendingAttachOp>(Allocator.Temp);
            NativeList<PendingMarkOp> pendingMarkOps = new NativeList<PendingMarkOp>(Allocator.Temp);
            NativeList<PendingFacingOp> pendingFacingOps = new NativeList<PendingFacingOp>(Allocator.Temp);
            try
            {
                foreach ((RefRO<CutscenePlay> _, Entity requestEntity) in
                    SystemAPI.Query<RefRO<CutscenePlay>>().WithEntityAccess())
                {
                    ProcessCutscene(
                        entityManager, requestEntity, cameraPoseEntity, deltaTime,
                        pendingAttachOps, pendingMarkOps, pendingFacingOps);
                }

                ApplyPendingAttachOps(entityManager, pendingAttachOps);
                ApplyPendingMarkOps(entityManager, pendingMarkOps);
                ApplyPendingFacingOps(entityManager, pendingFacingOps);
            }
            finally
            {
                pendingAttachOps.Dispose();
                pendingMarkOps.Dispose();
                pendingFacingOps.Dispose();
            }
        }

        private static void ProcessCutscene(
            EntityManager entityManager, Entity requestEntity, Entity cameraPoseEntity, float deltaTime,
            NativeList<PendingAttachOp> pendingAttachOps, NativeList<PendingMarkOp> pendingMarkOps,
            NativeList<PendingFacingOp> pendingFacingOps)
        {
            CutscenePlaybackState playbackState = entityManager.GetComponentData<CutscenePlaybackState>(requestEntity);
            if (playbackState.isComplete)
            {
                return;
            }

            CutscenePlay play = entityManager.GetComponentData<CutscenePlay>(requestEntity);
            ref CutsceneBlob blob = ref play.blob.Value;

            DynamicBuffer<CutsceneActorBinding> bindings = entityManager.GetBuffer<CutsceneActorBinding>(requestEntity);
            DynamicBuffer<CutsceneSlotRuntimeState> slotStates = entityManager.GetBuffer<CutsceneSlotRuntimeState>(requestEntity);
            DynamicBuffer<CutsceneSlotLayerState> layerStates = entityManager.GetBuffer<CutsceneSlotLayerState>(requestEntity);
            DynamicBuffer<AnimEventOutput> eventOutput = entityManager.GetBuffer<AnimEventOutput>(requestEntity);

            CutsceneControl control = entityManager.GetComponentData<CutsceneControl>(requestEntity);

            // Speed/pause reach every bound actor's clip layer every frame, independent of hold
            // state: a hold freezes only the clock, never layer speed — looping clips keep cycling
            // under it by owner call.
            float effectiveLayerSpeed = control.paused ? 0f : math.max(0f, control.speed);
            if (effectiveLayerSpeed != playbackState.appliedLayerSpeed)
            {
                ApplyLayerSpeedToAllActorSlots(entityManager, ref blob, bindings, layerStates, effectiveLayerSpeed);
                playbackState.appliedLayerSpeed = effectiveLayerSpeed;
            }

            if (control.skipRequested)
            {
                PerformSkip(
                    entityManager, ref blob, bindings, slotStates, layerStates, ref playbackState,
                    eventOutput, requestEntity, pendingAttachOps);
                control.skipRequested = false;
                entityManager.SetComponentData(requestEntity, control);
                entityManager.SetComponentData(requestEntity, playbackState);
                ApplyCameraPose(entityManager, cameraPoseEntity, ref blob, ref playbackState);
                return;
            }

            // Arrival and timeout are judged every frame, including while the clock is stopped - a
            // rendezvous hold exists precisely to be resolved by movement happening while nothing
            // else advances.
            ResolveOutstandingMarks(entityManager, ref blob, bindings, slotStates, deltaTime, control.paused);

            if (playbackState.isPausedOnHold)
            {
                ref CutsceneSegmentBlob heldSegment = ref blob.segments[playbackState.segmentIndex];
                bool releasedThisFrame = false;
                if (heldSegment.autoReleaseWhenMarksReached && !AnySlotHasAnOutstandingMark(slotStates))
                {
                    AdvanceToNextSegment(slotStates, ref playbackState);
                    releasedThisFrame = true;
                }
                else if (entityManager.IsComponentEnabled<CutsceneHoldRelease>(requestEntity))
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
                    // A held clock still faces somewhere, and a rendezvous hold is exactly when an
                    // actor is walking: facing must keep resolving while the timeline does not.
                    ProcessFacing(entityManager, ref blob, bindings, slotStates, ref playbackState, pendingFacingOps);
                    ApplyPose(entityManager, ref blob, bindings, slotStates, ref playbackState);
                    if (!control.paused)
                    {
                        ProcessLocomotion(entityManager, ref blob, deltaTime, bindings, slotStates, layerStates);
                    }
                    ApplyCameraPose(entityManager, cameraPoseEntity, ref blob, ref playbackState);
                    entityManager.SetComponentData(requestEntity, playbackState);
                    return;
                }

                // Released this frame: fall through to the normal path with zero elapsed time
                // instead of returning, so ProcessClipBlocks/ProcessEvents still fire everything
                // authored at the new segment's own time 0 on this exact frame rather than waiting
                // one frame for it.
                deltaTime = 0f;
            }

            if (!control.paused && effectiveLayerSpeed > 0f)
            {
                playbackState.timeInSegment += deltaTime * effectiveLayerSpeed;

                ProcessClipBlocks(entityManager, ref blob, effectiveLayerSpeed, bindings, slotStates, layerStates, ref playbackState);
                ProcessLayerStops(entityManager, ref blob, bindings, slotStates, layerStates, ref playbackState);
                ProcessEvents(entityManager, ref blob, ref playbackState, eventOutput, requestEntity);
                ProcessAttachMarkers(ref blob, bindings, slotStates, ref playbackState, pendingAttachOps);
                ProcessMarks(ref blob, bindings, slotStates, ref playbackState, pendingMarkOps);

                ref CutsceneSegmentBlob currentSegment = ref blob.segments[playbackState.segmentIndex];
                if (playbackState.timeInSegment >= currentSegment.duration)
                {
                    playbackState.timeInSegment = currentSegment.duration;
                    bool isFinalSegment = playbackState.segmentIndex == blob.segments.Length - 1;
                    if (isFinalSegment)
                    {
                        CompleteNaturally(entityManager, ref blob, bindings, slotStates, layerStates, ref playbackState);
                    }
                    else
                    {
                        playbackState.isPausedOnHold = true;
                    }
                }
            }

            ProcessFacing(entityManager, ref blob, bindings, slotStates, ref playbackState, pendingFacingOps);
            ApplyPose(entityManager, ref blob, bindings, slotStates, ref playbackState);
            if (!control.paused)
            {
                ProcessLocomotion(entityManager, ref blob, deltaTime, bindings, slotStates, layerStates);
            }
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
        // Clip blocks play by name against the bound actor's own ActorProfile — no raw layer index,
        // no second animation pipeline.
        // -----------------------------------------------------------------------------------

        private static void ProcessClipBlocks(
            EntityManager entityManager, ref CutsceneBlob blob, float layerSpeed,
            DynamicBuffer<CutsceneActorBinding> bindings, DynamicBuffer<CutsceneSlotRuntimeState> slotStates,
            DynamicBuffer<CutsceneSlotLayerState> layerStates, ref CutscenePlaybackState playbackState)
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

                BlobAssetReference<ActorProfileBlob> profileReference = default;
                bool hasProfile = entityManager.HasComponent<ActorProfile>(actorEntity);
                if (hasProfile)
                {
                    profileReference = entityManager.GetComponentData<ActorProfile>(actorEntity).Value;
                }

                if (!hasProfile || !profileReference.IsCreated)
                {
                    bool dueThisFrame = slotState.nextClipBlockIndex < slotSegment.clipBlocks.Length
                        && slotSegment.clipBlocks[slotState.nextClipBlockIndex].start <= playbackState.timeInSegment;
                    if (dueThisFrame && !slotState.warnedMissingProfile)
                    {
                        UnityEngine.Debug.LogWarning(
                            "[DOTS Animation Toolkit] Cutscene slot " + slotIndex + " is bound to an actor "
                            + "with no ActorProfile; its clip blocks are skipped for the rest of this run.");
                        slotState.warnedMissingProfile = true;
                    }
                    // Advance the cursor past every block due this frame anyway, so a profile-less
                    // actor does not re-enter this branch every frame for the same stale block.
                    while (slotState.nextClipBlockIndex < slotSegment.clipBlocks.Length &&
                           slotSegment.clipBlocks[slotState.nextClipBlockIndex].start <= playbackState.timeInSegment)
                    {
                        slotState.nextClipBlockIndex++;
                    }
                    slotStates[slotIndex] = slotState;
                    continue;
                }

                ref ActorProfileBlob profileBlob = ref profileReference.Value;
                DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(actorEntity);
                bool issuedAny = false;

                while (slotState.nextClipBlockIndex < slotSegment.clipBlocks.Length &&
                       slotSegment.clipBlocks[slotState.nextClipBlockIndex].start <= playbackState.timeInSegment)
                {
                    int blockIndex = slotState.nextClipBlockIndex;
                    CutsceneClipBlockBlob block = slotSegment.clipBlocks[blockIndex];
                    slotState.nextClipBlockIndex++;

                    int animationIndex;
                    if (!ActorProfileApi.TryFindAnimation(ref profileBlob, block.animationKey, out animationIndex))
                    {
                        if (!slotState.warnedUnresolvedAnimationKey)
                        {
                            UnityEngine.Debug.LogWarning(
                                "[DOTS Animation Toolkit] Cutscene slot " + slotIndex + " block names animation "
                                + "key 0x" + block.animationKey.ToString("X8") + ", which the bound actor's "
                                + "profile does not declare. Skipped (further unresolved keys on this slot are "
                                + "not reported again).");
                            slotState.warnedUnresolvedAnimationKey = true;
                        }
                        continue;
                    }

                    ref ActorAnimationBlob entry = ref profileBlob.animations[animationIndex];
                    byte entryLayerIndex = entry.layerIndex;

                    commands.Add(new AnimationCommand
                    {
                        kind = CommandKind.PlayAnimation,
                        animationKey = block.animationKey,
                        // The layer's currently-applied speed times the block's own times the
                        // profile entry's own — a block issued while the host has slowed or paused
                        // playback must not silently resume at normal speed.
                        speed = layerSpeed * CutsceneBlockTiming.EffectiveBlockSpeed(block.speed) * entry.speed,
                        loop = block.loop,
                        blendDuration = block.blendDuration,
                        time = 0f
                    });

                    // Play always starts a clip at 0 (or its end, in reverse) — CommandApplySystem
                    // ignores the command's own time — so an offset is a second command, drained
                    // right after it in the same frame, addressed by the entry's own layer.
                    if (block.clipStartOffset > 0f)
                    {
                        commands.Add(new AnimationCommand
                        {
                            kind = CommandKind.SetTime,
                            layerIndex = entryLayerIndex,
                            clip = default,
                            speed = 0f,
                            loop = LoopMode.UseClipDefault,
                            blendDuration = float.NaN,
                            time = block.clipStartOffset
                        });
                    }
                    issuedAny = true;

                    int layerStateIndex = slotIndex * CutsceneApi.LayersPerSlot + entryLayerIndex;
                    if (layerStateIndex < layerStates.Length)
                    {
                        layerStates[layerStateIndex] = new CutsceneSlotLayerState
                        {
                            activeBlockSegmentIndex = playbackState.segmentIndex,
                            activeBlockIndex = blockIndex,
                            activeBlockSpeed = CutsceneBlockTiming.EffectiveBlockSpeed(block.speed)
                        };
                    }
                }

                if (issuedAny)
                {
                    entityManager.SetComponentEnabled<AnimationCommandPending>(actorEntity, true);
                }
                slotStates[slotIndex] = slotState;
            }
        }

        // -----------------------------------------------------------------------------------
        // Layer stops: hand a profile layer back to auto locomotion (or silence, with no
        // locomotion configured) from the stop's own time.
        // -----------------------------------------------------------------------------------

        private static void ProcessLayerStops(
            EntityManager entityManager, ref CutsceneBlob blob, DynamicBuffer<CutsceneActorBinding> bindings,
            DynamicBuffer<CutsceneSlotRuntimeState> slotStates, DynamicBuffer<CutsceneSlotLayerState> layerStates,
            ref CutscenePlaybackState playbackState)
        {
            ref CutsceneSegmentBlob segment = ref blob.segments[playbackState.segmentIndex];
            for (int slotIndex = 0; slotIndex < blob.slots.Length; slotIndex++)
            {
                ref CutsceneSlotMetaBlob slotMeta = ref blob.slots[slotIndex];
                if (slotMeta.kind != CutsceneSlotKind.Actor)
                {
                    continue;
                }
                ref CutsceneSlotSegmentBlob slotSegment = ref segment.slotTracks[slotIndex];
                if (slotSegment.layerStops.Length == 0)
                {
                    continue;
                }

                CutsceneSlotRuntimeState slotState = slotStates[slotIndex];
                Entity actorEntity;
                bool hasBinding = TryResolveBinding(bindings, slotMeta.slotId, out actorEntity);
                byte locomotionLayerIndex = 0;
                bool hasLocomotion = hasBinding
                    && TryResolveLocomotionLayerIndex(entityManager, in slotMeta, actorEntity, out locomotionLayerIndex);

                while (slotState.nextLayerStopIndex < slotSegment.layerStops.Length &&
                       slotSegment.layerStops[slotState.nextLayerStopIndex].time <= playbackState.timeInSegment)
                {
                    CutsceneLayerStopBlob stop = slotSegment.layerStops[slotState.nextLayerStopIndex];
                    slotState.nextLayerStopIndex++;

                    int layerStateIndex = slotIndex * CutsceneApi.LayersPerSlot + stop.layerIndex;
                    if (layerStateIndex < layerStates.Length)
                    {
                        CutsceneSlotLayerState layerState = layerStates[layerStateIndex];
                        layerState.activeBlockSegmentIndex = -1;
                        layerStates[layerStateIndex] = layerState;
                    }

                    if (hasLocomotion && locomotionLayerIndex == stop.layerIndex)
                    {
                        slotState.lastLocomotionKey = 0u;
                    }

                    if (hasBinding && entityManager.HasComponent<AnimationCommand>(actorEntity))
                    {
                        DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(actorEntity);
                        commands.Add(new AnimationCommand
                        {
                            kind = CommandKind.Stop,
                            layerIndex = stop.layerIndex,
                            clip = default,
                            speed = 0f,
                            loop = LoopMode.UseClipDefault,
                            blendDuration = stop.blendOut,
                            time = 0f
                        });
                        entityManager.SetComponentEnabled<AnimationCommandPending>(actorEntity, true);
                    }
                }
                slotStates[slotIndex] = slotState;
            }
        }

        // -----------------------------------------------------------------------------------
        // Auto locomotion: the profile's moving/standing entry from the bound actor's own real
        // displacement. Authored blocks always win their layer; a stop key hands it back.
        // -----------------------------------------------------------------------------------

        /// <summary>The locomotion layer is the moving entry's own resolved layer — never a config value, since a profile can move an animation between layers.</summary>
        private static bool TryResolveLocomotionLayerIndex(
            EntityManager entityManager, in CutsceneSlotMetaBlob slotMeta, Entity actorEntity, out byte layerIndex)
        {
            layerIndex = 0;
            if (!slotMeta.locomotion.enabled || slotMeta.locomotion.movingKey == 0u
                || !entityManager.HasComponent<ActorProfile>(actorEntity))
            {
                return false;
            }
            BlobAssetReference<ActorProfileBlob> profileReference = entityManager.GetComponentData<ActorProfile>(actorEntity).Value;
            if (!profileReference.IsCreated)
            {
                return false;
            }
            ref ActorProfileBlob profileBlob = ref profileReference.Value;
            int movingAnimationIndex;
            if (!ActorProfileApi.TryFindAnimation(ref profileBlob, slotMeta.locomotion.movingKey, out movingAnimationIndex))
            {
                return false;
            }
            layerIndex = profileBlob.animations[movingAnimationIndex].layerIndex;
            return true;
        }

        private static void ProcessLocomotion(
            EntityManager entityManager, ref CutsceneBlob blob, float deltaTime,
            DynamicBuffer<CutsceneActorBinding> bindings, DynamicBuffer<CutsceneSlotRuntimeState> slotStates,
            DynamicBuffer<CutsceneSlotLayerState> layerStates)
        {
            int slotCount = math.min(blob.slots.Length, slotStates.Length);
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                ref CutsceneSlotMetaBlob slotMeta = ref blob.slots[slotIndex];
                if (slotMeta.kind != CutsceneSlotKind.Actor)
                {
                    continue;
                }

                Entity actorEntity;
                if (!TryResolveBinding(bindings, slotMeta.slotId, out actorEntity)
                    || !entityManager.HasComponent<LocalTransform>(actorEntity))
                {
                    continue;
                }

                CutsceneSlotRuntimeState slotState = slotStates[slotIndex];
                float3 currentPosition = entityManager.GetComponentData<LocalTransform>(actorEntity).Position;
                float3 displacement = currentPosition - slotState.lastPosition;
                bool isMoving = slotState.hasLastPosition
                    && CutsceneLocomotionMath.IsMoving(in displacement, deltaTime, slotMeta.locomotion.speedThreshold);
                slotState.lastPosition = currentPosition;
                slotState.hasLastPosition = true;

                byte locomotionLayerIndex;
                bool hasLocomotion = TryResolveLocomotionLayerIndex(entityManager, in slotMeta, actorEntity, out locomotionLayerIndex);
                if (!hasLocomotion || !entityManager.HasComponent<AnimationCommand>(actorEntity)
                    || !entityManager.HasBuffer<PlaybackLayer>(actorEntity))
                {
                    slotStates[slotIndex] = slotState;
                    continue;
                }

                int layerStateIndex = slotIndex * CutsceneApi.LayersPerSlot + locomotionLayerIndex;
                if (layerStateIndex < layerStates.Length && layerStates[layerStateIndex].activeBlockSegmentIndex >= 0)
                {
                    // Authored wins: a block claims its layer from its start, a stop key hands it back.
                    slotStates[slotIndex] = slotState;
                    continue;
                }

                uint desiredKey = isMoving ? slotMeta.locomotion.movingKey : slotMeta.locomotion.standingKey;
                if (desiredKey != slotState.lastLocomotionKey)
                {
                    DynamicBuffer<PlaybackLayer> layers = entityManager.GetBuffer<PlaybackLayer>(actorEntity);
                    bool alreadyPlaying = desiredKey != 0u && PlaybackApi.IsAnimationPlaying(layers, desiredKey);
                    if (!alreadyPlaying)
                    {
                        DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(actorEntity);
                        if (desiredKey == 0u)
                        {
                            // A zero standing key stops the moving entry rather than switching to
                            // another named entry.
                            commands.Add(new AnimationCommand
                            {
                                kind = CommandKind.StopAnimation,
                                animationKey = slotMeta.locomotion.movingKey,
                                blendDuration = float.NaN
                            });
                        }
                        else
                        {
                            commands.Add(new AnimationCommand
                            {
                                kind = CommandKind.PlayAnimation,
                                animationKey = desiredKey,
                                speed = float.NaN,
                                loop = LoopMode.UseClipDefault,
                                blendDuration = float.NaN
                            });
                        }
                        entityManager.SetComponentEnabled<AnimationCommandPending>(actorEntity, true);
                    }
                    slotState.lastLocomotionKey = desiredKey;
                }

                slotStates[slotIndex] = slotState;
            }
        }

        // -----------------------------------------------------------------------------------
        // Facing. The toolkit writes CutsceneFacing (the host's mirror/view-offset input) and, for a
        // bound Actor slot, ActorFacing — it never writes PartFacing; the host owns that.
        // -----------------------------------------------------------------------------------

        private struct PendingFacingOp
        {
            public Entity entity;
            public float angleDegrees;
        }

        /// <summary>
        /// Writes every bound Actor slot's facing. Adding <see cref="CutsceneFacing"/> is a
        /// structural change and is queued; setting its value and enabled bit is not, and stays
        /// inline so every frame after the first costs nothing but a write.
        /// </summary>
        private static void ProcessFacing(
            EntityManager entityManager, ref CutsceneBlob blob, DynamicBuffer<CutsceneActorBinding> bindings,
            DynamicBuffer<CutsceneSlotRuntimeState> slotStates, ref CutscenePlaybackState playbackState,
            NativeList<PendingFacingOp> pendingFacingOps)
        {
            ref CutsceneSegmentBlob segment = ref blob.segments[playbackState.segmentIndex];
            int slotCount = math.min(blob.slots.Length, slotStates.Length);
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                if (blob.slots[slotIndex].kind != CutsceneSlotKind.Actor)
                {
                    continue;
                }
                Entity actorEntity;
                if (!TryResolveBinding(bindings, blob.slots[slotIndex].slotId, out actorEntity))
                {
                    continue;
                }

                CutsceneSlotRuntimeState slotState = slotStates[slotIndex];
                float angleDegrees;
                if (!TryResolveSlotFacingAngle(
                        entityManager, ref segment, slotIndex, actorEntity, ref slotState,
                        playbackState.timeInSegment, out angleDegrees))
                {
                    // No override key and nothing moving: leave whatever facing is in effect alone
                    // rather than snapping the actor east.
                    slotStates[slotIndex] = slotState;
                    continue;
                }

                if (entityManager.HasComponent<CutsceneFacing>(actorEntity))
                {
                    entityManager.SetComponentData(
                        actorEntity, new CutsceneFacing { angleDegrees = angleDegrees });
                    entityManager.SetComponentEnabled<CutsceneFacing>(actorEntity, true);
                }
                else
                {
                    pendingFacingOps.Add(new PendingFacingOp
                    {
                        entity = actorEntity,
                        angleDegrees = angleDegrees
                    });
                }

                WriteActorFacing(entityManager, actorEntity, angleDegrees);

                slotStates[slotIndex] = slotState;
            }
        }

        // The mark branch exists because an outstanding mark suspends a slot's root lane (the host
        // is walking the actor and owns the transform), so the lane says where the rehearsal would
        // have put it, not where the actor is going; facing off the vector to the mark is what the
        // actor is actually doing. The latch sits between the mark branch and root travel: an
        // arrival (or timeout) leaves the actor standing, and the mark branch above has nothing left
        // to say once hasOutstandingMark clears, so without the latch a standing actor would snap to
        // whatever root travel derives from its now-motionless lane.
        /// <summary>
        /// The facing angle a slot is under at <paramref name="timeInSegment"/>: a Fixed key first
        /// (an Auto key cancels the pin), then — while walking to a mark — the direction of the mark,
        /// then a resolved mark's latched arrival facing until the slot moves again, and otherwise
        /// the direction its root lane is travelling.
        /// </summary>
        private static bool TryResolveSlotFacingAngle(
            EntityManager entityManager, ref CutsceneSegmentBlob segment, int slotIndex, Entity boundEntity,
            ref CutsceneSlotRuntimeState slotState, float timeInSegment, out float angleDegrees)
        {
            ref CutsceneSlotSegmentBlob slotSegment = ref segment.slotTracks[slotIndex];
            if (CutsceneBlobSampler.TryResolveFacingOverride(
                    ref slotSegment.facingKeys, timeInSegment, out angleDegrees))
            {
                slotState.hasLatchedFacing = false;
                return true;
            }

            if (slotState.hasOutstandingMark
                && entityManager.HasComponent<CutsceneMoveToMark>(boundEntity)
                && entityManager.IsComponentEnabled<CutsceneMoveToMark>(boundEntity)
                && entityManager.HasComponent<LocalTransform>(boundEntity))
            {
                CutsceneMoveToMark order = entityManager.GetComponentData<CutsceneMoveToMark>(boundEntity);
                float3 toMark = order.position - entityManager.GetComponentData<LocalTransform>(boundEntity).Position;
                toMark.y = 0f;
                if (math.lengthsq(toMark) >= 1e-6f)
                {
                    angleDegrees = CutsceneFacingVariants.AngleDegreesFromTravel(in toMark);
                    return true;
                }
            }

            if (slotState.hasLatchedFacing)
            {
                if (entityManager.HasComponent<LocalTransform>(boundEntity))
                {
                    float3 currentPosition = entityManager.GetComponentData<LocalTransform>(boundEntity).Position;
                    bool movedSinceLastFrame = slotState.hasLastPosition
                        && math.lengthsq(currentPosition - slotState.lastPosition) > 1e-6f;
                    if (movedSinceLastFrame)
                    {
                        slotState.hasLatchedFacing = false;
                    }
                }
                if (slotState.hasLatchedFacing)
                {
                    angleDegrees = slotState.latchedFacingDegrees;
                    return true;
                }
            }

            return CutsceneBlobSampler.TryDeriveFacingFromRootTravel(
                ref slotSegment.transformKeys, timeInSegment, out angleDegrees);
        }

        /// <summary>
        /// Folds a resolved angle onto the bound actor's own <c>ActorProfile.turnDirections</c> and
        /// writes it into <see cref="ActorFacing.facing"/> — <see cref="ActorFacing.appliedFacing"/>
        /// is never touched here, that is <c>ActorFacingRepickSystem</c>'s.
        /// </summary>
        private static void WriteActorFacing(EntityManager entityManager, Entity actorEntity, float angleDegrees)
        {
            if (!entityManager.HasComponent<ActorFacing>(actorEntity)
                || !entityManager.HasComponent<ActorProfile>(actorEntity))
            {
                return;
            }
            BlobAssetReference<ActorProfileBlob> profileReference = entityManager.GetComponentData<ActorProfile>(actorEntity).Value;
            if (!profileReference.IsCreated)
            {
                return;
            }
            AnimationDirections turnDirections = profileReference.Value.turnDirections;

            float angleRadians = math.radians(angleDegrees);
            float2 facingVector = new float2(math.cos(angleRadians), math.sin(angleRadians));

            ActorFacing actorFacing = entityManager.GetComponentData<ActorFacing>(actorEntity);
            Direction resolvedFacing = FacingResolver.FromMovement(in facingVector, turnDirections, actorFacing.facing);
            if (resolvedFacing == actorFacing.facing)
            {
                return;
            }
            actorFacing.facing = resolvedFacing;
            entityManager.SetComponentData(actorEntity, actorFacing);
        }

        private static void ApplyPendingFacingOps(
            EntityManager entityManager, NativeList<PendingFacingOp> pendingFacingOps)
        {
            for (int opIndex = 0; opIndex < pendingFacingOps.Length; opIndex++)
            {
                PendingFacingOp op = pendingFacingOps[opIndex];
                if (!entityManager.Exists(op.entity))
                {
                    continue;
                }
                if (!entityManager.HasComponent<CutsceneFacing>(op.entity))
                {
                    entityManager.AddComponent<CutsceneFacing>(op.entity);
                }
                entityManager.SetComponentData(
                    op.entity, new CutsceneFacing { angleDegrees = op.angleDegrees });
                entityManager.SetComponentEnabled<CutsceneFacing>(op.entity, true);
            }
        }

        /// <summary>A completed cutscene stops steering: the host's own facing takes over again.</summary>
        private static void DisableActorFacing(
            EntityManager entityManager, ref CutsceneBlob blob, DynamicBuffer<CutsceneActorBinding> bindings)
        {
            for (int slotIndex = 0; slotIndex < blob.slots.Length; slotIndex++)
            {
                Entity actorEntity;
                if (TryResolveBinding(bindings, blob.slots[slotIndex].slotId, out actorEntity)
                    && entityManager.HasComponent<CutsceneFacing>(actorEntity))
                {
                    entityManager.SetComponentEnabled<CutsceneFacing>(actorEntity, false);
                }
            }
        }

        private static void StopActorLayers(
            EntityManager entityManager, ref CutsceneBlob blob, DynamicBuffer<CutsceneActorBinding> bindings,
            DynamicBuffer<CutsceneSlotLayerState> layerStates)
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
                bool issuedAny = false;
                for (int layerOffset = 0; layerOffset < CutsceneApi.LayersPerSlot; layerOffset++)
                {
                    int layerStateIndex = slotIndex * CutsceneApi.LayersPerSlot + layerOffset;
                    if (layerStateIndex >= layerStates.Length)
                    {
                        break;
                    }
                    if (layerStates[layerStateIndex].activeBlockSegmentIndex < 0)
                    {
                        // The locomotion layer, and any layer nothing ever claimed, is left playing
                        // whatever it is playing — a host that assigns idle/walk itself finds the
                        // right one already there.
                        continue;
                    }
                    commands.Add(new AnimationCommand
                    {
                        kind = CommandKind.Stop,
                        layerIndex = (byte)layerOffset,
                        clip = default,
                        speed = 0f,
                        loop = LoopMode.UseClipDefault,
                        blendDuration = float.NaN,
                        time = 0f
                    });
                    issuedAny = true;
                }

                if (issuedAny)
                {
                    entityManager.SetComponentEnabled<AnimationCommandPending>(actorEntity, true);
                }
            }
        }

        /// <summary>
        /// Issues <c>SetSpeed</c> to every layer an authored block currently claims, re-deriving the
        /// entry's own speed from the profile since <see cref="CutsceneSlotLayerState"/> keeps only
        /// the block's own authored speed.
        /// </summary>
        private static void ApplyLayerSpeedToAllActorSlots(
            EntityManager entityManager, ref CutsceneBlob blob, DynamicBuffer<CutsceneActorBinding> bindings,
            DynamicBuffer<CutsceneSlotLayerState> layerStates, float layerSpeed)
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

                bool hasProfile = entityManager.HasComponent<ActorProfile>(actorEntity);
                BlobAssetReference<ActorProfileBlob> profileReference = hasProfile
                    ? entityManager.GetComponentData<ActorProfile>(actorEntity).Value : default;
                if (!hasProfile || !profileReference.IsCreated)
                {
                    continue;
                }
                ref ActorProfileBlob profileBlob = ref profileReference.Value;

                DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(actorEntity);
                bool issuedAny = false;
                for (int layerOffset = 0; layerOffset < CutsceneApi.LayersPerSlot; layerOffset++)
                {
                    int layerStateIndex = slotIndex * CutsceneApi.LayersPerSlot + layerOffset;
                    if (layerStateIndex >= layerStates.Length)
                    {
                        break;
                    }
                    CutsceneSlotLayerState layerState = layerStates[layerStateIndex];
                    if (layerState.activeBlockSegmentIndex < 0)
                    {
                        continue;
                    }

                    float entrySpeed = 1f;
                    if (layerState.activeBlockSegmentIndex < blob.segments.Length)
                    {
                        ref CutsceneSlotSegmentBlob activeSlotSegment =
                            ref blob.segments[layerState.activeBlockSegmentIndex].slotTracks[slotIndex];
                        if (layerState.activeBlockIndex >= 0
                            && layerState.activeBlockIndex < activeSlotSegment.clipBlocks.Length)
                        {
                            uint activeAnimationKey = activeSlotSegment.clipBlocks[layerState.activeBlockIndex].animationKey;
                            int animationIndex;
                            if (ActorProfileApi.TryFindAnimation(ref profileBlob, activeAnimationKey, out animationIndex))
                            {
                                entrySpeed = profileBlob.animations[animationIndex].speed;
                            }
                        }
                    }

                    commands.Add(new AnimationCommand
                    {
                        kind = CommandKind.SetSpeed,
                        layerIndex = (byte)layerOffset,
                        clip = default,
                        // The block's own speed multiplies the cutscene's, and the entry's own on
                        // top: a host halving playback must not silently override a block or profile
                        // entry authored to run slower.
                        speed = layerSpeed * CutsceneBlockTiming.EffectiveBlockSpeed(layerState.activeBlockSpeed) * entrySpeed,
                        loop = LoopMode.UseClipDefault,
                        blendDuration = float.NaN,
                        time = 0f
                    });
                    issuedAny = true;
                }

                if (issuedAny)
                {
                    entityManager.SetComponentEnabled<AnimationCommandPending>(actorEntity, true);
                }
            }
        }

        // -----------------------------------------------------------------------------------
        // Events use the same AnimEventOutput shape a clip's own events use, on the cutscene
        // request entity itself rather than any one bound actor.
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
                // reset — a rider that boarded before a hold is still aboard after it.
                CutsceneSlotRuntimeState slotState = slotStates[i];
                slotState.nextClipBlockIndex = 0;
                slotState.nextAttachMarkerIndex = 0;
                slotState.nextMarkIndex = 0;
                slotState.nextLayerStopIndex = 0;
                slotStates[i] = slotState;
            }
        }

        private static void CompleteNaturally(
            EntityManager entityManager, ref CutsceneBlob blob, DynamicBuffer<CutsceneActorBinding> bindings,
            DynamicBuffer<CutsceneSlotRuntimeState> slotStates, DynamicBuffer<CutsceneSlotLayerState> layerStates,
            ref CutscenePlaybackState playbackState)
        {
            StopActorLayers(entityManager, ref blob, bindings, layerStates);
            ClearOutstandingMarks(entityManager, ref blob, bindings, slotStates);
            DisableActorFacing(entityManager, ref blob, bindings);
            playbackState.isComplete = true;
        }

        /// <summary>
        /// Jumps straight to the cutscene's final instant. Reaches the exact same
        /// <c>(segmentIndex, timeInSegment)</c> — and therefore the exact same sampled pose — a full
        /// play-through eventually settles on, which is what makes skipped and watched end states
        /// identical rather than merely close.
        /// </summary>
        private static void PerformSkip(
            EntityManager entityManager, ref CutsceneBlob blob,
            DynamicBuffer<CutsceneActorBinding> bindings, DynamicBuffer<CutsceneSlotRuntimeState> slotStates,
            DynamicBuffer<CutsceneSlotLayerState> layerStates, ref CutscenePlaybackState playbackState,
            DynamicBuffer<AnimEventOutput> eventOutput, Entity requestEntity, NativeList<PendingAttachOp> pendingAttachOps)
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
            // leave the same world — including the detach signals a host may have been waiting on.
            SkipAttachMarkers(ref blob, bindings, slotStates, ref playbackState, pendingAttachOps);

            // An outstanding order resolves the way a timeout resolves one - placed, and not warned
            // about: a skip is a deliberate jump to the end, not a mover that failed to arrive.
            TeleportOutstandingMarks(entityManager, ref blob, bindings, slotStates);

            playbackState.segmentIndex = blob.segments.Length - 1;
            ref CutsceneSegmentBlob finalSegment = ref blob.segments[playbackState.segmentIndex];
            playbackState.timeInSegment = finalSegment.duration;
            playbackState.isPausedOnHold = false;
            playbackState.nextEventIndex = finalSegment.events.Length;
            for (int slotIndex = 0; slotIndex < slotStates.Length; slotIndex++)
            {
                CutsceneSlotRuntimeState slotState = slotStates[slotIndex];
                slotState.nextAttachMarkerIndex = finalSegment.slotTracks[slotIndex].attachMarkers.Length;
                slotState.nextMarkIndex = finalSegment.slotTracks[slotIndex].markKeys.Length;
                slotState.nextLayerStopIndex = finalSegment.slotTracks[slotIndex].layerStops.Length;
                slotStates[slotIndex] = slotState;
            }

            ApplyPose(entityManager, ref blob, bindings, slotStates, ref playbackState);
            CompleteNaturally(entityManager, ref blob, bindings, slotStates, layerStates, ref playbackState);
        }

        // -----------------------------------------------------------------------------------
        // Attach lane. Collected here, applied after the query loop: every one of these operations
        // is a structural change, which SystemAPI.Query forbids mid-iteration.
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
                    // Warned at bake; silently skipped here rather than erroring.
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
            slotState.hasEverDetached = true;
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
        /// Hides or reveals an attached entity and every rendering member of its linked group, via
        /// <c>DisableRendering</c> — never <c>AnimVisible</c>, which a host's own visibility system
        /// rewrites every frame. The member list is read fresh each time, since a spawned actor's
        /// <c>LinkedEntityGroup</c> is rebuilt by its host's spawn-init.
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
        // Marks lane. The toolkit orders a move and judges arrival; it never walks the entity
        // itself - pathfinding belongs to the host.
        // -----------------------------------------------------------------------------------

        private struct PendingMarkOp
        {
            public Entity entity;
            public CutsceneMoveToMark order;
        }

        /// <summary>
        /// Walks each slot's mark cursor up to the playhead, flagging the slot as outstanding now
        /// (so <see cref="ApplyPose"/> already suspends its root lane this frame) and queuing the
        /// structural half - adding the order component - for after the query loop.
        /// </summary>
        private static void ProcessMarks(
            ref CutsceneBlob blob, DynamicBuffer<CutsceneActorBinding> bindings,
            DynamicBuffer<CutsceneSlotRuntimeState> slotStates, ref CutscenePlaybackState playbackState,
            NativeList<PendingMarkOp> pendingMarkOps)
        {
            ref CutsceneSegmentBlob segment = ref blob.segments[playbackState.segmentIndex];
            for (int slotIndex = 0; slotIndex < blob.slots.Length; slotIndex++)
            {
                ref CutsceneSlotSegmentBlob slotSegment = ref segment.slotTracks[slotIndex];
                CutsceneSlotRuntimeState slotState = slotStates[slotIndex];
                while (slotState.nextMarkIndex < slotSegment.markKeys.Length &&
                       slotSegment.markKeys[slotState.nextMarkIndex].time <= playbackState.timeInSegment)
                {
                    CutsceneMarkKeyBlob mark = slotSegment.markKeys[slotState.nextMarkIndex];
                    slotState.nextMarkIndex++;

                    Entity boundEntity;
                    if (!TryResolveBinding(bindings, blob.slots[slotIndex].slotId, out boundEntity))
                    {
                        continue;
                    }

                    pendingMarkOps.Add(new PendingMarkOp
                    {
                        entity = boundEntity,
                        order = new CutsceneMoveToMark
                        {
                            position = mark.position,
                            facingRadians = mark.facingRadians,
                            toleranceMeters = mark.toleranceMeters,
                            timeoutSeconds = mark.timeoutSeconds,
                            elapsedSeconds = 0f
                        }
                    });
                    slotState.hasOutstandingMark = true;
                }
                slotStates[slotIndex] = slotState;
            }
        }

        /// <summary>
        /// Judges every outstanding order: arrived (XZ distance within tolerance), or timed out and
        /// therefore placed. <paramref name="isPaused"/> freezes the timeout clock only - a paused
        /// cutscene must not tick one down - while arrival still resolves, because whatever is
        /// moving the entity may not be paused with it. Either resolution latches the mark's arrival
        /// facing (§3.3) so a standing actor holds it until it moves again or a Fixed key takes over.
        /// </summary>
        private static void ResolveOutstandingMarks(
            EntityManager entityManager, ref CutsceneBlob blob, DynamicBuffer<CutsceneActorBinding> bindings,
            DynamicBuffer<CutsceneSlotRuntimeState> slotStates, float deltaTime, bool isPaused)
        {
            int slotCount = math.min(blob.slots.Length, slotStates.Length);
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                CutsceneSlotRuntimeState slotState = slotStates[slotIndex];
                if (!slotState.hasOutstandingMark)
                {
                    continue;
                }

                Entity boundEntity;
                if (!TryResolveBinding(bindings, blob.slots[slotIndex].slotId, out boundEntity)
                    || !entityManager.HasComponent<CutsceneMoveToMark>(boundEntity)
                    || !entityManager.IsComponentEnabled<CutsceneMoveToMark>(boundEntity))
                {
                    // The order was queued this frame and is applied after the loop; judge it next frame.
                    continue;
                }

                CutsceneMoveToMark order = entityManager.GetComponentData<CutsceneMoveToMark>(boundEntity);
                float3 currentPosition = entityManager.HasComponent<LocalTransform>(boundEntity)
                    ? entityManager.GetComponentData<LocalTransform>(boundEntity).Position
                    : order.position;

                // XZ only: a mark authored off the walkable plane still resolves, and the Y an
                // arriving entity stands at is its own, never the mark's.
                float2 planarOffset = new float2(
                    currentPosition.x - order.position.x, currentPosition.z - order.position.z);
                if (math.lengthsq(planarOffset) <= order.toleranceMeters * order.toleranceMeters)
                {
                    entityManager.SetComponentEnabled<CutsceneMoveToMark>(boundEntity, false);
                    slotState.hasOutstandingMark = false;
                    slotState.hasLatchedFacing = true;
                    slotState.latchedFacingDegrees = math.degrees(order.facingRadians);
                    slotStates[slotIndex] = slotState;
                    continue;
                }

                if (!isPaused)
                {
                    order.elapsedSeconds += deltaTime;
                    entityManager.SetComponentData(boundEntity, order);
                }

                if (order.timeoutSeconds > 0f && order.elapsedSeconds >= order.timeoutSeconds)
                {
                    PlaceAtMark(entityManager, boundEntity, order);
                    entityManager.SetComponentEnabled<CutsceneMoveToMark>(boundEntity, false);
                    slotState.hasOutstandingMark = false;
                    slotState.hasLatchedFacing = true;
                    slotState.latchedFacingDegrees = math.degrees(order.facingRadians);
                    slotStates[slotIndex] = slotState;
                    UnityEngine.Debug.LogWarning(
                        "[DOTS Animation Toolkit] Cutscene slot " + slotIndex + " did not reach its mark within "
                        + order.timeoutSeconds + "s and was placed there, so the scene could continue.");
                }
            }
        }

        private static bool AnySlotHasAnOutstandingMark(DynamicBuffer<CutsceneSlotRuntimeState> slotStates)
        {
            for (int slotIndex = 0; slotIndex < slotStates.Length; slotIndex++)
            {
                if (slotStates[slotIndex].hasOutstandingMark)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>On skip, every outstanding order is resolved by placement, silently.</summary>
        private static void TeleportOutstandingMarks(
            EntityManager entityManager, ref CutsceneBlob blob, DynamicBuffer<CutsceneActorBinding> bindings,
            DynamicBuffer<CutsceneSlotRuntimeState> slotStates)
        {
            int slotCount = math.min(blob.slots.Length, slotStates.Length);
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                CutsceneSlotRuntimeState slotState = slotStates[slotIndex];
                if (!slotState.hasOutstandingMark)
                {
                    continue;
                }

                Entity boundEntity;
                if (TryResolveBinding(bindings, blob.slots[slotIndex].slotId, out boundEntity)
                    && entityManager.HasComponent<CutsceneMoveToMark>(boundEntity))
                {
                    PlaceAtMark(
                        entityManager, boundEntity,
                        entityManager.GetComponentData<CutsceneMoveToMark>(boundEntity));
                    entityManager.SetComponentEnabled<CutsceneMoveToMark>(boundEntity, false);
                }
                slotState.hasOutstandingMark = false;
                slotStates[slotIndex] = slotState;
            }
        }

        /// <summary>On completion, an order nobody fulfilled must not outlive the cutscene that gave it.</summary>
        private static void ClearOutstandingMarks(
            EntityManager entityManager, ref CutsceneBlob blob, DynamicBuffer<CutsceneActorBinding> bindings,
            DynamicBuffer<CutsceneSlotRuntimeState> slotStates)
        {
            int slotCount = math.min(blob.slots.Length, slotStates.Length);
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                CutsceneSlotRuntimeState slotState = slotStates[slotIndex];
                Entity boundEntity;
                if (TryResolveBinding(bindings, blob.slots[slotIndex].slotId, out boundEntity)
                    && entityManager.HasComponent<CutsceneMoveToMark>(boundEntity)
                    && entityManager.IsComponentEnabled<CutsceneMoveToMark>(boundEntity))
                {
                    entityManager.SetComponentEnabled<CutsceneMoveToMark>(boundEntity, false);
                }
                slotState.hasOutstandingMark = false;
                slotStates[slotIndex] = slotState;
            }
        }

        private static void PlaceAtMark(EntityManager entityManager, Entity entity, in CutsceneMoveToMark order)
        {
            if (!entityManager.HasComponent<LocalTransform>(entity))
            {
                return;
            }
            LocalTransform localTransform = entityManager.GetComponentData<LocalTransform>(entity);
            localTransform.Position = order.position;
            localTransform.Rotation = quaternion.RotateY(order.facingRadians);
            entityManager.SetComponentData(entity, localTransform);
        }

        private static void ApplyPendingMarkOps(
            EntityManager entityManager, NativeList<PendingMarkOp> pendingMarkOps)
        {
            for (int opIndex = 0; opIndex < pendingMarkOps.Length; opIndex++)
            {
                PendingMarkOp op = pendingMarkOps[opIndex];
                if (!entityManager.Exists(op.entity))
                {
                    continue;
                }
                if (!entityManager.HasComponent<CutsceneMoveToMark>(op.entity))
                {
                    entityManager.AddComponent<CutsceneMoveToMark>(op.entity);
                }
                entityManager.SetComponentData(op.entity, op.order);
                entityManager.SetComponentEnabled<CutsceneMoveToMark>(op.entity, true);
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
                // An attached slot's transform belongs to its host — SocketResolveSystem or Unity's
                // own parent hierarchy writes it, and a root key written here would fight that every frame.
                if (slotStates[slotIndex].attachedHostSlotIndex >= 0)
                {
                    continue;
                }

                // Same rule for a slot still walking to a mark: whatever the host moves it with owns
                // the transform, and the merged arrival key must not drag it along the rehearsed
                // path while the real walk is still happening.
                if (slotStates[slotIndex].hasOutstandingMark)
                {
                    continue;
                }

                // A slot that has ever finished a ride never gets its root lane back. The flat
                // lane's only real content is the single pre-ride pickup key CutsceneMarkMerge folds
                // in (nothing is ever authored for "while riding" or "after being dropped off", since
                // that motion belongs to the host) — resampling it post-detach would snap the rider
                // back to where it was picked up, on top of whatever ApplyDetach correctly placed it at.
                if (slotStates[slotIndex].hasEverDetached)
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
                    // No root keys authored for this slot: leave the bound entity's transform
                    // exactly as it is rather than snapping it to the world origin.
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
                // False once the cutscene has completed, even though a pose is still written here —
                // a host's exit transition must fire exactly once, not keep re-triggering on a
                // stale-but-still-"driven" pose every frame after the end.
                isDriven = !playbackState.isComplete
            });
        }
    }
}
