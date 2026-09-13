// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public enum VatBakeFreshness : byte
    {
        Unbaked = 0,
        Fresh = 1,
        Stale = 2
    }

    /// <summary>
    /// Hashes a clip set's VAT-bound sources and a rig's VAT-relevant structure with FNV-1a, and
    /// compares them against a baked <see cref="VatTextureSetAsset"/> to say whether it is stale.
    /// </summary>
    public static class VatSourceHashResolver
    {
        private const ulong FnvOffsetBasis = 1469598103934665603UL;

        public static bool IsVatBound(ClipAsset clip)
        {
            if (clip == null)
            {
                return false;
            }
            if (clip.vatSource != null && clip.vatSource.sourceClip != null)
            {
                return true;
            }
            if (clip.vatTracks != null && clip.vatTracks.Count > 0)
            {
                return true;
            }
            if (clip.boneTracks != null && clip.boneTracks.Count > 0)
            {
                return true;
            }
            return false;
        }

        public static bool HasVatBoundClips(ClipSetAsset clipSet)
        {
            if (clipSet == null || clipSet.clips == null)
            {
                return false;
            }
            for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
            {
                if (IsVatBound(clipSet.clips[clipIndex]))
                {
                    return true;
                }
            }
            return false;
        }

        public static ulong ComputeClipsHash(ClipSetAsset clipSet)
        {
            ulong hash = FnvOffsetBasis;
            if (clipSet == null || clipSet.clips == null)
            {
                return Fold(hash, 0UL);
            }
            for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
            {
                ClipAsset clip = clipSet.clips[clipIndex];
                if (clip == null || !IsVatBound(clip))
                {
                    continue;
                }
                hash = Fold(hash, clip.Id.Value);
                hash = FoldFloat(hash, clip.duration);
                hash = FoldFloat(hash, clip.frameRate);
                hash = FoldVatClipSource(hash, clip.vatSource);

                int vatTrackCount = clip.vatTracks == null ? 0 : clip.vatTracks.Count;
                hash = Fold(hash, (ulong)vatTrackCount);
                for (int trackIndex = 0; trackIndex < vatTrackCount; trackIndex++)
                {
                    hash = FoldVatTrack(hash, clip.vatTracks[trackIndex]);
                }

                int boneTrackCount = clip.boneTracks == null ? 0 : clip.boneTracks.Count;
                hash = Fold(hash, (ulong)boneTrackCount);
                for (int trackIndex = 0; trackIndex < boneTrackCount; trackIndex++)
                {
                    hash = FoldBoneTrack(hash, clip.boneTracks[trackIndex]);
                }
            }
            return hash;
        }

        public static ulong ComputeRigStructureHash(RigAsset rig, VatFlavor flavor)
        {
            ulong hash = FnvOffsetBasis;
            if (rig == null)
            {
                return Fold(hash, 0UL);
            }
            hash = Fold(hash, rig.StableId);
            string prefabPath = rig.sourcePrefab == null ? string.Empty : AssetDatabase.GetAssetPath(rig.sourcePrefab);
            hash = FoldString(hash, string.IsNullOrEmpty(prefabPath) ? string.Empty : AssetDatabase.AssetPathToGUID(prefabPath));
            hash = Fold(hash, (ulong)flavor);

            int targetCount = rig.targets == null ? 0 : rig.targets.Count;
            hash = Fold(hash, (ulong)targetCount);
            for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null)
                {
                    hash = Fold(hash, 0UL);
                    continue;
                }
                hash = Fold(hash, target.Id.Value);
                hash = FoldString(hash, target.sourceNodePath);
                hash = Fold(hash, (ulong)target.kind);
            }

            if (rig.sockets != null)
            {
                for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
                {
                    SocketDefinition socket = rig.sockets[socketIndex];
                    if (socket != null && socket.mode == SocketAttachMode.Bone)
                    {
                        hash = Fold(hash, socket.Id.Value);
                        hash = FoldString(hash, socket.boneName);
                    }
                }
            }

            List<VatBakeSource> sources;
            string failureMessage;
            if (VatBakeSourceResolver.TryResolve(rig, out sources, out failureMessage))
            {
                for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    VatBakeSource source = sources[sourceIndex];
                    hash = Fold(hash, source.TargetId);
                    hash = FoldString(hash, source.SourceNodePath);
                    int elementCount;
                    if (flavor == VatFlavor.BoneMatrix)
                    {
                        elementCount = source.PrefabRenderer == null ? 0 : source.PrefabRenderer.bones.Length;
                    }
                    else
                    {
                        elementCount = source.PrefabRenderer == null || source.PrefabRenderer.sharedMesh == null
                            ? 0
                            : source.PrefabRenderer.sharedMesh.vertexCount;
                    }
                    hash = Fold(hash, (ulong)elementCount);
                }
            }
            else
            {
                hash = Fold(hash, 0xFFFFFFFFUL);
            }
            return hash;
        }

        public static ulong ComputeSourceHash(ClipSetAsset clipSet, RigAsset rig, VatFlavor flavor)
        {
            ulong hash = FnvOffsetBasis;
            hash = Fold(hash, ComputeClipsHash(clipSet));
            hash = Fold(hash, ComputeRigStructureHash(rig, flavor));
            return hash;
        }

        public static VatBakeFreshness Resolve(ClipSetAsset clipSet, RigAsset rig, VatTextureSetAsset textures, out string reason)
        {
            if (textures == null)
            {
                reason = "Not baked: this clip set has no VAT texture set. Bake it in the VAT Bake tab.";
                return VatBakeFreshness.Unbaked;
            }
            if (rig == null)
            {
                reason = "Rig changed: the rig this set was baked from is not in the project.";
                return VatBakeFreshness.Stale;
            }
            if (textures.sourceRigKey != 0 && textures.sourceRigKey != rig.StableId)
            {
                reason = "Rig changed: baked for a different rig than '" + rig.name + "'. Rebake it.";
                return VatBakeFreshness.Stale;
            }
            if (ComputeSourceHash(clipSet, rig, textures.flavor) == textures.sourceHash)
            {
                reason = "Up to date with its clips and rig.";
                return VatBakeFreshness.Fresh;
            }
            if (textures.sourceRigStructureHash == 0)
            {
                reason = "Sources changed since this set was baked. Rebake it.";
                return VatBakeFreshness.Stale;
            }
            if (ComputeRigStructureHash(rig, textures.flavor) != textures.sourceRigStructureHash)
            {
                reason = "Rig changed since this set was baked. Rebake it.";
                return VatBakeFreshness.Stale;
            }
            reason = "Clips changed since this set was baked. Rebake it.";
            return VatBakeFreshness.Stale;
        }

        private static ulong FoldVatClipSource(ulong hash, VatClipSource vatClipSource)
        {
            if (vatClipSource == null)
            {
                return Fold(hash, 0UL);
            }
            hash = FoldClipIdentity(hash, vatClipSource.sourceClip);
            hash = Fold(hash, vatClipSource.loopSafe ? 1UL : 0UL);
            return hash;
        }

        private static ulong FoldVatTrack(ulong hash, VatTrack vatTrack)
        {
            if (vatTrack == null)
            {
                return Fold(hash, 0UL);
            }
            hash = Fold(hash, vatTrack.targetId);
            hash = FoldClipIdentity(hash, vatTrack.sourceClip);
            hash = Fold(hash, vatTrack.loopSafe ? 1UL : 0UL);
            return hash;
        }

        private static ulong FoldBoneTrack(ulong hash, BoneTrack boneTrack)
        {
            if (boneTrack == null)
            {
                return Fold(hash, 0UL);
            }
            hash = FoldString(hash, boneTrack.boneName);
            int keyCount = boneTrack.keys == null ? 0 : boneTrack.keys.Count;
            hash = Fold(hash, (ulong)keyCount);
            for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
            {
                BoneKey boneKey = boneTrack.keys[keyIndex];
                hash = FoldFloat(hash, boneKey.normalizedTime);
                hash = FoldFloat(hash, boneKey.localPosition.x);
                hash = FoldFloat(hash, boneKey.localPosition.y);
                hash = FoldFloat(hash, boneKey.localPosition.z);
                hash = FoldFloat(hash, boneKey.localRotation.value.x);
                hash = FoldFloat(hash, boneKey.localRotation.value.y);
                hash = FoldFloat(hash, boneKey.localRotation.value.z);
                hash = FoldFloat(hash, boneKey.localRotation.value.w);
                hash = FoldFloat(hash, boneKey.localScale.x);
                hash = FoldFloat(hash, boneKey.localScale.y);
                hash = FoldFloat(hash, boneKey.localScale.z);
                hash = Fold(hash, (ulong)boneKey.interpolation);
                hash = FoldFloat(hash, boneKey.bezierStartHandle.x);
                hash = FoldFloat(hash, boneKey.bezierStartHandle.y);
                hash = FoldFloat(hash, boneKey.bezierEndHandle.x);
                hash = FoldFloat(hash, boneKey.bezierEndHandle.y);
            }
            return hash;
        }

        private static ulong FoldClipIdentity(ulong hash, AnimationClip animationClip)
        {
            if (animationClip == null)
            {
                return Fold(hash, 0UL);
            }
            hash = FoldString(hash, animationClip.name);
            hash = FoldFloat(hash, animationClip.length);
            string assetPath = AssetDatabase.GetAssetPath(animationClip);
            if (!string.IsNullOrEmpty(assetPath))
            {
                hash = FoldString(hash, AssetDatabase.AssetPathToGUID(assetPath));
                hash = FoldString(hash, AssetDatabase.GetAssetDependencyHash(assetPath).ToString());
            }
            return hash;
        }

        private static ulong FoldString(ulong hash, string value)
        {
            string text = value ?? string.Empty;
            for (int charIndex = 0; charIndex < text.Length; charIndex++)
            {
                hash = Fold(hash, text[charIndex]);
            }
            return Fold(hash, (ulong)text.Length);
        }

        private static ulong FoldFloat(ulong hash, float value)
        {
            return Fold(hash, (ulong)(uint)BitConverter.SingleToInt32Bits(value));
        }

        private static ulong Fold(ulong hash, ulong value)
        {
            return (hash ^ value) * 1099511628211UL;
        }
    }
}
