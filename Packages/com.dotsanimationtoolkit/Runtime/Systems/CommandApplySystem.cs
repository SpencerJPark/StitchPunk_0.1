// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs first in <see cref="AnimationToolkitLogicSystemGroup"/>: clears last frame's events
    /// before applying queued commands. <c>EventEmissionSystem</c> runs after this system and would
    /// otherwise erase resolve-failure events raised in the same frame.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitLogicSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct CommandApplySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // Two independent reasons to run — stale events or pending commands — so a single
            // RequireForUpdate (AND semantics) would skip both jobs whenever either side was empty.
            EntityQuery pendingCommandQuery = SystemAPI.QueryBuilder()
                .WithAll<AnimationCommandPending, AnimationCommand, PlaybackLayer, ClipRegistry, ActorProfile, ActorFacing>()
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

            ApplyAnimationCommandsJob applyJob = new ApplyAnimationCommandsJob
            {
                ragdollRequestLookup = SystemAPI.GetComponentLookup<ActorRagdollRequest>()
            };
            state.Dependency = applyJob.ScheduleParallel(state.Dependency);
        }
    }

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

    // WithPresent, not WithAll: an EnabledRefRW<T> parameter defaults T into an All query filter,
    // which would silently restrict this job to actors whose bounds/events already happened to be
    // dirty/pending — no error, just commands that quietly never apply.
    [BurstCompile]
    [WithAll(typeof(AnimationCommandPending), typeof(ActorProfile), typeof(ActorFacing))]
    [WithPresent(typeof(BoundsDirty), typeof(AnimEventsPending))]
    internal partial struct ApplyAnimationCommandsJob : IJobEntity
    {
        // Every actor bakes ActorRagdollRequest disabled (opt-in RagdollActor may not even be
        // present), so PlayAnimation writes it through a lookup on its own entity rather than an
        // Execute parameter, keeping the write local to the one command kind that needs it.
        [NativeDisableParallelForRestriction] public ComponentLookup<ActorRagdollRequest> ragdollRequestLookup;

        private void Execute(
            Entity actorEntity,
            ref DynamicBuffer<PlaybackLayer> layers,
            ref DynamicBuffer<AnimationCommand> commands,
            ref DynamicBuffer<AnimEventOutput> animEvents,
            in ClipRegistry clipRegistry,
            in ActorProfile actorProfile,
            in ActorFacing actorFacing,
            EnabledRefRW<AnimationCommandPending> animationCommandPendingEnabled,
            EnabledRefRW<AnimEventsPending> animEventsPendingEnabled,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
        {
            // Copied to a local first: `clipRegistry` is an `in` parameter, and reaching through it
            // to a ref-returning property on a non-readonly struct is the kind of construct that
            // compiles into a defensive copy. The local removes the question entirely.
            BlobAssetReference<ClipRegistryBlob> registryReference = clipRegistry.Value;
            ref ClipRegistryBlob registry = ref registryReference.Value;

            // Same defensive-copy reasoning as the registry above. Not every actor's profile has
            // baked content yet (a hand-built or profile-less test actor may carry an uncreated
            // reference), so PlayAnimation/StopAnimation guard on IsCreated before dereferencing.
            BlobAssetReference<ActorProfileBlob> profileReference = actorProfile.Value;

            for (int commandIndex = 0; commandIndex < commands.Length; commandIndex++)
            {
                AnimationCommand command = commands[commandIndex];

                // A layer index that no longer exists is a routine consequence of swapping a rig for
                // one with fewer layers, not a corrupt asset. The command is dropped, no event fires.
                // PlayAnimation/StopAnimation resolve their own layer from the profile instead, so
                // this bound only guards the raw, layer-addressed command kinds below.
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
                    case CommandKind.PlayAnimation:
                        if (profileReference.IsCreated)
                        {
                            ApplyPlayAnimation(
                                actorEntity,
                                ref layers,
                                ref registry,
                                ref profileReference.Value,
                                actorFacing.facing,
                                command,
                                ref animEvents,
                                animEventsPendingEnabled,
                                boundsDirtyEnabled,
                                ref ragdollRequestLookup);
                        }
                        else
                        {
                            EmitResolveFailure(
                                ref animEvents, animEventsPendingEnabled, 0, new ClipId(command.animationKey));
                        }
                        break;
                    case CommandKind.StopAnimation:
                        if (profileReference.IsCreated)
                        {
                            ApplyStopAnimation(
                                ref layers, ref registry, ref profileReference.Value, command, boundsDirtyEnabled);
                        }
                        break;
                }
            }

            commands.Clear();
            animationCommandPendingEnabled.ValueRW = false;
        }

        /// <summary>
        /// Starts a clip on the layer, demoting whatever was playing into the crossfade source. The
        /// queue slot is left alone — Play addresses only the current slot; Stop is what clears the queue.
        /// </summary>
        private static void ApplyPlay(
            ref PlaybackLayer layer,
            ref ClipRegistryBlob registry,
            in AnimationCommand command,
            ref DynamicBuffer<AnimEventOutput> animEvents,
            EnabledRefRW<AnimEventsPending> animEventsPendingEnabled,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
        {
            if (!ClipRegistryApi.TryResolveClip(ref registry, command.clip, out int incomingClipIndex))
            {
                EmitResolveFailure(ref animEvents, animEventsPendingEnabled, command.layerIndex, command.clip);
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
                    layer.previousLoop = layer.loop; // must run before layer.loop is overwritten below, or a crossfading Once clip wraps instead of holding
                }
                else if (!isLayerActive || layer.previousClipIndex < 0)
                {
                    // Nothing to fade from on this layer, so fade in from the pose the layers below
                    // composited. ClipSampler.CompositeLayers reads an empty previous slot as "lerp
                    // from the incoming pose", which is exactly a layer easing in over the ones beneath it.
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

            // Zeroed unconditionally: ApplyPlayAnimation re-stamps its own key right after calling
            // this for a PlayAnimation command, so a raw Play always ends up with 0 either way.
            layer.animationKey = 0u;

            // Reverse playback starts at the end, or the first advance would immediately clamp a
            // Once clip and report it finished before a single frame of it was shown.
            layer.time = command.speed < 0f ? incomingClip.duration : 0f;
            layer.timeAtFrameStart = layer.time;

            layer.flags |= PlaybackFlags.Active;
            layer.flags &= ~(PlaybackFlags.Finished | PlaybackFlags.FinishedThisFrame);

            if (outgoingClipIndex != incomingClipIndex)
            {
                boundsDirtyEnabled.ValueRW = true;
            }
        }

        /// <summary>
        /// Stores a clip in the layer's one-deep queue slot, to be promoted when the current clip
        /// finishes. Resolved eagerly, even though only the promotion needs the index, so a NaN
        /// blend resolves against the incoming clip's own default and a bad clip id is reported at
        /// queue time rather than seconds later.
        /// </summary>
        private static void ApplyQueue(
            ref PlaybackLayer layer,
            ref ClipRegistryBlob registry,
            in AnimationCommand command,
            ref DynamicBuffer<AnimEventOutput> animEvents,
            EnabledRefRW<AnimEventsPending> animEventsPendingEnabled)
        {
            if (!ClipRegistryApi.TryResolveClip(ref registry, command.clip, out int queuedClipIndex))
            {
                EmitResolveFailure(ref animEvents, animEventsPendingEnabled, command.layerIndex, command.clip);
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

        /// <summary>Stops the layer, either at once or by fading the current clip out to nothing.</summary>
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
                layer.timeAtFrameStart = 0f;
                layer.speed = 0f;
                layer.loop = LoopMode.UseClipDefault;
            }
            else
            {
                ClearBlendSource(ref layer);
                layer.clip = default;
                layer.clipIndex = -1;
                layer.time = 0f;
                layer.timeAtFrameStart = 0f;
                layer.speed = 0f;
                layer.loop = LoopMode.UseClipDefault;
                layer.flags = PlaybackFlags.None;
                layer.animationKey = 0u; // the layer deactivates on the spot, so the key clears with it
            }

            if (outgoingClipIndex != layer.clipIndex)
            {
                boundsDirtyEnabled.ValueRW = true;
            }
        }

        private static void ClearBlendSource(ref PlaybackLayer layer)
        {
            ClearPreviousSlot(ref layer);
            layer.blendElapsed = 0f;
            layer.blendDuration = 0f;
            layer.flags &= ~PlaybackFlags.Blending;
        }

        // An empty previous slot with a running blend is the "fade in from the layers below" state;
        // this clears the slot without touching the blend itself.
        private static void ClearPreviousSlot(ref PlaybackLayer layer)
        {
            layer.previousClip = default;
            layer.previousClipIndex = -1;
            layer.previousTime = 0f;
            layer.previousSpeed = 0f;
            layer.previousLoop = LoopMode.UseClipDefault;
        }

        private static void ClearQueue(ref PlaybackLayer layer)
        {
            layer.queuedClip = default;
            layer.queuedSpeed = 0f;
            layer.queuedLoop = LoopMode.UseClipDefault;
            layer.queuedBlend = 0f;
            layer.flags &= ~PlaybackFlags.HasQueued;
        }

        /// <summary>
        /// Resolves a named entry from the actor's profile against its current facing and starts it
        /// on the entry's own layer, ignoring <see cref="AnimationCommand.layerIndex"/>. An unresolved
        /// key is reported and every layer is left untouched.
        /// </summary>
        private static void ApplyPlayAnimation(
            Entity actorEntity,
            ref DynamicBuffer<PlaybackLayer> layers,
            ref ClipRegistryBlob registry,
            ref ActorProfileBlob profileBlob,
            Direction facing,
            in AnimationCommand command,
            ref DynamicBuffer<AnimEventOutput> animEvents,
            EnabledRefRW<AnimEventsPending> animEventsPendingEnabled,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled,
            ref ComponentLookup<ActorRagdollRequest> ragdollRequestLookup)
        {
            if (!ActorProfileApi.TryResolve(
                    ref profileBlob,
                    command.animationKey,
                    facing,
                    out byte layerIndex,
                    out ClipId resolvedClip,
                    out int animationIndex))
            {
                // The unresolved key is carried in the event's clip field so it renders as hex
                // (ClipId.ToString uses X16) wherever the event is later inspected.
                EmitResolveFailure(ref animEvents, animEventsPendingEnabled, 0, new ClipId(command.animationKey));
                return;
            }

            // A stale profile/layer-count mismatch, same routine-drop treatment as an out-of-range
            // raw layerIndex above.
            if (layerIndex >= layers.Length)
            {
                return;
            }

            ref ActorAnimationBlob entry = ref profileBlob.animations[animationIndex];
            AnimationCommand effectivePlay = new AnimationCommand
            {
                kind = CommandKind.Play,
                layerIndex = layerIndex,
                clip = resolvedClip,
                speed = math.isnan(command.speed) ? entry.speed : command.speed,
                loop = command.loop == LoopMode.UseClipDefault ? entry.loop : command.loop,
                blendDuration = math.isnan(command.blendDuration) ? entry.blendIn : command.blendDuration,
                time = 0f
            };

            ref PlaybackLayer targetLayer = ref layers.ElementAt(layerIndex);
            ApplyPlay(ref targetLayer, ref registry, effectivePlay, ref animEvents, animEventsPendingEnabled, boundsDirtyEnabled);
            targetLayer.animationKey = command.animationKey;

            if (entry.ragdollTrigger != RagdollTrigger.None
                && entry.ragdollAtEventKey == 0u
                && ragdollRequestLookup.HasComponent(actorEntity))
            {
                ragdollRequestLookup[actorEntity] = new ActorRagdollRequest { trigger = entry.ragdollTrigger };
                ragdollRequestLookup.SetComponentEnabled(actorEntity, true);
            }
        }

        /// <summary>
        /// Stops the entry's layer only if that layer is still playing this exact key — a Stop for an
        /// animation that already moved on, or never started, is a no-op rather than a plain layer stop.
        /// </summary>
        private static void ApplyStopAnimation(
            ref DynamicBuffer<PlaybackLayer> layers,
            ref ClipRegistryBlob registry,
            ref ActorProfileBlob profileBlob,
            in AnimationCommand command,
            EnabledRefRW<BoundsDirty> boundsDirtyEnabled)
        {
            if (!ActorProfileApi.TryFindAnimation(ref profileBlob, command.animationKey, out int animationIndex))
            {
                return;
            }

            ref ActorAnimationBlob entry = ref profileBlob.animations[animationIndex];
            if (entry.layerIndex >= layers.Length)
            {
                return;
            }

            ref PlaybackLayer targetLayer = ref layers.ElementAt(entry.layerIndex);
            if (targetLayer.animationKey != command.animationKey)
            {
                return;
            }

            AnimationCommand effectiveStop = new AnimationCommand
            {
                kind = CommandKind.Stop,
                layerIndex = entry.layerIndex,
                clip = default,
                speed = 0f,
                loop = LoopMode.UseClipDefault,
                blendDuration = command.blendDuration,
                time = 0f
            };
            ApplyStop(ref targetLayer, ref registry, effectiveStop, boundsDirtyEnabled);
        }

        /// <summary>Reports a Play/Queue/PlayAnimation whose clip or key does not resolve, leaving the layer untouched.</summary>
        private static void EmitResolveFailure(
            ref DynamicBuffer<AnimEventOutput> animEvents,
            EnabledRefRW<AnimEventsPending> animEventsPendingEnabled,
            byte layerIndex,
            ClipId clip)
        {
            animEvents.Add(new AnimEventOutput
            {
                eventKey = (uint)ReservedEventKeys.ClipResolveFailed,
                layerIndex = layerIndex,
                clip = clip,
                intParam = 0,
                floatParam = 0f
            });
            animEventsPendingEnabled.ValueRW = true;
        }
    }
}
