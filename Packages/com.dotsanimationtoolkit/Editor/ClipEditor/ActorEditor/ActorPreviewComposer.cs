// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Previews a whole <see cref="ActorProfileAsset"/>, not one clip: every layer composited,
    /// triggered and advanced through the same statics <c>PlaybackTimeSystem</c>/
    /// <c>CommandApplySystem</c> use, turning through the profile's directions and raising ragdoll
    /// events for the host panel to apply. Owns no ragdoll state of its own beyond bookkeeping.
    /// </summary>
    public sealed class ActorPreviewComposer : IDisposable
    {
        private BlobAssetReference<ActorProfileBlob> profileBlob;
        private NativeArray<PlaybackLayer> layers;
        private ActorProfileAsset profile;
        private IActorPosePresenter presenter;

        private Direction facing = Direction.SouthEast;
        private Direction appliedFacing = Direction.SouthEast;

        private bool ragdollOn;
        private uint ragdollStartedByKey;

        /// <summary>Raised once per marker crossing or at-play trigger that starts the ragdoll; carries the animation key that triggered it.</summary>
        public event Action<uint> RagdollStartRequested;

        /// <summary>Raised once per marker crossing or at-play trigger that stops the ragdoll; carries the animation key that triggered it.</summary>
        public event Action<uint> RagdollStopRequested;

        public Direction Facing
        {
            get { return facing; }
            set { facing = value; }
        }

        public bool RagdollOn
        {
            get { return ragdollOn; }
        }

        public uint RagdollStartedByKey
        {
            get { return ragdollStartedByKey; }
        }

        /// <summary>Whether <see cref="SetProfile"/> has built a layer array to advance.</summary>
        public bool IsCreated
        {
            get { return layers.IsCreated; }
        }

        /// <summary>
        /// Builds the profile blob, hands the presenter the profile's clip sets, and seeds every
        /// layer from its starter exactly as <c>ActorBaker</c> would (resolved at
        /// <see cref="Direction.SouthEast"/>).
        /// </summary>
        public void SetProfile(ActorProfileAsset profileAsset, IActorPosePresenter presenter)
        {
            DisposeBlobAndLayers();

            profile = profileAsset;
            this.presenter = presenter;
            facing = Direction.SouthEast;
            appliedFacing = Direction.SouthEast;
            ragdollOn = false;
            ragdollStartedByKey = 0u;

            if (profileAsset == null || presenter == null)
            {
                return;
            }

            profileBlob = ActorProfileBuilder.Build(profileAsset, Allocator.Persistent);
            presenter.SetClipSets(profileAsset.clipSets);

            int layerCount = profileAsset.layers == null ? 0 : profileAsset.layers.Count;
            layers = new NativeArray<PlaybackLayer>(layerCount, Allocator.Persistent);
            SeedStartingLayers(presenter.Registry);
        }

        /// <summary>Puts every layer back at its starter (or inactive), ragdoll off, facing back to South East.</summary>
        public void Reset()
        {
            if (!layers.IsCreated)
            {
                return;
            }

            facing = Direction.SouthEast;
            appliedFacing = Direction.SouthEast;
            ragdollOn = false;
            ragdollStartedByKey = 0u;
            SeedStartingLayers(presenter != null ? presenter.Registry : default);
        }

        /// <summary>
        /// Advances every layer, fires ragdoll triggers whose marker was crossed this tick, re-picks
        /// directional layers on a facing change, then hands the presenter the composited pose.
        /// </summary>
        public void Tick(float deltaTime, IActorPosePresenter presenter)
        {
            this.presenter = presenter;
            if (!layers.IsCreated || !profileBlob.IsCreated || presenter == null)
            {
                return;
            }

            BlobAssetReference<ClipRegistryBlob> registryReference = presenter.Registry;
            if (!registryReference.IsCreated)
            {
                return;
            }

            ref ClipRegistryBlob registry = ref registryReference.Value;

            NativeList<int> crossedEventIndices = new NativeList<int>(4, Allocator.Temp);
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                PlaybackLayer layer = layers[layerIndex];
                PlaybackTimeMath.Advance(ref layer, ref registry, deltaTime, out bool _);
                DetectAtEventRagdollTrigger(ref registry, in layer, ref crossedEventIndices);
                layers[layerIndex] = layer;
            }
            crossedEventIndices.Dispose();

            if (facing != appliedFacing)
            {
                RepickDirectionalLayers(ref registry);
                appliedFacing = facing;
            }

            FacingResolver.ToAuthoredSide(facing, out Direction _, out bool mirrorX);
            presenter.SampleCompositedPose(in layers, mirrorX);
        }

        /// <summary>
        /// Resolves <paramref name="animationKey"/> against the current facing and starts it on its
        /// own layer, applying the same state change <c>CommandApplySystem.ApplyPlayAnimation</c>
        /// makes at play, including an at-play ragdoll trigger.
        /// </summary>
        /// <returns>False when the key or its resolved clip does not resolve; the layer is still stamped with the key regardless, matching the runtime's own quirk.</returns>
        public bool PlayAnimation(uint animationKey)
        {
            if (!layers.IsCreated || !profileBlob.IsCreated || presenter == null)
            {
                return false;
            }

            BlobAssetReference<ClipRegistryBlob> registryReference = presenter.Registry;
            if (!registryReference.IsCreated)
            {
                return false;
            }

            ref ActorProfileBlob profileBlobValue = ref profileBlob.Value;
            if (!ActorProfileApi.TryResolve(
                    ref profileBlobValue, animationKey, facing,
                    out byte layerIndex, out ClipId resolvedClip, out int animationIndex))
            {
                return false;
            }

            if (layerIndex >= layers.Length)
            {
                return false;
            }

            ref ClipRegistryBlob registry = ref registryReference.Value;
            ref ActorAnimationBlob entry = ref profileBlobValue.animations[animationIndex];

            // A trigger-only entry (no clip) is a request, never a play — same rule as the runtime.
            if (!resolvedClip.IsValid)
            {
                if (entry.ragdollTrigger != RagdollTrigger.None && entry.ragdollAtEventKey == 0u)
                {
                    RaiseRagdollTrigger(entry.ragdollTrigger, animationKey);
                }
                return true;
            }

            PlaybackLayer targetLayer = layers[layerIndex];
            bool played = PlaybackCommandMath.ApplyPlay(
                ref targetLayer, ref registry, resolvedClip, entry.speed, entry.loop, entry.blendIn, out bool _);
            targetLayer.animationKey = animationKey; // stamped unconditionally, matching CommandApplySystem.ApplyPlayAnimation
            layers[layerIndex] = targetLayer;

            if (entry.ragdollTrigger != RagdollTrigger.None && entry.ragdollAtEventKey == 0u)
            {
                RaiseRagdollTrigger(entry.ragdollTrigger, animationKey);
            }

            return played;
        }

        /// <summary>Stops the entry's layer, only if that layer is still playing this exact key — a no-op otherwise, matching <c>CommandApplySystem.ApplyStopAnimation</c>.</summary>
        public bool StopAnimation(uint animationKey)
        {
            if (!layers.IsCreated || !profileBlob.IsCreated || presenter == null)
            {
                return false;
            }

            BlobAssetReference<ClipRegistryBlob> registryReference = presenter.Registry;
            if (!registryReference.IsCreated)
            {
                return false;
            }

            ref ActorProfileBlob profileBlobValue = ref profileBlob.Value;
            if (!ActorProfileApi.TryFindAnimation(ref profileBlobValue, animationKey, out int animationIndex))
            {
                return false;
            }

            ref ActorAnimationBlob entry = ref profileBlobValue.animations[animationIndex];
            if (entry.layerIndex >= layers.Length)
            {
                return false;
            }

            PlaybackLayer targetLayer = layers[entry.layerIndex];
            if (targetLayer.animationKey != animationKey)
            {
                return false;
            }

            ref ClipRegistryBlob registry = ref registryReference.Value;
            PlaybackCommandMath.ApplyStop(ref targetLayer, ref registry, float.NaN, out bool _);
            layers[entry.layerIndex] = targetLayer;
            return true;
        }

        /// <summary>Whether the named entry's layer is active and still stamped with its key.</summary>
        public bool IsAnimationPlaying(uint animationKey)
        {
            if (!layers.IsCreated || !profileBlob.IsCreated || animationKey == 0u)
            {
                return false;
            }

            ref ActorProfileBlob profileBlobValue = ref profileBlob.Value;
            if (!ActorProfileApi.TryFindAnimation(ref profileBlobValue, animationKey, out int animationIndex))
            {
                return false;
            }

            ref ActorAnimationBlob entry = ref profileBlobValue.animations[animationIndex];
            if (entry.layerIndex >= layers.Length)
            {
                return false;
            }

            PlaybackLayer layer = layers[entry.layerIndex];
            return layer.animationKey == animationKey && (layer.flags & PlaybackFlags.Active) != 0;
        }

        public float LayerTime(int layerIndex)
        {
            return IsValidLayerIndex(layerIndex) ? layers[layerIndex].time : 0f;
        }

        /// <summary>Scrubs a layer directly, re-snapshotting <c>timeAtFrameStart</c> so the next tick's event window opens exactly here.</summary>
        public void SetLayerTime(int layerIndex, float time)
        {
            if (!IsValidLayerIndex(layerIndex))
            {
                return;
            }

            PlaybackLayer layer = layers[layerIndex];
            layer.time = time;
            layer.timeAtFrameStart = time;
            layers[layerIndex] = layer;
        }

        public ClipId LayerClip(int layerIndex)
        {
            return IsValidLayerIndex(layerIndex) ? layers[layerIndex].clip : default;
        }

        public PlaybackFlags LayerFlags(int layerIndex)
        {
            return IsValidLayerIndex(layerIndex) ? layers[layerIndex].flags : PlaybackFlags.None;
        }

        public uint LayerAnimationKey(int layerIndex)
        {
            return IsValidLayerIndex(layerIndex) ? layers[layerIndex].animationKey : 0u;
        }

        /// <summary>The playing clip's duration in seconds, or 0 when the layer has none.</summary>
        public float ClipDuration(int layerIndex)
        {
            if (!IsValidLayerIndex(layerIndex) || presenter == null)
            {
                return 0f;
            }

            BlobAssetReference<ClipRegistryBlob> registryReference = presenter.Registry;
            int clipIndex = layers[layerIndex].clipIndex;
            if (!registryReference.IsCreated || clipIndex < 0 || clipIndex >= registryReference.Value.clips.Length)
            {
                return 0f;
            }

            return registryReference.Value.clips[clipIndex].duration;
        }

        public void Dispose()
        {
            DisposeBlobAndLayers();
        }

        private bool IsValidLayerIndex(int layerIndex)
        {
            return layers.IsCreated && layerIndex >= 0 && layerIndex < layers.Length;
        }

        private void DisposeBlobAndLayers()
        {
            if (profileBlob.IsCreated)
            {
                profileBlob.Dispose();
                profileBlob = default;
            }
            if (layers.IsCreated)
            {
                layers.Dispose();
            }
        }

        // Mirrors ActorBaker.AddPlaybackLayers/SeedStartingLayers: every layer starts at "no clip",
        // then a layer whose startingAnimationKey resolves gets that entry playing from time 0,
        // active regardless of defaultActive. Silent on a resolve failure — the panel's validation
        // badge is what surfaces a malformed starter, not this seed path.
        private void SeedStartingLayers(BlobAssetReference<ClipRegistryBlob> registryReference)
        {
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                layers[layerIndex] = new PlaybackLayer
                {
                    clipIndex = -1,
                    previousClipIndex = -1,
                    speed = 1f,
                    previousSpeed = 1f,
                    loop = LoopMode.UseClipDefault,
                    previousLoop = LoopMode.UseClipDefault,
                    flags = PlaybackFlags.None
                };
            }

            if (profile == null || profile.layers == null || !registryReference.IsCreated || !profileBlob.IsCreated)
            {
                return;
            }

            ref ClipRegistryBlob registry = ref registryReference.Value;
            ref ActorProfileBlob profileBlobValue = ref profileBlob.Value;

            for (int layerIndex = 0; layerIndex < profile.layers.Count; layerIndex++)
            {
                ActorLayerDefinition layerDefinition = profile.layers[layerIndex];
                if (layerDefinition == null || layerDefinition.startingAnimationKey == 0u)
                {
                    continue;
                }

                if (!ActorProfileApi.TryResolve(
                        ref profileBlobValue, layerDefinition.startingAnimationKey, Direction.SouthEast,
                        out byte resolvedLayerIndex, out ClipId resolvedClip, out int animationIndex))
                {
                    continue;
                }

                if (resolvedLayerIndex >= layers.Length
                    || !ClipRegistryApi.TryResolveClip(ref registry, resolvedClip, out int clipIndex))
                {
                    continue;
                }

                ref ActorAnimationBlob animationBlob = ref profileBlobValue.animations[animationIndex];
                PlaybackLayer seededLayer = layers[resolvedLayerIndex];
                seededLayer.clip = resolvedClip;
                seededLayer.clipIndex = clipIndex;
                seededLayer.time = 0f;
                seededLayer.speed = animationBlob.speed;
                seededLayer.loop = animationBlob.loop;
                seededLayer.flags |= PlaybackFlags.Active;
                layers[resolvedLayerIndex] = seededLayer;
            }
        }

        // Mirrors ActorFacingRepickSystem's rule: a directional entry's layer swaps its clip in
        // place (time, speed, loop and blend slots untouched) when the resolved clip differs from
        // what is already playing.
        private void RepickDirectionalLayers(ref ClipRegistryBlob registry)
        {
            ref ActorProfileBlob profileBlobValue = ref profileBlob.Value;

            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                PlaybackLayer layer = layers[layerIndex];
                if (layer.animationKey == 0u || (layer.flags & PlaybackFlags.Active) == 0)
                {
                    continue;
                }

                if (!ActorProfileApi.TryFindAnimation(ref profileBlobValue, layer.animationKey, out int animationIndex))
                {
                    continue;
                }

                ref ActorAnimationBlob entry = ref profileBlobValue.animations[animationIndex];
                if (!entry.hasDirections)
                {
                    continue;
                }

                if (!ActorProfileApi.TryResolve(
                        ref profileBlobValue, layer.animationKey, facing,
                        out byte _, out ClipId resolvedClip, out int _))
                {
                    continue;
                }

                if (resolvedClip == layer.clip)
                {
                    continue;
                }

                if (!ClipRegistryApi.TryResolveClip(ref registry, resolvedClip, out int resolvedClipIndex))
                {
                    continue;
                }

                layer.clip = resolvedClip;
                layer.clipIndex = resolvedClipIndex;
                layers[layerIndex] = layer;
            }
        }

        // Mirrors EventEmissionSystem's own crossing window (timeAtFrameStart to time), but only
        // for the one entry whose ragdollAtEventKey names a marker actually on this clip's timeline.
        private void DetectAtEventRagdollTrigger(
            ref ClipRegistryBlob registry, in PlaybackLayer layer, ref NativeList<int> crossedEventIndices)
        {
            if (layer.animationKey == 0u || layer.clipIndex < 0 || layer.clipIndex >= registry.clips.Length)
            {
                return;
            }

            ref ActorProfileBlob profileBlobValue = ref profileBlob.Value;
            if (!ActorProfileApi.TryFindAnimation(ref profileBlobValue, layer.animationKey, out int animationIndex))
            {
                return;
            }

            ref ActorAnimationBlob entry = ref profileBlobValue.animations[animationIndex];
            if (entry.ragdollTrigger == RagdollTrigger.None || entry.ragdollAtEventKey == 0u)
            {
                return;
            }

            ref ClipBlob clip = ref registry.clips[layer.clipIndex];
            if (clip.events.Length == 0)
            {
                return;
            }

            LoopMode resolvedLoopMode = ClipSampler.ResolveLoopMode(layer.loop, clip.defaultLoop);
            crossedEventIndices.Clear();
            EventWrapMath.CollectCrossings(
                ref clip.events, layer.timeAtFrameStart, layer.time, clip.duration, resolvedLoopMode,
                ref crossedEventIndices);

            for (int crossingIndex = 0; crossingIndex < crossedEventIndices.Length; crossingIndex++)
            {
                if (clip.events[crossedEventIndices[crossingIndex]].eventKey == entry.ragdollAtEventKey)
                {
                    RaiseRagdollTrigger(entry.ragdollTrigger, layer.animationKey);
                    return;
                }
            }
        }

        private void RaiseRagdollTrigger(RagdollTrigger trigger, uint animationKey)
        {
            if (trigger == RagdollTrigger.Start)
            {
                ragdollOn = true;
                ragdollStartedByKey = animationKey;
                RagdollStartRequested?.Invoke(animationKey);
            }
            else if (trigger == RagdollTrigger.Stop)
            {
                ragdollOn = false;
                ragdollStartedByKey = 0u;
                RagdollStopRequested?.Invoke(animationKey);
            }
        }
    }
}
