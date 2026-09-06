// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Makes double-clicking a <see cref="DirectionSetAsset"/> open the Clip Editor with the 2D
    /// Direction Sets pane up and that set loaded.
    /// </summary>
    // The pane has no menu entry of its own — this and the Clip Editor's toolbar toggle are the only two entry paths.
    internal static class DirectionSetAssetOpener
    {
        [OnOpenAsset]
        private static bool OnOpenDirectionSetAsset(EntityId entityId, int line)
        {
            DirectionSetAsset openedSet =
                EditorUtility.EntityIdToObject(entityId) as DirectionSetAsset;
            if (openedSet == null)
            {
                return false;
            }

            ClipEditorWindow.FocusDirectionSetsTab(openedSet);
            return true;
        }
    }
}
