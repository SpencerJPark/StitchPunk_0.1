// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Sends a VAT texture set and the part files only it references to the OS trash.</summary>
    public static class VatTextureSetAssetUtility
    {
        private const string LogPrefix = "[DOTS Animation Toolkit] VAT textures: ";

        // Never SaveAssets: the clip set's vatTextures slot simply goes missing, which Health reports as unbaked.
        public static bool TrashTextureSet(VatTextureSetAsset set)
        {
            if (set == null)
            {
                return false;
            }

            string setPath = AssetDatabase.GetAssetPath(set);
            if (string.IsNullOrEmpty(setPath))
            {
                return false;
            }

            List<VatTextureSetAsset> allSets = new List<VatTextureSetAsset>(AssetReferenceIndex.VatTextureSets);
            List<Object> partFilesSafeToTrash = VatTextureOwnershipResolver.FindTexturesSafeToTrash(set, allSets);

            List<string> pathsToTrash = new List<string>();
            pathsToTrash.Add(setPath);
            foreach (Object partFile in partFilesSafeToTrash)
            {
                string partPath = AssetDatabase.GetAssetPath(partFile);
                // A sub-asset shares its set's path and leaves with it.
                if (string.IsNullOrEmpty(partPath) || pathsToTrash.Contains(partPath))
                {
                    continue;
                }

                pathsToTrash.Add(partPath);
            }

            bool everyPathMoved = true;
            foreach (string pathToTrash in pathsToTrash)
            {
                if (!AssetDatabase.MoveAssetToTrash(pathToTrash))
                {
                    everyPathMoved = false;
                    Debug.LogWarning(LogPrefix + "Could not move '" + pathToTrash + "' to the trash.");
                }
            }

            return everyPathMoved;
        }
    }
}
