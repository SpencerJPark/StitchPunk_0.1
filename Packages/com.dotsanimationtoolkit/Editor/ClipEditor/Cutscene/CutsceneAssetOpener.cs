// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Makes double-clicking a <see cref="CutsceneAsset"/> open the Clip Editor on its Cutscene Editor tab with that asset loaded.</summary>
    /// <remarks>Mirrors <c>DirectionSetAssetOpener</c> — one entry path per asset kind, never a second surface that can disagree about which one is open.</remarks>
    internal static class CutsceneAssetOpener
    {
        [OnOpenAsset]
        private static bool OnOpenCutsceneAsset(EntityId entityId, int line)
        {
            CutsceneAsset openedCutscene = EditorUtility.EntityIdToObject(entityId) as CutsceneAsset;
            if (openedCutscene == null)
            {
                return false;
            }

            ClipEditorWindow.FocusCutsceneTab(openedCutscene);
            return true;
        }
    }
}
