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
    /// <remarks>
    /// The pane has no menu entry of its own. One entry path — the Clip Editor's toolbar toggle, or
    /// the asset itself — is one thing to document and one place a set can be open, rather than two
    /// surfaces that can disagree about which set that is.
    /// </remarks>
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
