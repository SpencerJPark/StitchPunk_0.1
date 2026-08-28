// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Counts how many event markers use a given <see cref="AnimEventKeyEntry"/>'s key (amendment
    /// A55): the number <see cref="AnimEventKeyRegistryEditor"/> shows before a delete, mirroring
    /// <see cref="TargetTagBindingUtility"/>'s pair for the tag registry.
    /// </summary>
    /// <remarks>
    /// Deleting an event is not the same cost as deleting a tag. A tag's id is baked into a track's
    /// binding and its absence fails validation (rule T3); an event key is arithmetic a marker
    /// already carries, and removing its registry row only means the key can no longer be resolved
    /// to a name — the marker keeps firing the same number, forever. The confirmation dialog this
    /// utility feeds must say "shows as an unresolved key", never "fails validation".
    /// </remarks>
    public static class AnimEventBindingUtility
    {
        /// <summary>
        /// Counts, across <paramref name="clips"/>, the event markers whose <c>eventKey</c> equals
        /// <paramref name="eventKey"/>. Pure — no asset database access.
        /// </summary>
        /// <param name="eventKey">The event key to count bindings for. 0 always counts 0 — reserved,
        /// never a real event a marker would carry.</param>
        /// <param name="clips">The clips to search. Null, or a null entry within it, contributes 0.</param>
        /// <returns>The total number of matching markers.</returns>
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

        /// <summary>
        /// Counts <paramref name="entry"/>'s marker bindings across every <see cref="ClipAsset"/> the
        /// project's asset database can find. The entry point <see cref="AnimEventKeyRegistryEditor"/>
        /// actually calls.
        /// </summary>
        /// <param name="entry">The event entry about to be removed. Null, or an entry still at the
        /// reserved 0 key, counts 0.</param>
        /// <returns>The number of matching markers project-wide.</returns>
        public static int CountMarkerBindings(AnimEventKeyEntry entry)
        {
            if (entry == null || entry.eventKey == 0u)
            {
                return 0;
            }
            return CountMarkerBindings(entry.eventKey, FindAllClipAssetsInProject());
        }

        /// <summary>
        /// Counts, across <paramref name="clips"/>, how many distinct clips carry at least one marker
        /// with <paramref name="eventKey"/> — the delete-confirmation dialog's "across N clip(s)"
        /// half, alongside <see cref="CountMarkerBindings(uint, IReadOnlyList{ClipAsset})"/>'s marker
        /// total.
        /// </summary>
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

        /// <summary>
        /// Finds every <see cref="ClipAsset"/> in the project, whether it is a free-standing asset or
        /// a sub-asset of a <see cref="ClipSetAsset"/> file. See
        /// <see cref="TargetTagBindingUtility"/>'s identical helper for why
        /// <see cref="AssetDatabase.LoadAllAssetsAtPath"/> is required here instead of
        /// <c>LoadAssetAtPath&lt;ClipAsset&gt;</c>.
        /// </summary>
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
