// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Checks rule T4 (V37, Phase E target-tags spec §6): a clip referenced by more than one
    /// <see cref="ClipSetAsset"/> that still binds one of its tracks by target id rather than by tag
    /// — the rule that turns "my shared clip does nothing on the second character" into a message at
    /// authoring time, instead of a silent mystery discovered on screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This cannot live in <see cref="ClipValidation"/>.</strong> That type validates one
    /// clip or one set in isolation (its own doc comment says so), but "how many sets reference this
    /// clip" is a fact about the whole project — the same reason
    /// <see cref="TargetTagBindingUtility"/>'s project-wide overloads live here instead of in the
    /// Authoring assembly, which has no <c>AssetDatabase</c> to ask.
    /// </para>
    /// <para>
    /// <strong>Split into a pure overload and a project-scanning one</strong>, the same shape
    /// <see cref="TargetTagBindingUtility"/> already uses: <see cref="CountReferencingClipSets"/>
    /// does the counting against whatever set list it is handed, with no asset database in the way,
    /// so an EditMode fixture can exercise it directly; <see cref="ValidateSharedClipBinding(ClipAsset)"/>
    /// is the thin entry point that finds every <see cref="ClipSetAsset"/> in the project and hands
    /// the list to the pure logic.
    /// </para>
    /// </remarks>
    public static class SharedClipBindingUtility
    {
        /// <summary>
        /// Counts how many of <paramref name="clipSets"/> list <paramref name="clip"/>, each set
        /// counted at most once regardless of how many times it repeats the clip (rule V11 already
        /// covers a set repeating its own clip; this rule is about distinct sets).
        /// </summary>
        /// <param name="clip">The clip to search for. Null always counts 0.</param>
        /// <param name="clipSets">The sets to search. Null, or a null entry within it, contributes 0.</param>
        /// <returns>The number of distinct sets that reference the clip.</returns>
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

        /// <summary>
        /// Validates <paramref name="clip"/> against T4 (V37) given an explicit list of the project's
        /// clip sets — the pure overload a test exercises directly, with no asset database access.
        /// </summary>
        /// <param name="clip">The clip to check. Null reports nothing.</param>
        /// <param name="clipSets">Every clip set in the project (or, in a test, every clip set the
        /// fixture cares about).</param>
        /// <returns>
        /// One V37 warning per <see cref="TransformTrack"/> or <see cref="SpriteTrack"/> that still
        /// binds by target id (<c>tagId == 0</c>, <c>targetId != 0</c>), when the clip is referenced
        /// by more than one set. Empty when the clip is referenced by at most one set, or every
        /// track that names a real target is already tag-bound.
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

        /// <summary>
        /// Validates <paramref name="clip"/> against every <see cref="ClipSetAsset"/> the project's
        /// asset database can find — the entry point a live inspector or the Clip Editor's
        /// validation badge actually calls.
        /// </summary>
        /// <param name="clip">The clip to check.</param>
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
