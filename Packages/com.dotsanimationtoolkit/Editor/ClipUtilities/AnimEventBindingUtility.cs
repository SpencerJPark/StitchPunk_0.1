// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Counts how many event markers use a given <see cref="AnimEventKeyEntry"/>'s key, so
    /// <see cref="AnimEventKeyRegistryEditor"/> can show that count before a delete. Unlike a tag
    /// id, an event key is not validated — removing its registry row only leaves it unresolved.
    /// </summary>
    public static class AnimEventBindingUtility
    {
        /// <param name="eventKey">0 always counts 0 — reserved, never a real event key.</param>
        /// <param name="clips">Null, or a null entry within it, contributes 0.</param>
        public static int CountMarkerBindings(uint eventKey, IReadOnlyList<ClipAsset> clips)
        {
            if (eventKey == 0u || clips == null)
            {
                return 0;
            }

            int bindingCount = 0;
            for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                ClipAsset clip = clips[clipIndex];
                if (clip == null || clip.events == null)
                {
                    continue;
                }
                for (int eventIndex = 0; eventIndex < clip.events.Count; eventIndex++)
                {
                    // EventMarker is a struct (never null); the list itself can only be null, and
                    // that is already guarded above.
                    if (clip.events[eventIndex].eventKey == eventKey)
                    {
                        bindingCount++;
                    }
                }
            }
            return bindingCount;
        }

        /// <param name="entry">Null, or an entry still at the reserved 0 key, counts 0.</param>
        public static int CountMarkerBindings(AnimEventKeyEntry entry)
        {
            if (entry == null || entry.eventKey == 0u)
            {
                return 0;
            }
            return CountMarkerBindings(entry.eventKey, FindAllClipAssetsInProject());
        }

        public static int CountBoundClips(uint eventKey, IReadOnlyList<ClipAsset> clips)
        {
            if (eventKey == 0u || clips == null)
            {
                return 0;
            }

            int clipCount = 0;
            for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                ClipAsset clip = clips[clipIndex];
                if (clip == null || clip.events == null)
                {
                    continue;
                }
                for (int eventIndex = 0; eventIndex < clip.events.Count; eventIndex++)
                {
                    if (clip.events[eventIndex].eventKey == eventKey)
                    {
                        clipCount++;
                        break;
                    }
                }
            }
            return clipCount;
        }

        /// <summary>Project-wide entry point for <see cref="CountBoundClips(uint, IReadOnlyList{ClipAsset})"/>.</summary>
        public static int CountBoundClips(AnimEventKeyEntry entry)
        {
            if (entry == null || entry.eventKey == 0u)
            {
                return 0;
            }
            return CountBoundClips(entry.eventKey, FindAllClipAssetsInProject());
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
