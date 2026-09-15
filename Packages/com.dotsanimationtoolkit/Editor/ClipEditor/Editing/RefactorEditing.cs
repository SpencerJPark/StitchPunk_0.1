// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Project-wide id changes: re-key or merge event keys, and move keyed tracks to another tag.
    /// Each operation is one undo group across every asset it touches.
    /// </summary>
    public static class RefactorEditing
    {
        public const string RekeyEventUndoName = "Re-key event";
        public const string MergeEventKeysUndoName = "Merge event keys";
        public const string ReplaceTrackTagUndoName = "Replace track tag";

        public static List<AssetReference> PreviewRekeyEvent(uint fromKey)
        {
            return new List<AssetReference>();
        }

        public static int RekeyEvent(uint fromKey, uint toKey)
        {
            return 0;
        }

        public static int MergeEventKeys(uint fromKey, uint intoKey)
        {
            return MergeEventKeys(fromKey, intoKey, VocabularyRegistryProvider.AnimEventKeys);
        }

        public static int MergeEventKeys(uint fromKey, uint intoKey, AnimEventKeyRegistry registry)
        {
            return 0;
        }

        public static List<AssetReference> PreviewReplaceTrackTag(uint fromTagId)
        {
            return new List<AssetReference>();
        }

        public static int ReplaceTrackTag(uint fromTagId, uint toTagId)
        {
            return 0;
        }
    }
}
