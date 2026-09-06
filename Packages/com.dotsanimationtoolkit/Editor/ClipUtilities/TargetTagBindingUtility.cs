// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Counts how many rig targets and tracks bind a given <see cref="TargetTagEntry"/>'s id, so
    /// <see cref="TargetTagRegistryEditor"/> can show a delete's real cost before it happens.
    /// </summary>
    public static class TargetTagBindingUtility
    {
        /// <param name="tagId">0 ("untagged") always counts 0 — untagged is not a binding to anything.</param>
        /// <param name="rigs">Null, or a null entry within it, contributes 0.</param>
        public static int CountRigTargetBindings(uint tagId, IReadOnlyList<RigAsset> rigs)
        {
            if (tagId == 0u || rigs == null)
            {
                return 0;
            }

            int bindingCount = 0;
            for (int rigIndex = 0; rigIndex < rigs.Count; rigIndex++)
            {
                RigAsset rig = rigs[rigIndex];
                if (rig == null || rig.targets == null)
                {
                    continue;
                }
                for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
                {
                    RigTargetDefinition targetDefinition = rig.targets[targetIndex];
                    if (targetDefinition != null && targetDefinition.tagId == tagId)
                    {
                        bindingCount++;
                    }
                }
            }
            return bindingCount;
        }

        /// <param name="entry">Null, or an entry still at the reserved 0 id, counts 0.</param>
        public static int CountRigTargetBindings(TargetTagEntry entry)
        {
            if (entry == null || entry.stableId == 0u)
            {
                return 0;
            }
            return CountRigTargetBindings(entry.stableId, FindAllRigAssetsInProject());
        }

        private static List<RigAsset> FindAllRigAssetsInProject()
        {
            List<RigAsset> rigs = new List<RigAsset>();
            string[] rigAssetGuids = AssetDatabase.FindAssets("t:" + nameof(RigAsset));
            for (int guidIndex = 0; guidIndex < rigAssetGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(rigAssetGuids[guidIndex]);
                RigAsset rig = AssetDatabase.LoadAssetAtPath<RigAsset>(assetPath);
                if (rig != null)
                {
                    rigs.Add(rig);
                }
            }
            return rigs;
        }

        /// <param name="tagId">0 always counts 0 — it means "bind by target id instead", never a
        /// binding to a tag.</param>
        /// <param name="clips">Null, or a null entry within it, contributes 0.</param>
        public static int CountTrackBindings(uint tagId, IReadOnlyList<ClipAsset> clips)
        {
            if (tagId == 0u || clips == null)
            {
                return 0;
            }

            int bindingCount = 0;
            for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                ClipAsset clip = clips[clipIndex];
                if (clip == null)
                {
                    continue;
                }

                if (clip.transformTracks != null)
                {
                    for (int trackIndex = 0; trackIndex < clip.transformTracks.Count; trackIndex++)
                    {
                        TransformTrack track = clip.transformTracks[trackIndex];
                        if (track != null && track.tagId == tagId)
                        {
                            bindingCount++;
                        }
                    }
                }

                if (clip.spriteTracks != null)
                {
                    for (int trackIndex = 0; trackIndex < clip.spriteTracks.Count; trackIndex++)
                    {
                        SpriteTrack track = clip.spriteTracks[trackIndex];
                        if (track != null && track.tagId == tagId)
                        {
                            bindingCount++;
                        }
                    }
                }
            }
            return bindingCount;
        }

        /// <param name="entry">Null, or an entry still at the reserved 0 id, counts 0.</param>
        public static int CountTrackBindings(TargetTagEntry entry)
        {
            if (entry == null || entry.stableId == 0u)
            {
                return 0;
            }
            return CountTrackBindings(entry.stableId, FindAllClipAssetsInProject());
        }

        // Uses LoadAllAssetsAtPath, not LoadAssetAtPath<ClipAsset> — the latter would silently
        // miss a clip that is a sub-asset rather than its file's main object.
        private static List<ClipAsset> FindAllClipAssetsInProject()
        {
            List<ClipAsset> clips = new List<ClipAsset>();
            string[] clipAssetGuids = AssetDatabase.FindAssets("t:" + nameof(ClipAsset));
            HashSet<string> seenPaths = new HashSet<string>();
            for (int guidIndex = 0; guidIndex < clipAssetGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(clipAssetGuids[guidIndex]);
                if (string.IsNullOrEmpty(assetPath) || !seenPaths.Add(assetPath))
                {
                    continue;
                }
                UnityEngine.Object[] assetsAtPath = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                for (int assetIndex = 0; assetIndex < assetsAtPath.Length; assetIndex++)
                {
                    ClipAsset clip = assetsAtPath[assetIndex] as ClipAsset;
                    if (clip != null)
                    {
                        clips.Add(clip);
                    }
                }
            }
            return clips;
        }
    }
}
