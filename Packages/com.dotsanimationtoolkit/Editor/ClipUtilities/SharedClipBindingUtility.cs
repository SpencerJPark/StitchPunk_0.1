// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Flags a clip referenced by more than one <see cref="ClipSetAsset"/> that still binds one of
    /// its tracks by target id rather than by tag, so a shared clip that does nothing on the second
    /// character is a message at authoring time, not a silent mystery on screen.
    /// </summary>
    public static class SharedClipBindingUtility
    {
        // Each set counts at most once regardless of how many times it repeats the clip.
        /// <param name="clip">Null always counts 0.</param>
        /// <param name="clipSets">Null, or a null entry within it, contributes 0.</param>
        public static int CountReferencingClipSets(ClipAsset clip, IReadOnlyList<ClipSetAsset> clipSets)
        {
            if (clip == null || clipSets == null)
            {
                return 0;
            }

            int referencingSetCount = 0;
            for (int setIndex = 0; setIndex < clipSets.Count; setIndex++)
            {
                ClipSetAsset clipSet = clipSets[setIndex];
                if (clipSet == null || clipSet.clips == null)
                {
                    continue;
                }
                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    if (clipSet.clips[clipIndex] == clip)
                    {
                        referencingSetCount++;
                        break;
                    }
                }
            }
            return referencingSetCount;
        }

        /// <param name="clip">Null reports nothing.</param>
        /// <returns>
        /// One warning per <see cref="TransformTrack"/> or <see cref="SpriteTrack"/> that still
        /// binds by target id when the clip is referenced by more than one set; empty otherwise.
        /// </returns>
        public static List<ValidationMessage> ValidateSharedClipBinding(
            ClipAsset clip, IReadOnlyList<ClipSetAsset> clipSets)
        {
            List<ValidationMessage> messages = new List<ValidationMessage>();
            if (clip == null)
            {
                return messages;
            }

            int referencingSetCount = CountReferencingClipSets(clip, clipSets);
            if (referencingSetCount <= 1)
            {
                return messages;
            }

            AddNonTaggedTrackWarnings(clip, clip.transformTracks, "Transform track", referencingSetCount, messages);
            AddNonTaggedTrackWarnings(clip, clip.spriteTracks, "Sprite track", referencingSetCount, messages);
            return messages;
        }

        /// <returns>See <see cref="ValidateSharedClipBinding(ClipAsset, IReadOnlyList{ClipSetAsset})"/>.</returns>
        public static List<ValidationMessage> ValidateSharedClipBinding(ClipAsset clip)
        {
            return ValidateSharedClipBinding(clip, FindAllClipSetAssetsInProject());
        }

        private static void AddNonTaggedTrackWarnings(
            ClipAsset clip,
            List<TransformTrack> tracks,
            string trackKindLabel,
            int referencingSetCount,
            List<ValidationMessage> messages)
        {
            if (tracks == null)
            {
                return;
            }
            for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
            {
                TransformTrack track = tracks[trackIndex];
                if (track == null || track.tagId != 0u || track.targetId == 0u)
                {
                    continue;
                }
                messages.Add(BuildMessage(clip, trackKindLabel, trackIndex, referencingSetCount));
            }
        }

        private static void AddNonTaggedTrackWarnings(
            ClipAsset clip,
            List<SpriteTrack> tracks,
            string trackKindLabel,
            int referencingSetCount,
            List<ValidationMessage> messages)
        {
            if (tracks == null)
            {
                return;
            }
            for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
            {
                SpriteTrack track = tracks[trackIndex];
                if (track == null || track.tagId != 0u || track.targetId == 0u)
                {
                    continue;
                }
                messages.Add(BuildMessage(clip, trackKindLabel, trackIndex, referencingSetCount));
            }
        }

        private static ValidationMessage BuildMessage(
            ClipAsset clip, string trackKindLabel, int trackIndex, int referencingSetCount)
        {
            return new ValidationMessage(
                ValidationSeverity.Warning,
                ValidationCode.V37,
                clip,
                trackKindLabel + " " + trackIndex + " of clip '" + clip.name +
                "' is bound by target id, but the clip is referenced by " + referencingSetCount +
                " clip sets; it will not travel to any rig but the one that target id belongs to. " +
                "Bind it by tag instead to share it.");
        }

        private static List<ClipSetAsset> FindAllClipSetAssetsInProject()
        {
            List<ClipSetAsset> clipSets = new List<ClipSetAsset>();
            string[] clipSetAssetGuids = AssetDatabase.FindAssets("t:" + nameof(ClipSetAsset));
            for (int guidIndex = 0; guidIndex < clipSetAssetGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(clipSetAssetGuids[guidIndex]);
                ClipSetAsset clipSet = AssetDatabase.LoadAssetAtPath<ClipSetAsset>(assetPath);
                if (clipSet != null)
                {
                    clipSets.Add(clipSet);
                }
            }
            return clipSets;
        }
    }
}
