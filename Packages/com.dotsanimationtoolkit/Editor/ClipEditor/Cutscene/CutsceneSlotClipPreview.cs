// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// One cutscene slot's in-editor clip registry: the same <c>ClipRegistryBlob</c> the slot's
    /// (rig, clip sets) bind bakes for a real actor, built here so the Scene-view preview can sample
    /// clip blocks through <see cref="ClipSampler"/> (amendment A58 §3.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rebuilt on a bind change, never on a scrub</strong> — the Direction Sets pane's own
    /// guard is the model. Building is the expensive step; a scrub that rebuilt would hitch on every
    /// frame of a drag.
    /// </para>
    /// <para>
    /// The blob is <c>Persistent</c> and preview-scoped (decision A58-D2): entering preview builds,
    /// exiting disposes, nothing survives between preview sessions.
    /// </para>
    /// </remarks>
    internal sealed class CutsceneSlotClipPreview : IDisposable
    {
        private BlobAssetReference<ClipRegistryBlob> registry;
        private RigAsset boundRig;
        private readonly List<ClipSetAsset> boundClipSets = new List<ClipSetAsset>();
        private readonly List<ulong> boundClipIds = new List<ulong>();

        /// <summary>Why there is no registry, for the slot inspector. Empty when one built.</summary>
        public string StatusMessage { get; private set; } = string.Empty;

        public bool HasRegistry
        {
            get { return registry.IsCreated; }
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

        /// <summary>Rebuilds only when the slot's rig or clip-set membership has actually changed.</summary>
        public void RebuildIfBindChanged(RigAsset rig, List<ClipSetAsset> clipSets)
        {
            if (registry.IsCreated && boundRig == rig && SameBind(clipSets))
            {
                return;
            }

            Dispose();
            boundRig = rig;
            boundClipSets.Clear();
            if (clipSets != null)
            {
                boundClipSets.AddRange(clipSets);
            }
            CollectClipIds(clipSets, boundClipIds);

            if (rig == null)
            {
                StatusMessage = "Slot has no rig — clip blocks cannot be previewed.";
                return;
            }
            if (boundClipSets.Count == 0)
            {
                StatusMessage = "Slot has no clip sets — clip blocks cannot be previewed.";
                return;
            }

            try
            {
                Unity.Entities.Hash128 contentHash;
                ClipRegistryBuilder.Build(rig, boundClipSets, out registry, out contentHash);
                StatusMessage = string.Empty;
            }
            catch (ClipValidationException)
            {
                // Named, not listed: the clip set's own inspector renders the findings, and an
                // authoring window that dies while a clip is being fixed is useless.
                registry = default(BlobAssetReference<ClipRegistryBlob>);
                StatusMessage = "Clip set has validation errors — nothing can be previewed for this slot.";
            }
            catch (Exception buildException)
            {
                registry = default(BlobAssetReference<ClipRegistryBlob>);
                StatusMessage = buildException.Message;
            }
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

        public float GetClipDuration(int clipIndex)
        {
            return registry.Value.clips[clipIndex].duration;
        }

        /// <summary>Samples one part of one clip, exactly as <c>TransformSampleSystem</c> would.</summary>
        public void SamplePose(
            int clipIndex, int targetIndex, float normalizedTime, in TargetRestPose rest, out TargetPose pose)
        {
            ref ClipBlob clip = ref registry.Value.clips[clipIndex];
            ClipSampler.SamplePose(ref clip, targetIndex, normalizedTime, in rest, out pose);
        }

        /// <summary>Alt-view frames per variant for a target, which facing steps through (A58-T4).</summary>
        public int GetFramesPerVariant(int targetIndex)
        {
            ref ClipRegistryBlob registryBlob = ref registry.Value;
            return targetIndex < registryBlob.targetFramesPerVariant.Length
                ? registryBlob.targetFramesPerVariant[targetIndex]
                : 1;
        }

        public void Dispose()
        {
            if (registry.IsCreated)
            {
                registry.Dispose();
            }
            registry = default(BlobAssetReference<ClipRegistryBlob>);
        }

        /// <summary>
        /// Whether the bind is still the one the registry was built from — the same sets, holding the
        /// same clips.
        /// </summary>
        /// <remarks>
        /// The clips inside each set are compared, not just the set references: dragging a clip into
        /// a set from its inspector while the cutscene tab is open leaves the set reference identical
        /// and the registry one clip short, so the new block would preview nothing with no error
        /// anywhere. Key edits <em>inside</em> a clip are not compared and do not need to be — they
        /// happen on the Clip Editor tab, and switching tabs exits the preview and drops the
        /// registries anyway.
        /// </remarks>
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
