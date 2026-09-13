// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Raises <see cref="AssetsImported"/> once after each import batch, including the reimport that
    /// saving a clip or rig causes, which <c>EditorApplication.projectChanged</c> never reports.
    /// </summary>
    public sealed class VatSourceImportWatcher : AssetPostprocessor
    {
        public static event Action AssetsImported;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            // Deferred and coalesced: listeners hash through the AssetDatabase, which is safer outside
            // the import callback, and a burst of batches should refresh once.
            EditorApplication.delayCall -= RaiseAssetsImported;
            EditorApplication.delayCall += RaiseAssetsImported;
        }

        private static void RaiseAssetsImported()
        {
            if (AssetsImported != null)
            {
                AssetsImported();
            }
        }
    }
}
