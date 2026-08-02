// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Turns the requests a game wrote through <c>AnimationCommandUtil</c> into playback state
    /// (architecture section 5.4), and opens the frame's event window (amendment A28).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Two passes, in this order.</strong> First every actor holding last frame's events has
    /// its <see cref="AnimEventOutput"/> buffer cleared; then commands are applied. The clear lives
    /// here rather than in <c>EventEmissionSystem</c> because this system is specified to emit
    /// <see cref="ReservedEventKeys.ClipResolveFailed"/> and <c>EventEmissionSystem</c> runs
    /// <em>after</em> it — a buffer cleared there would destroy every resolve-failure event in the
    /// same frame it was raised, and the only test that could catch it is one that runs both systems
    /// in order. Amendment A28 records the move and how to revert it.
    /// </para>
    /// <para>
    /// <strong>The ordering that matters.</strong> On a crossfading Play, the outgoing clip's loop
    /// mode is copied into <see cref="PlaybackLayer.previousLoop"/> <em>before</em>
    /// <see cref="PlaybackLayer.loop"/> is overwritten with the incoming request. That single
    /// statement order is the whole reason the field exists (section 5.2): get it wrong and a clip
    /// that was played <see cref="LoopMode.Once"/> starts wrapping to zero as it fades out, which
    /// pops in precisely the transition the crossfade was added to smooth.
    /// </para>
    /// <para>
    /// <strong>Parallel safety.</strong> Each actor writes only its own buffers and its own
    /// enableable components, so there is nothing to coordinate between workers.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitLogicSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct CommandApplySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // Two independent reasons to run, so neither may gate the other: an actor can have
            // stale events and no commands (the usual case a frame after anything happened), or
            // commands and no events. RequireForUpdate would AND them and skip both jobs whenever
            // either side was empty.
            EntityQuery pendingCommandQuery = SystemAPI.QueryBuilder()
                .WithAll<AnimationCommandPending, AnimationCommand, PlaybackLayer, ClipRegistry>()
                .Build();
            EntityQuery staleEventQuery = SystemAPI.QueryBuilder()
                .WithAll<AnimEventsPending, AnimEventOutput>()
                .Build();

            NativeArray<EntityQuery> requiredQueries = new NativeArray<EntityQuery>(2, Allocator.Temp);
            requiredQueries[0] = pendingCommandQuery;
            requiredQueries[1] = staleEventQuery;
            state.RequireAnyForUpdate(requiredQueries);
            requiredQueries.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ClearStaleAnimEventsJob clearJob = new ClearStaleAnimEventsJob();
            state.Dependency = clearJob.ScheduleParallel(state.Dependency);

            ApplyAnimationCommandsJob applyJob = new ApplyAnimationCommandsJob();
            state.Dependency = applyJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// Drops last frame's events, so that everything written during this frame's logic group
    /// survives to the end of it (amendment A28).
    /// </summary>
    /// <remarks>
    /// The query is the enabled <see cref="AnimEventsPending"/> set — exactly the actors that have
    /// something to drop. Actors that emitted nothing are not visited at all, which is the same
    /// bargain the enableable was introduced for on the consumer side.
    /// </remarks>
    [BurstCompile]
    [WithAll(typeof(AnimEventsPending))]
    internal partial struct ClearStaleAnimEventsJob : IJobEntity
    {
        private void Execute(
            ref DynamicBuffer<AnimEventOutput> animEvents,
            EnabledRefRW<AnimEventsPending> animEventsPendingEnabled)
        {
            animEvents.Clear();
            animEventsPendingEnabled.ValueRW = false;
        }
    }

    /// <summary>
    /// Applies one actor's queued commands to its playback layers, then empties the command buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BoundsDirty"/> and <see cref="AnimEventsPending"/> are declared
    /// <c>WithPresent</c>, not <c>WithAll</c>. An <c>EnabledRefRW&lt;T&gt;</c> parameter puts
    /// <c>T</c> into the query as an <em>All</em> component by default, which would silently
    /// restrict this job to actors whose bounds happened to be dirty and whose events happened to be
    /// pending — a filter that is false for almost every actor almost every frame, and one that
    /// produces no error at all, just commands that quietly never apply.
    /// </para>
    /// </remarks>
    [BurstCompile]
    [WithAll(typeof(AnimationCommandPending))]
    [WithPresent(typeof(BoundsDirty), typeof(AnimEventsPending))]
    internal partial struct ApplyAnimationCommandsJob : IJobEntity
    {
        private void Execute(
            ref DynamicBuffer<PlaybackLayer> layers,
            ref DynamicBuffer<AnimationCommand> commands,
            ref DynamicBuffer<AnimEventOutput> animEvents,
            in ClipRegistry clipRegistry,
            EnabledRefRW<AnimationCommandPending> animationCommandPendingEnabled,
            EnabledRefRW<AnimEventsPending> animEventsPendingEnabled,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
        {
            // Copied to a local first: `clipRegistry` is an `in` parameter, and reaching through it
            // to a ref-returning property on a non-readonly struct is the kind of construct that
            // compiles into a defensive copy. The local removes the question entirely.
            BlobAssetReference<ClipRegistryBlob> registryReference = clipRegistry.Value;
            ref ClipRegistryBlob registry = ref registryReference.Value;

            for (int commandIndex = 0; commandIndex < commands.Length; commandIndex++)
            {
                AnimationCommand command = commands[commandIndex];

                // A layer index that no longer exists is a routine consequence of swapping a rig for
                // one with fewer layers, not a corrupt asset. The command is dropped and the layers
                // are left alone; amendment A29 records why no event is emitted for it.
                if (command.layerIndex >= layers.Length)
                {
                    continue;
                }

                ref PlaybackLayer layer = ref layers.ElementAt(command.layerIndex);
                switch (command.kind)
                {
                    case CommandKind.Play:
                        ApplyPlay(
                            ref layer,
                            ref registry,
                            command,
                            ref animEvents,
                            animEventsPendingEnabled,
                            boundsDirtyEnabled);
                        break;
                    case CommandKind.Queue:
                        ApplyQueue(
                            ref layer,
                            ref registry,
                            command,
                            ref animEvents,
                            animEventsPendingEnabled);
                        break;
                    case CommandKind.Stop:
                        ApplyStop(ref layer, ref registry, command, boundsDirtyEnabled);
                        break;
                    case CommandKind.SetSpeed:
                        layer.speed = command.speed;
                        break;
                    case CommandKind.SetTime:
                        layer.time = command.time;
                        break;
                }
            }

            commands.Clear();
            animationCommandPendingEnabled.ValueRW = false;
        }

        /// <summary>
        /// Starts a clip on the layer, demoting whatever was playing into the crossfade source.
        /// </summary>
        /// <remarks>
        /// The queue slot is deliberately left alone. Play addresses the current slot; a game that
        /// set up "swing, then return to idle" and then interrupts the swing with a stagger still
        /// wants the idle afterwards. Stop is the request that means "and nothing after this", and
        /// it is the one that clears the queue.
        /// </remarks>
        private static void ApplyPlay(
            ref PlaybackLayer layer,
            ref ClipRegistryBlob registry,
            in AnimationCommand command,
            ref DynamicBuffer<AnimEventOutput> animEvents,
            EnabledRefRW<AnimEventsPending> animEventsPendingEnabled,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
        {
            if (!ClipRegistryUtil.TryResolveClip(ref registry, command.clip, out int incomingClipIndex))
            {
                EmitResolveFailure(ref animEvents, animEventsPendingEnabled, command);
                return;
            }

            ref ClipBlob incomingClip = ref registry.clips[incomingClipIndex];

            // NaN means "no opinion", which resolves to the clip's authored blend-in. 0 is a
            // different, reachable answer — a hard cut — so the two must never collapse together.
            float blendDuration = math.isnan(command.blendDuration)
                ? math.max(incomingClip.defaultBlendIn, 0f)
                : math.max(command.blendDuration, 0f);

            int outgoingClipIndex = layer.clipIndex;
            bool isLayerActive = (layer.flags & PlaybackFlags.Active) != 0;
            bool hasOutgoingClip = isLayerActive && outgoingClipIndex >= 0;

            if (blendDuration > 0f)
            {
                if (hasOutgoingClip)
                {
                    layer.previousClip = layer.clip;
                    layer.previousClipIndex = outgoingClipIndex;
                    layer.previousTime = layer.time;
                    layer.previousSpeed = layer.speed;

                    // Section 5.2's whole reason for previousLoop: the mode the outgoing clip was
                    // ACTUALLY playing under, captured before the line below destroys it. Moving
                    // this assignment after `layer.loop = command.loop` compiles, runs, and makes
                    // every crossfade out of a Once-played clip wrap to zero instead of holding.
                    layer.previousLoop = layer.loop;
                }
                else if (!isLayerActive || layer.previousClipIndex < 0)
                {
                    // Nothing to fade from on this layer, so fade in from the pose the layers below
                    // composited (amendment A32). ClipSampler.CompositeLayers already reads an empty
                    // previous slot as "lerp from the incoming pose", which is exactly a layer
                    // easing in over the ones beneath it.
                    ClearPreviousSlot(ref layer);
                }

                // A layer stopped with a fade keeps its outgoing clip in the previous slot, so a
                // Play arriving mid-fade crossfades out of it rather than dropping it — the branch
                // above deliberately leaves that case alone.
                layer.blendElapsed = 0f;
                layer.blendDuration = blendDuration;
                layer.flags |= PlaybackFlags.Blending;
            }
            else
            {
                ClearBlendSource(ref layer);
            }

            layer.clip = command.clip;
            layer.clipIndex = incomingClipIndex;
            layer.speed = command.speed;
            layer.loop = command.loop;

            // Reverse playback starts at the end, or the first advance would immediately clamp a
            // Once clip and report it finished before a single frame of it was shown.
            layer.time = command.speed < 0f ? incomingClip.duration : 0f;
            layer.advanceStartTime = layer.time;

            layer.flags |= PlaybackFlags.Active;
            layer.flags &= ~(PlaybackFlags.Finished | PlaybackFlags.FinishedThisFrame);

            if (outgoingClipIndex != incomingClipIndex)
            {
                boundsDirtyEnabled.ValueRW = true;
            }
        }

        /// <summary>
        /// Stores a clip in the layer's one-deep queue slot, to be promoted when the current clip
        /// finishes.
        /// </summary>
        /// <remarks>
        /// The clip is resolved here even though only the promotion needs its index, for two
        /// reasons: a NaN blend has to be resolved against the incoming clip's authored blend-in,
        /// and a game that queues a clip belonging to another set should learn about it when it
        /// queues, not seconds later when the current clip happens to end (amendment A29).
        /// </remarks>
        private static void ApplyQueue(
            ref PlaybackLayer layer,
            ref ClipRegistryBlob registry,
            in AnimationCommand command,
            ref DynamicBuffer<AnimEventOutput> animEvents,
            EnabledRefRW<AnimEventsPending> animEventsPendingEnabled)
        {
            if (!ClipRegistryUtil.TryResolveClip(ref registry, command.clip, out int queuedClipIndex))
            {
                EmitResolveFailure(ref animEvents, animEventsPendingEnabled, command);
                return;
            }

            ref ClipBlob queuedClip = ref registry.clips[queuedClipIndex];

            layer.queuedClip = command.clip;
            layer.queuedSpeed = command.speed;
            layer.queuedLoop = command.loop;
            layer.queuedBlend = math.isnan(command.blendDuration)
                ? math.max(queuedClip.defaultBlendIn, 0f)
                : math.max(command.blendDuration, 0f);
            layer.flags |= PlaybackFlags.HasQueued;
        }

        /// <summary>
        /// Stops the layer, either at once or by fading the current clip out to nothing.
        /// </summary>
        private static void ApplyStop(
            ref PlaybackLayer layer,
            ref ClipRegistryBlob registry,
            in AnimationCommand command,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
        {
            int outgoingClipIndex = layer.clipIndex;
            bool hasOutgoingClip = (layer.flags & PlaybackFlags.Active) != 0 && outgoingClipIndex >= 0;

            float fadeDuration;
            if (math.isnan(command.blendDuration))
            {
                fadeDuration = hasOutgoingClip
                    ? math.max(registry.clips[outgoingClipIndex].defaultBlendOut, 0f)
                    : 0f;
            }
            else
            {
                fadeDuration = math.max(command.blendDuration, 0f);
            }

            // A stop cancels what was going to happen next, in both branches. Leaving a queued clip
            // behind would arm the layer to restart on its own the next time anything finished.
            ClearQueue(ref layer);

            if (hasOutgoingClip && fadeDuration > 0f)
            {
                layer.previousClip = layer.clip;
                layer.previousClipIndex = outgoingClipIndex;
                layer.previousTime = layer.time;
                layer.previousSpeed = layer.speed;
                layer.previousLoop = layer.loop;
                layer.blendElapsed = 0f;
                layer.blendDuration = fadeDuration;

                // Still Active: the layer has no current clip but is fading one out, and
                // PlaybackTimeSystem deactivates it when the fade completes.
                layer.flags |= PlaybackFlags.Active | PlaybackFlags.Blending;
                layer.flags &= ~(PlaybackFlags.Finished | PlaybackFlags.FinishedThisFrame);

                layer.clip = default;
                layer.clipIndex = -1;
                layer.time = 0f;
                layer.advanceStartTime = 0f;
                layer.speed = 0f;
                layer.loop = LoopMode.UseClipDefault;
            }
            else
            {
                ClearBlendSource(ref layer);
                layer.clip = default;
                layer.clipIndex = -1;
                layer.time = 0f;
                layer.advanceStartTime = 0f;
                layer.speed = 0f;
                layer.loop = LoopMode.UseClipDefault;
                layer.flags = PlaybackFlags.None;
            }

            if (outgoingClipIndex != layer.clipIndex)
            {
                boundsDirtyEnabled.ValueRW = true;
            }
        }

        /// <summary>Empties the crossfade-source slot and cancels any running blend.</summary>
        private static void ClearBlendSource(ref PlaybackLayer layer)
        {
            ClearPreviousSlot(ref layer);
            layer.blendElapsed = 0f;
            layer.blendDuration = 0f;
            layer.flags &= ~PlaybackFlags.Blending;
        }

        /// <summary>
        /// Empties the crossfade-source slot without touching the blend itself — an empty slot with
        /// a running blend is the "fade in from the layers below" state (amendment A32).
        /// </summary>
        private static void ClearPreviousSlot(ref PlaybackLayer layer)
        {
            layer.previousClip = default;
            layer.previousClipIndex = -1;
            layer.previousTime = 0f;
            layer.previousSpeed = 0f;
            layer.previousLoop = LoopMode.UseClipDefault;
        }

        /// <summary>Empties the one-deep queue slot.</summary>
        private static void ClearQueue(ref PlaybackLayer layer)
        {
            layer.queuedClip = default;
            layer.queuedSpeed = 0f;
            layer.queuedLoop = LoopMode.UseClipDefault;
            layer.queuedBlend = 0f;
            layer.flags &= ~PlaybackFlags.HasQueued;
        }

        /// <summary>
        /// Reports a Play/Queue whose clip id is not a member of this actor's registry, leaving the
        /// layer untouched (architecture section 5.4).
        /// </summary>
        /// <remarks>
        /// The id is carried through on the event so a listener can name the clip that failed. The
        /// alternative — dropping the request silently — is the failure mode this package has spent
        /// four gates removing: an animation that simply never plays, with no error and a plausible
        /// innocent explanation.
        /// </remarks>
        private static void EmitResolveFailure(
            ref DynamicBuffer<AnimEventOutput> animEvents,
            EnabledRefRW<AnimEventsPending> animEventsPendingEnabled,
            in AnimationCommand command)
        {
            animEvents.Add(new AnimEventOutput
            {
                eventKey = (uint)ReservedEventKeys.ClipResolveFailed,
                layerIndex = command.layerIndex,
                clip = command.clip,
                intParam = 0,
                floatParam = 0f
            });
            animEventsPendingEnabled.ValueRW = true;
        }
    }
}
