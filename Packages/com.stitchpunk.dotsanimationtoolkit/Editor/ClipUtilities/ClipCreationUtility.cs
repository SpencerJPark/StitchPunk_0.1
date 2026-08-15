// Copyright (c) 2026 Stitch Punk. All rights reserved.

using StitchPunk.AnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// Mints a new <see cref="ClipAsset"/> into a <see cref="ClipSetAsset"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One creation path, two callers.</strong> The clip set's inspector and the clip
    /// editor's Clips pane both offer "new clip", and a clip created from one has to be
    /// indistinguishable from a clip created from the other — same folder, same rig, same id
    /// minting, same undo entry. Two implementations of that would agree on the day they were
    /// written and drift afterwards, and the symptom would be clips that fail validation depending
    /// on which button made them.
    /// </para>
    /// <para>
    /// <strong>The rig is inherited from the set, not left empty.</strong> Validation rule V06 only
    /// lets a clip join a set whose rig is the same asset, so a clip created with a null rig is born
    /// failing validation. Copying the set's rig is what makes a newly created clip immediately
    /// valid and immediately authorable.
    /// </para>
    /// <para>
    /// <strong>The asset write is not undoable, and deliberately is not made so.</strong> Ctrl+Z
    /// does not delete a file from disk — <c>MirrorClipUtility.CreateMirroredCopy</c> makes the same
    /// call. What is undoable, and what this wraps in one named group, is the append to
    /// <see cref="ClipSetAsset.clips"/>: driven through <see cref="SerializedProperty"/> and
    /// <see cref="SerializedObject.ApplyModifiedProperties"/>, which already records one undo entry
    /// per apply. The group name is what makes it read as "Create Clip In Set" in the Undo History
    /// rather than as a generic property change.
    /// </para>
    /// </remarks>
    public static class ClipCreationUtility
    {
        private const string LogPrefix = "[DOTS Animation Toolkit] ";
        private const string NewClipAssetBaseName = "NewClip";
        private const string AssetExtension = ".asset";
        private const string UndoActionName = "Create Clip In Set";

        /// <summary>
        /// Creates a clip beside <paramref name="clipSet"/> on disk, gives it the set's rig, and
        /// appends it to the set's clip list.
        /// </summary>
        /// <returns>The new clip, or null when the set has nowhere to write it.</returns>
        public static ClipAsset CreateClipInSet(ClipSetAsset clipSet)
        {
            if (clipSet == null)
            {
                return null;
            }

            string setAssetPath = AssetDatabase.GetAssetPath(clipSet);
            if (string.IsNullOrEmpty(setAssetPath))
            {
                Debug.LogError(
                    LogPrefix + "Clip set '" + clipSet.name + "' is not saved as an asset yet, " +
                    "so a new clip has nowhere to be written. Save the set first.",
                    clipSet);
                return null;
            }

            string containingFolderPath = ExtractContainingFolderPath(setAssetPath);
            if (string.IsNullOrEmpty(containingFolderPath))
            {
                Debug.LogError(
                    LogPrefix + "Could not resolve the folder containing clip set '" +
                    clipSet.name + "' from its asset path '" + setAssetPath + "'.",
                    clipSet);
                return null;
            }

            string uniqueAssetPath = AssetDatabase.GenerateUniqueAssetPath(
                containingFolderPath + "/" + NewClipAssetBaseName + AssetExtension);

            ClipAsset newClip = ScriptableObject.CreateInstance<ClipAsset>();
            newClip.rig = clipSet.rig;

            // Minted explicitly rather than left to the asset's own Awake/OnValidate, for the reason
            // MirrorClipUtility gives: it is idempotent, costs nothing, and makes the guarantee local
            // to the method that depends on it.
            newClip.EnsureStableIds();
            newClip.name = ExtractAssetName(uniqueAssetPath);

            AssetDatabase.CreateAsset(newClip, uniqueAssetPath);
            AssetDatabase.SaveAssets();

            // The minted id is on disk now, so the "not yet persisted" report is discharged.
            newClip.MarkStableIdPersisted();

            AppendClipToSet(clipSet, newClip);
            return newClip;
        }

        /// <summary>Appends a clip to a set's list as one undo step.</summary>
        private static void AppendClipToSet(ClipSetAsset clipSet, ClipAsset clip)
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoActionName);

            SerializedObject serializedSet = new SerializedObject(clipSet);
            SerializedProperty clipsProperty = serializedSet.FindProperty("clips");
            if (clipsProperty != null)
            {
                int newElementIndex = clipsProperty.arraySize;
                clipsProperty.InsertArrayElementAtIndex(newElementIndex);
                clipsProperty.GetArrayElementAtIndex(newElementIndex).objectReferenceValue = clip;
                serializedSet.ApplyModifiedProperties();
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>
        /// Renames a clip asset on disk, keeping the file and the object name in step.
        /// </summary>
        /// <returns>True when the rename happened; false when it was refused or unnecessary.</returns>
        /// <remarks>
        /// A clip's asset name is not cosmetic — <c>ClipSetAssetEditor</c>'s id-constant generator
        /// turns it into a C# identifier, so "NewClip 3" becomes a constant nobody wants to read.
        /// Offering the rename wherever clips are created is what stops a set filling up with them.
        /// </remarks>
        public static bool RenameClip(ClipAsset clip, string newName)
        {
            if (clip == null || string.IsNullOrWhiteSpace(newName) || clip.name == newName)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            // Returns a message rather than throwing when the name is illegal or already taken.
            string failureReason = AssetDatabase.RenameAsset(assetPath, newName);
            if (!string.IsNullOrEmpty(failureReason))
            {
                Debug.LogWarning(
                    LogPrefix + "Could not rename clip to '" + newName + "': " + failureReason, clip);
                return false;
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        private static string ExtractContainingFolderPath(string assetPath)
        {
            int lastSeparatorIndex = assetPath.LastIndexOf('/');
            return lastSeparatorIndex > 0 ? assetPath.Substring(0, lastSeparatorIndex) : string.Empty;
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
