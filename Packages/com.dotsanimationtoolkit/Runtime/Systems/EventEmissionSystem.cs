// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs after <c>PlaybackTimeSystem</c> in <see cref="AnimationToolkitLogicSystemGroup"/>: turns
    /// this frame's playback advance into animation events. Appends and enables only —
    /// <c>CommandApplySystem</c> owns clearing <see cref="AnimEventOutput"/>, at the top of the
    /// group next frame; a clear here would destroy resolve-failure events raised earlier in the same frame.
    /// </summary>
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

    // AnimEventsPending is WithPresent, not a plain EnabledRefRW parameter: it is disabled on most
    // actors (whichever emitted nothing last frame), and an EnabledRefRW<T> parameter would enrol
    // it as an enabled-only filter, so an actor's first-ever event would never fire.
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
                    // layer.clip is still the clip that finished: a queued follow-up is not promoted
                    // until the next advance, precisely so this event and the crossings above it can
                    // be attributed to the clip they belong to.
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

        /// <returns>How many events were appended.</returns>
        private static int EmitLayerCrossings(
            ref ClipRegistryBlob registry,
            in PlaybackLayer layer,
            byte layerIndex,
            ref NativeList<int> crossedEventIndices,
            ref DynamicBuffer<AnimEventOutput> animEvents)
        {
            // A layer fading out of a Stop has no current clip; the crossfade source deliberately
            // emits nothing, so there is nothing to collect.
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
            // The window is read (timeAtFrameStart to time), never recomputed as time - dt * speed:
            // that formula is wrong on exactly the frames a Once clip clamps or a queue promotes,
            // silently dropping the last events of a finishing clip.
            int crossingCount = EventWrapMath.CollectCrossings(
                ref clip.events,
                layer.timeAtFrameStart,
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
