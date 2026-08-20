// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Turns this frame's playback advance into animation events (architecture section 5.5): the
    /// markers the layers crossed, plus a <see cref="ReservedEventKeys.ClipFinished"/> for every
    /// clip that ended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This system appends and enables only.</strong> It does not clear
    /// <see cref="AnimEventOutput"/> and does not disable <see cref="AnimEventsPending"/> —
    /// <c>CommandApplySystem</c> owns the clear, at the top of the group (amendment A28). Adding a
    /// clear back here would destroy the <see cref="ReservedEventKeys.ClipResolveFailed"/> events
    /// raised earlier in the same frame, which is the defect A28 exists to close.
    /// </para>
    /// <para>
    /// <strong>The crossing window is read, not recomputed.</strong>
    /// <see cref="PlaybackLayer.advanceStartTime"/> to <see cref="PlaybackLayer.time"/>, both on the
    /// un-wrapped timeline (amendment A27). Deriving the opening edge as <c>time − dt × speed</c>
    /// is wrong on exactly the frames a Once clip clamps or a queue promotes, and wrong quietly:
    /// the events it drops are the last ones in a clip.
    /// </para>
    /// <para>
    /// <strong>Never gated on <see cref="AnimVisible"/>.</strong> Events are gameplay, not
    /// presentation. An actor behind the camera lands its hits on schedule.
    /// </para>
    /// <para>
    /// <strong>Latency contract.</strong> Events are valid from this system's execution until the
    /// next frame's <c>CommandApplySystem</c>. A host system ordered earlier in the frame sees the
    /// previous frame's events; one that needs them same-frame orders itself after
    /// <see cref="AnimationToolkitSystemGroup"/>.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitLogicSystemGroup))]
    [UpdateAfter(typeof(PlaybackTimeSystem))]
    [BurstCompile]
    public partial struct EventEmissionSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlaybackLayer>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EmitAnimationEventsJob emitJob = new EmitAnimationEventsJob();
            state.Dependency = emitJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// Collects one actor's marker crossings and clip completions into its event buffer.
    /// </summary>
    /// <remarks>
    /// <see cref="AnimEventsPending"/> is declared <c>WithPresent</c>: it is disabled on every actor
    /// that emitted nothing last frame, which is most of them, and an <c>EnabledRefRW&lt;T&gt;</c>
    /// parameter would otherwise enrol it as an enabled-only filter — so the system would emit
    /// events only for actors that already had some, and an actor's first event would never fire.
    /// </remarks>
    [BurstCompile]
    [WithPresent(typeof(AnimEventsPending))]
    internal partial struct EmitAnimationEventsJob : IJobEntity
    {
        private void Execute(
            in DynamicBuffer<PlaybackLayer> layers,
            ref DynamicBuffer<AnimEventOutput> animEvents,
            in ClipRegistry clipRegistry,
            EnabledRefRW<AnimEventsPending> animEventsPendingEnabled)
        {
            BlobAssetReference<ClipRegistryBlob> registryReference = clipRegistry.Value;
            ref ClipRegistryBlob registry = ref registryReference.Value;

            int emittedCount = 0;
            NativeList<int> crossedEventIndices = new NativeList<int>(8, Allocator.Temp);

            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                PlaybackLayer layer = layers[layerIndex];
                bool finishedThisFrame = (layer.flags & PlaybackFlags.FinishedThisFrame) != 0;

                // A Once clip that finished with nothing queued is already inactive — this is the
                // frame its completion has to be reported, so "active" cannot be the whole gate.
                if ((layer.flags & PlaybackFlags.Active) == 0 && !finishedThisFrame)
                {
                    continue;
                }

                emittedCount += EmitLayerCrossings(
                    ref registry,
                    layer,
                    (byte)layerIndex,
                    ref crossedEventIndices,
                    ref animEvents);

                if (finishedThisFrame)
                {
                    // layer.clip is still the clip that finished, and that is a property of
                    // amendment A30 rather than of this line: a queued follow-up is not promoted
                    // until the next advance, precisely so that this event and the crossings above
                    // it can be attributed to the clip they belong to.
                    animEvents.Add(new AnimEventOutput
                    {
                        eventKey = (uint)ReservedEventKeys.ClipFinished,
                        layerIndex = (byte)layerIndex,
                        clip = layer.clip,
                        intParam = 0,
                        floatParam = 0f
                    });
                    emittedCount++;
                }
            }

            crossedEventIndices.Dispose();

            // Enabled, never disabled. CommandApplySystem's clear is the sole reset path, so an
            // actor whose only event this frame was a ClipResolveFailed raised before the advance
            // keeps its flag rather than having it turned off here.
            if (emittedCount > 0)
            {
                animEventsPendingEnabled.ValueRW = true;
            }
        }

        /// <summary>
        /// Emits the markers the layer's current clip crossed during this frame's advance.
        /// </summary>
        /// <returns>How many events were appended.</returns>
        private static int EmitLayerCrossings(
            ref ClipRegistryBlob registry,
            in PlaybackLayer layer,
            byte layerIndex,
            ref NativeList<int> crossedEventIndices,
            ref DynamicBuffer<AnimEventOutput> animEvents)
        {
            // A layer fading out of a Stop has no current clip. The crossfade source deliberately
            // emits nothing (amendment A27, limitation R11), so there is nothing to collect.
            if (layer.clipIndex < 0 || layer.clipIndex >= registry.clips.Length)
            {
                return 0;
            }

            ref ClipBlob clip = ref registry.clips[layer.clipIndex];
            if (clip.events.Length == 0)
            {
                return 0;
            }

            LoopMode resolvedLoopMode = ClipSampler.ResolveLoopMode(layer.loop, clip.defaultLoop);

            crossedEventIndices.Clear();
            int crossingCount = EventWrapMath.CollectCrossings(
                ref clip.events,
                layer.advanceStartTime,
                layer.time,
                clip.duration,
                resolvedLoopMode,
                ref crossedEventIndices);

            for (int crossingIndex = 0; crossingIndex < crossedEventIndices.Length; crossingIndex++)
            {
                ref EventMarkerBlob marker = ref clip.events[crossedEventIndices[crossingIndex]];
                animEvents.Add(new AnimEventOutput
                {
                    eventKey = marker.eventKey,
                    layerIndex = layerIndex,
                    clip = layer.clip,
                    intParam = marker.intParam,
                    floatParam = marker.floatParam
                });
            }

            return crossingCount;
        }
    }
}
