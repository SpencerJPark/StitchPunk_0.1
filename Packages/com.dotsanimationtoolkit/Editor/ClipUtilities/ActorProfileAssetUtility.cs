// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Creates, renames and trashes <see cref="ActorProfileAsset"/> assets — the write path
    /// behind the Actor Editor's profile catalog.
    /// </summary>
    public static class ActorProfileAssetUtility
    {
        /// <summary>
        /// Writes an empty profile with both bookends at <paramref name="assetPath"/>.
        /// </summary>
        /// <returns>The new profile, or null when the path is unusable.</returns>
        public static ActorProfileAsset CreateProfile(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            ActorProfileAsset newProfile = ScriptableObject.CreateInstance<ActorProfileAsset>();

            // Must run before the asset is saved — a profile with a missing bookend or a still-zero
            // stable id fails validation the moment the Actor Editor or a clip references it.
            newProfile.EnsureBookends();
            newProfile.EnsureStableIds();
            newProfile.name = ExtractAssetName(assetPath);

            AssetDatabase.CreateAsset(newProfile, assetPath);
            AssetDatabase.SaveAssets();

            // The minted id is on disk now, so the "not yet persisted" report is discharged —
            // the same close-out RigAssetUtility.CreateRig performs for a freshly minted rig.
            newProfile.MarkStableIdPersisted();

            return newProfile;
        }

        /// Renames the profile's asset on disk. False when the profile is null, the name is empty
        /// or unchanged, or the rename fails.
        public static bool RenameProfile(ActorProfileAsset profile, string newName)
        {
            if (profile == null || string.IsNullOrWhiteSpace(newName) || profile.name == newName)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            // Returns a message rather than throwing when the name is illegal or already taken.
            string failureReason = AssetDatabase.RenameAsset(assetPath, newName);
            if (!string.IsNullOrEmpty(failureReason))
            {
                Debug.LogWarning(
                    "[DOTS Animation Toolkit] Actor Editor: Could not rename profile to '" + newName +
                    "': " + failureReason, profile);
                return false;
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        /// Moves the profile's asset to the OS trash. False when the profile is null or unsaved.
        // Trashed rather than deleted outright: anything still referencing this profile can only be
        // recovered if the asset stays reachable from the OS trash.
        public static bool TrashProfile(ActorProfileAsset profile)
        {
            if (profile == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            if (!AssetDatabase.MoveAssetToTrash(assetPath))
            {
                Debug.LogWarning(
                    "[DOTS Animation Toolkit] Actor Editor: Could not move profile '" + assetPath +
                    "' to the trash.", profile);
                return false;
            }

            return true;
        }

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
