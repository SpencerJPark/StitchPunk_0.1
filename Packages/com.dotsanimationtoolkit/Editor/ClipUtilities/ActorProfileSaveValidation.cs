// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Warns in the console when a saved actor profile names an animation missing from the
    /// Animation Names registry; never blocks the save.
    /// </summary>
    public sealed class ActorProfileSaveValidation : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            for (int pathIndex = 0; pathIndex < paths.Length; pathIndex++)
            {
                string path = paths[pathIndex];
                if (path == null)
                {
                    continue;
                }

                if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(ActorProfileAsset))
                {
                    continue;
                }

                ActorProfileAsset profile = AssetDatabase.LoadAssetAtPath<ActorProfileAsset>(path);
                if (profile == null)
                {
                    continue;
                }

                List<ValidationMessage> messages = ProfileP2Scan.ScanProfile(profile);
                for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
                {
                    ValidationMessage message = messages[messageIndex];
                    Debug.LogWarning(ProfileP2Scan.FormatForConsole(profile, message), profile);
                }
            }

            return paths;
        }
    }
}
