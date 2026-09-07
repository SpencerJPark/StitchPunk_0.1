// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Creates a <see cref="RigAsset"/> from a scanned source prefab — the write path behind the
    /// New Rig flow.
    /// </summary>
    public static class RigAssetUtility
    {
        /// <summary>
        /// Creates a rig at <paramref name="assetPath"/>, points it at <paramref name="sourcePrefab"/>,
        /// and gives it one target per entry in <paramref name="targets"/>.
        /// </summary>
        /// <returns>The new rig, or null when the path is unusable.</returns>
        public static RigAsset CreateRig(
            string assetPath, GameObject sourcePrefab, List<RigTargetDefinition> targets)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            RigAsset newRig = ScriptableObject.CreateInstance<RigAsset>();
            newRig.sourcePrefab = sourcePrefab;
            if (targets != null)
            {
                newRig.targets.AddRange(targets);
            }

            // Must run after targets is populated — called on an empty list it mints nothing, and
            // a rig saved with every target id still 0 fails validation the moment a clip references it.
            newRig.EnsureStableIds();
            newRig.name = ExtractAssetName(assetPath);

            AssetDatabase.CreateAsset(newRig, assetPath);
            AssetDatabase.SaveAssets();

            // The minted ids are on disk now, so the "not yet persisted" report is discharged —
            // the same close-out ClipAssetUtility.CreateClipSet performs for a freshly minted set.
            newRig.MarkStableIdPersisted();

            return newRig;
        }

        /// <summary>Returns the file name of <paramref name="assetPath"/> without its extension.</summary>
        private static string ExtractAssetName(string assetPath)
        {
            int lastSeparatorIndex = assetPath.LastIndexOf('/');
            string fileName = lastSeparatorIndex >= 0
                ? assetPath.Substring(lastSeparatorIndex + 1)
                : assetPath;
            int extensionIndex = fileName.LastIndexOf('.');
            return extensionIndex > 0 ? fileName.Substring(0, extensionIndex) : fileName;
        }
    }
}
