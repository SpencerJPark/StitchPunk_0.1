// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Watches asset import/delete/move events and marks the reference index dirty.
    /// Does no rebuild work itself — the next query rescans.
    /// </summary>
    internal sealed class AssetReferenceIndexPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (AnyPathIsAsset(importedAssets) || AnyPathIsAsset(deletedAssets) || AnyPathIsAsset(movedAssets))
            {
                AssetReferenceIndex.MarkDirty();
            }
        }

        private static bool AnyPathIsAsset(string[] paths)
        {
            if (paths == null)
            {
                return false;
            }

            for (int pathIndex = 0; pathIndex < paths.Length; pathIndex++)
            {
                if (paths[pathIndex].EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
