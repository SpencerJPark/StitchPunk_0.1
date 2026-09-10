// Copyright (c) 2026 Spencer Park. All rights reserved.
using System;
using System.IO;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Remembers the last folder an actor profile was saved to and resolves safe asset paths for new ones.
    /// </summary>
    public sealed class ActorProfileSaveLocation
    {
        public const string PrefsKey = "DotsAnimationToolkit.Profiles.SaveFolder";
        public const string DefaultAssetName = "NewActorProfile";

        public string FallbackFolder { get; set; } = "Assets";

        public string Recall()
        {
            string storedFolder = EditorPrefs.GetString(PrefsKey, string.Empty);
            return !string.IsNullOrEmpty(storedFolder) && AssetDatabase.IsValidFolder(storedFolder)
                ? storedFolder
                : FallbackFolder;
        }

        public void Remember(string projectRelativeFolder)
        {
            EditorPrefs.SetString(PrefsKey, projectRelativeFolder);
        }

        public static string SanitizeAssetName(string requestedName)
        {
            string trimmedName = requestedName != null ? requestedName.Trim() : string.Empty;
            char[] invalidFileNameCharacters = Path.GetInvalidFileNameChars();
            for (int characterIndex = 0; characterIndex < invalidFileNameCharacters.Length; characterIndex++)
            {
                trimmedName = trimmedName.Replace(invalidFileNameCharacters[characterIndex].ToString(), string.Empty);
            }
            trimmedName = trimmedName.Trim();
            return string.IsNullOrWhiteSpace(trimmedName) ? DefaultAssetName : trimmedName;
        }

        public static string ResolveTargetAssetPath(string folder, string requestedName)
        {
            return AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SanitizeAssetName(requestedName) + ".asset");
        }

        public static bool TryMakeProjectRelative(string absoluteFolder, string projectAssetsAbsolutePath, out string projectRelativeFolder)
        {
            string normalizedFolder = (absoluteFolder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            string normalizedAssetsPath = (projectAssetsAbsolutePath ?? string.Empty).Replace('\\', '/').TrimEnd('/');

            bool isExactMatch = string.Equals(normalizedFolder, normalizedAssetsPath, StringComparison.OrdinalIgnoreCase);
            bool isNestedMatch = normalizedFolder.StartsWith(normalizedAssetsPath + "/", StringComparison.OrdinalIgnoreCase);

            if (!isExactMatch && !isNestedMatch)
            {
                projectRelativeFolder = null;
                return false;
            }

            string remainder = isExactMatch ? string.Empty : normalizedFolder.Substring(normalizedAssetsPath.Length);
            projectRelativeFolder = "Assets" + remainder;
            return true;
        }
    }
}
