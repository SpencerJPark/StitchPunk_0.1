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
