// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// One cutscene slot's in-editor clip registry and actor profile: the same
    /// <c>ClipRegistryBlob</c>/<c>ActorProfileBlob</c> pair a real actor bakes, built here so the
    /// Scene-view preview can reconstruct a <see cref="PlaybackLayer"/> array per scrub and composite
    /// it through <see cref="ClipSampler"/>. Rebuilt on a bind change, never on a scrub — building is
    /// the expensive step. Both blobs and the layer array are <c>Persistent</c> and preview-scoped.
    /// </summary>
    internal sealed class CutsceneSlotClipPreview : IDisposable
    {
        private BlobAssetReference<ClipRegistryBlob> registry;
        private BlobAssetReference<ActorProfileBlob> profileBlob;
        private NativeArray<PlaybackLayer> layers;
        private ActorProfileAsset boundProfile;
        private RigAsset boundRig;
        private readonly List<ClipSetAsset> boundClipSets = new List<ClipSetAsset>();
        private readonly List<ulong> boundClipIds = new List<ulong>();

        /// <summary>Why there is no registry, for the slot inspector. Empty when one built.</summary>
        public string StatusMessage { get; private set; } = string.Empty;

        public bool HasRegistry
        {
            get { return registry.IsCreated; }
        }

        /// <summary>Whether a profile blob was built — the gate on any layer reconstruction.</summary>
        public bool HasProfile
        {
            get { return profileBlob.IsCreated; }
        }

        /// <summary>How many playback layers the bound profile defines. 0 without a profile.</summary>
        public int LayerCount
        {
            get { return layers.IsCreated ? layers.Length : 0; }
        }

        /// <summary>How many rig targets the registry resolves, which is the dense target index space.</summary>
        public int TargetCount
        {
            get { return registry.IsCreated ? registry.Value.sortedTargetIds.Length : 0; }
        }

        public uint GetTargetId(int targetIndex)
        {
            return registry.Value.sortedTargetIds[targetIndex];
        }

        /// <summary>Rebuilds only when the slot's profile — or the rig/clip sets it resolves to — has actually changed.</summary>
        public void RebuildIfBindChanged(ActorProfileAsset profile)
        {
            RigAsset rig = profile != null ? profile.rig : null;
            List<ClipSetAsset> clipSets = profile != null ? profile.clipSets : null;

            if (registry.IsCreated && boundProfile == profile && boundRig == rig && SameBind(clipSets))
            {
                return;
            }

            Dispose();
            boundProfile = profile;
            boundRig = rig;
            boundClipSets.Clear();
            if (clipSets != null)
            {
                boundClipSets.AddRange(clipSets);
            }
            CollectClipIds(clipSets, boundClipIds);

            if (profile == null)
            {
                StatusMessage = "Slot has no profile — clip blocks cannot be previewed.";
                return;
            }
            if (rig == null)
            {
                StatusMessage = "Profile has no rig — clip blocks cannot be previewed.";
                return;
            }
            if (boundClipSets.Count == 0)
            {
                StatusMessage = "Profile has no clip sets — clip blocks cannot be previewed.";
                return;
            }

            try
            {
                Unity.Entities.Hash128 contentHash;
                ClipRegistryBuilder.Build(rig, boundClipSets, out registry, out contentHash);
                profileBlob = ActorProfileBuilder.Build(profile, Allocator.Persistent);
                layers = new NativeArray<PlaybackLayer>(
                    profile.layers != null ? profile.layers.Count : 0, Allocator.Persistent);
                StatusMessage = string.Empty;
            }
            catch (ClipValidationException)
            {
                // Named, not listed: the profile's/clip set's own inspector renders the findings, and
                // an authoring window that dies while a clip is being fixed is useless.
                DisposeBuiltState();
                StatusMessage = "Profile or clip set has validation errors — nothing can be previewed for this slot.";
            }
            catch (Exception buildException)
            {
                DisposeBuiltState();
                StatusMessage = buildException.Message;
            }
        }

        // Either every built resource exists or none does — a partial build (say, a valid registry
        // but a profile that then failed validation) must not linger as a half-alive state.
        private void DisposeBuiltState()
        {
            if (registry.IsCreated)
            {
                registry.Dispose();
            }
            registry = default(BlobAssetReference<ClipRegistryBlob>);
            DisposeBlobs();
        }

        /// <summary>The registry's dense index for a clip id, or false when the slot's sets do not hold it.</summary>
        public bool TryGetClipIndex(ulong clipId, out int clipIndex)
        {
            clipIndex = -1;
            if (!registry.IsCreated || clipId == 0UL)
            {
                return false;
            }
            ref ClipRegistryBlob registryBlob = ref registry.Value;
            for (int index = 0; index < registryBlob.sortedClipIds.Length; index++)
            {
                if (registryBlob.sortedClipIds[index] == clipId)
                {
                    clipIndex = index;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The profile layer an animation key lives on, or false when the profile has no such key.</summary>
        public bool TryFindAnimationLayer(uint animationKey, out byte layerIndex)
        {
            layerIndex = 0;
            int animationIndex;
            if (!profileBlob.IsCreated
                || !ActorProfileApi.TryFindAnimation(ref profileBlob.Value, animationKey, out animationIndex))
            {
                return false;
            }
            layerIndex = profileBlob.Value.animations[animationIndex].layerIndex;
            return true;
        }

        /// <summary>The profile layer named <paramref name="layerName"/>, read from the authoring asset directly since a stop key names a layer by display name, never an index.</summary>
        public bool TryResolveLayerIndexByName(string layerName, out byte layerIndex)
        {
            layerIndex = 0;
            if (boundProfile == null || boundProfile.layers == null || string.IsNullOrEmpty(layerName))
            {
                return false;
            }
            for (int index = 0; index < boundProfile.layers.Count; index++)
            {
                ActorLayerDefinition layer = boundProfile.layers[index];
                if (layer != null && layer.displayName == layerName)
                {
                    layerIndex = (byte)index;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Resolves an animation key for a facing into its clip, loop and speed — the same fold <c>ActorProfileApi.TryResolve</c> gives a real actor.</summary>
        public bool TryResolveAnimation(
            uint animationKey, Direction facing, out byte layerIndex, out ClipId clip, out LoopMode loop, out float speed)
        {
            layerIndex = 0;
            clip = default;
            loop = LoopMode.UseClipDefault;
            speed = 1f;

            int animationIndex;
            if (!profileBlob.IsCreated || !ActorProfileApi.TryResolve(
                    ref profileBlob.Value, animationKey, facing, out layerIndex, out clip, out animationIndex))
            {
                return false;
            }
            ref ActorAnimationBlob animation = ref profileBlob.Value.animations[animationIndex];
            loop = animation.loop;
            speed = animation.speed;
            return true;
        }

        /// <summary>Overwrites one reconstructed layer. Pass <c>default</c> for an inactive layer.</summary>
        public void SetLayer(int layerIndex, in PlaybackLayer layer)
        {
            layers[layerIndex] = layer;
        }

        /// <summary>Composites every reconstructed layer for one target through the runtime's own compositor.</summary>
        public void CompositePose(int targetIndex, in TargetRestPose restPose, out TargetPose pose)
        {
            ClipSampler.CompositeLayers(ref registry.Value, in layers, targetIndex, in restPose, false, out pose);
        }

        public void Dispose()
        {
            DisposeBuiltState();
        }

        private void DisposeBlobs()
        {
            if (profileBlob.IsCreated)
            {
                profileBlob.Dispose();
            }
            profileBlob = default(BlobAssetReference<ActorProfileBlob>);
            if (layers.IsCreated)
            {
                layers.Dispose();
            }
            layers = default(NativeArray<PlaybackLayer>);
        }

        // Whether the bind is still the one the registry was built from — the same sets, holding the
        // same clips. The clips inside each set are compared, not just the set references: dragging
        // a clip into a set from its inspector leaves the set reference identical but the registry one clip short.
        private bool SameBind(List<ClipSetAsset> clipSets)
        {
            int setCount = clipSets != null ? clipSets.Count : 0;
            if (setCount != boundClipSets.Count)
            {
                return false;
            }
            for (int index = 0; index < setCount; index++)
            {
                if (clipSets[index] != boundClipSets[index])
                {
                    return false;
                }
            }

            CollectClipIds(clipSets, comparisonClipIds);
            if (comparisonClipIds.Count != boundClipIds.Count)
            {
                return false;
            }
            for (int index = 0; index < comparisonClipIds.Count; index++)
            {
                if (comparisonClipIds[index] != boundClipIds[index])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Reused so the membership check a scrub runs every tick allocates nothing.</summary>
        private readonly List<ulong> comparisonClipIds = new List<ulong>();

        private static void CollectClipIds(List<ClipSetAsset> clipSets, List<ulong> clipIds)
        {
            clipIds.Clear();
            for (int setIndex = 0; clipSets != null && setIndex < clipSets.Count; setIndex++)
            {
                ClipSetAsset clipSet = clipSets[setIndex];
                if (clipSet == null || clipSet.clips == null)
                {
                    continue;
                }
                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    ClipAsset clip = clipSet.clips[clipIndex];
                    clipIds.Add(clip != null ? clip.Id.Value : 0UL);
                }
            }
        }
    }
}
