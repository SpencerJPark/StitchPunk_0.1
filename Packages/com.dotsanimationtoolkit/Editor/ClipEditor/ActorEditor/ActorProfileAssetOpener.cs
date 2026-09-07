// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Makes double-clicking an <see cref="ActorProfileAsset"/> open the Clip Editor on its Actor Editor tab with that profile loaded.</summary>
    // Mirrors CutsceneAssetOpener — one entry path per asset kind, never a second that can disagree about which one is open.
    internal static class ActorProfileAssetOpener
    {
        [OnOpenAsset]
        private static bool OnOpenActorProfileAsset(EntityId entityId, int line)
        {
            ActorProfileAsset openedProfile = EditorUtility.EntityIdToObject(entityId) as ActorProfileAsset;
            if (openedProfile == null)
            {
                return false;
            }

            ClipEditorWindow.FocusWithActorEditorTab(openedProfile);
            return true;
        }
    }
}
