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
                    {
                        bool played = PlaybackCommandMath.ApplyPlay(
                            ref layer, ref registry, command.clip, command.speed, command.loop,
                            command.blendDuration, out bool playBoundsDirty);
                        if (!played)
                        {
                            EmitResolveFailure(ref animEvents, animEventsPendingEnabled, command.layerIndex, command.clip);
                        }
                        else if (playBoundsDirty)
                        {
                            boundsDirtyEnabled.ValueRW = true;
                        }
                        break;
                    }
                    case CommandKind.Queue:
                        ApplyQueue(
                            ref layer,
                            ref registry,
                            command,
                            ref animEvents,
                            animEventsPendingEnabled);
                        break;
                    case CommandKind.Stop:
                    {
                        PlaybackCommandMath.ApplyStop(ref layer, ref registry, command.blendDuration, out bool stopBoundsDirty);
                        if (stopBoundsDirty)
                        {
                            boundsDirtyEnabled.ValueRW = true;
                        }
                        break;
                    }
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

            // A trigger-only entry (no clip, a ragdoll Start/Stop) is a request, never a play: the
            // layer is left exactly as it is and nothing is reported as unresolved.
            if (!resolvedClip.IsValid)
            {
                if (entry.ragdollTrigger != RagdollTrigger.None
                    && entry.ragdollAtEventKey == 0u
                    && ragdollRequestLookup.HasComponent(actorEntity))
                {
                    ragdollRequestLookup[actorEntity] = new ActorRagdollRequest { trigger = entry.ragdollTrigger };
                    ragdollRequestLookup.SetComponentEnabled(actorEntity, true);
                }
                return;
            }

            float effectiveSpeed = math.isnan(command.speed) ? entry.speed : command.speed;
            LoopMode effectiveLoop = command.loop == LoopMode.UseClipDefault ? entry.loop : command.loop;
            float effectiveBlendDuration = math.isnan(command.blendDuration) ? entry.blendIn : command.blendDuration;

            ref PlaybackLayer targetLayer = ref layers.ElementAt(layerIndex);
            bool played = PlaybackCommandMath.ApplyPlay(
                ref targetLayer, ref registry, resolvedClip, effectiveSpeed, effectiveLoop,
                effectiveBlendDuration, out bool playBoundsDirty);
            if (!played)
            {
                EmitResolveFailure(ref animEvents, animEventsPendingEnabled, layerIndex, resolvedClip);
            }
            else if (playBoundsDirty)
            {
                boundsDirtyEnabled.ValueRW = true;
            }

            // Stamped unconditionally, even on the resolve failure reported above — pre-existing
            // behaviour, preserved rather than changed here.
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

            PlaybackCommandMath.ApplyStop(ref targetLayer, ref registry, command.blendDuration, out bool boundsDirty);
            if (boundsDirty)
            {
                boundsDirtyEnabled.ValueRW = true;
            }
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
