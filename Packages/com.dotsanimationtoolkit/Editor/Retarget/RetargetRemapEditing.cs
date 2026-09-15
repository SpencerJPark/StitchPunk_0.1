// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Rewrites one track's tag in one clip, with undo. A track that would duplicate another
    /// track's tag merges into it instead, the same rule the timeline's tag picker applies.
    /// </summary>
    public static class RetargetRemapEditing
    {
        public const string RemapTrackTagUndoName = "Remap Track Tag";

        public static bool RemapTrackTag(ClipAsset clip, RetargetTrackKind kind, int trackIndex, uint newTagId)
        {
            if (clip == null || newTagId == 0u)
            {
                return false;
            }

            if (kind == RetargetTrackKind.Transform)
            {
                if (clip.transformTracks == null || trackIndex < 0 || trackIndex >= clip.transformTracks.Count)
                {
                    return false;
                }

                TransformTrack transformTrack = clip.transformTracks[trackIndex];
                if (transformTrack == null || transformTrack.tagId == newTagId)
                {
                    return false;
                }

                int destinationIndex = -1;
                for (int otherIndex = 0; otherIndex < clip.transformTracks.Count; otherIndex++)
                {
                    if (otherIndex != trackIndex && clip.transformTracks[otherIndex] != null
                        && clip.transformTracks[otherIndex].tagId == newTagId)
                    {
                        destinationIndex = otherIndex;
                        break;
                    }
                }

                RecordClip(clip);
                if (destinationIndex >= 0)
                {
                    ClipComponentModel.MergeTransformTracks(transformTrack, clip.transformTracks[destinationIndex]);
                    clip.transformTracks.RemoveAt(trackIndex);
                }
                else
                {
                    transformTrack.tagId = newTagId;
                }

                FinishEdit(clip);
                return true;
            }

            if (kind == RetargetTrackKind.Sprite)
            {
                if (clip.spriteTracks == null || trackIndex < 0 || trackIndex >= clip.spriteTracks.Count)
                {
                    return false;
                }

                SpriteTrack spriteTrack = clip.spriteTracks[trackIndex];
                if (spriteTrack == null || spriteTrack.tagId == newTagId)
                {
                    return false;
                }

                int destinationIndex = -1;
                for (int otherIndex = 0; otherIndex < clip.spriteTracks.Count; otherIndex++)
                {
                    if (otherIndex != trackIndex && clip.spriteTracks[otherIndex] != null
                        && clip.spriteTracks[otherIndex].tagId == newTagId)
                    {
                        destinationIndex = otherIndex;
                        break;
                    }
                }

                RecordClip(clip);
                if (destinationIndex >= 0)
                {
                    ClipComponentModel.MergeSpriteTracks(spriteTrack, clip.spriteTracks[destinationIndex]);
                    clip.spriteTracks.RemoveAt(trackIndex);
                }
                else
                {
                    spriteTrack.tagId = newTagId;
                }

                FinishEdit(clip);
                return true;
            }

            return false;
        }

        public const string AddTagToRigPartUndoName = "Add Tag To Rig Part";

        public static bool AddTagToRigPart(RigAsset rig, uint targetStableId, uint tagId, out string failureMessage)
        {
            failureMessage = string.Empty;

            if (rig == null)
            {
                failureMessage = "No rig selected.";
                return false;
            }

            if (rig.targets == null)
            {
                failureMessage = "No rig selected.";
                return false;
            }

            if (tagId == 0u)
            {
                failureMessage = "That track carries no tag.";
                return false;
            }

            // Only one target may wear a given tag; SetTargetTag does not enforce that, so it lives here.
            RigTargetDefinition existingWearer = ClipComponentModel.FindTargetByTag(rig, tagId);
            if (existingWearer != null && existingWearer.Id.Value != targetStableId)
            {
                failureMessage = "\"" + existingWearer.displayName + "\" already wears this tag.";
                return false;
            }

            bool targetStillInRig = false;
            foreach (RigTargetDefinition candidateTarget in rig.targets)
            {
                if (candidateTarget != null && candidateTarget.Id.Value == targetStableId)
                {
                    targetStillInRig = true;
                    break;
                }
            }

            if (!targetStillInRig)
            {
                failureMessage = "That rig part is no longer in the rig.";
                return false;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(AddTagToRigPartUndoName);
            bool didWrite = RigAssetUtility.SetTargetTag(rig, targetStableId, tagId);
            if (!didWrite)
            {
                failureMessage = "Could not write the tag to the rig part.";
                return false;
            }

            AssetReferenceIndex.MarkDirty();
            return true;
        }

        private static void RecordClip(ClipAsset clip)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(RemapTrackTagUndoName);
            Undo.RecordObject(clip, RemapTrackTagUndoName);
        }

        private static void FinishEdit(ClipAsset clip)
        {
            EditorUtility.SetDirty(clip);
            AssetReferenceIndex.MarkDirty();
        }
    }
}
