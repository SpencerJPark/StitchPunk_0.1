// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Creates, renames, removes and deletes the assets a <see cref="ClipSetAsset"/> is made of —
    /// the one lifecycle path both the clip set inspector and the Clip Editor's Clips pane use, so
    /// a clip made from either is indistinguishable on disk.
    /// </summary>
    public static class ClipAssetUtility
    {
        private const string LogPrefix = "[DOTS Animation Toolkit] ";
        private const string NewClipAssetBaseName = "NewClip";
        private const string AssetExtension = ".asset";
        private const string UndoActionName = "Create Clip In Set";
        private const string RemoveUndoActionName = "Remove Clip From Set";

        /// <summary>
        /// Creates a clip beside <paramref name="clipSet"/> on disk and appends it to the set's
        /// clip list.
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

            // Minted explicitly rather than left to the asset's own Awake/OnValidate, for the reason
            // MirrorClipUtility gives: it is idempotent, costs nothing, and makes the guarantee local
            // to the method that depends on it.
            newClip.EnsureStableIds();
            newClip.name = ExtractAssetName(uniqueAssetPath);

            // The file write itself is not undoable — Ctrl+Z cannot delete a file from disk. Only
            // the append below, through SerializedProperty, is wrapped in an undo group.
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
        /// Creates an empty <see cref="ClipSetAsset"/> at <paramref name="assetPath"/>.
        /// </summary>
        /// <returns>The new set, or null when the path is unusable.</returns>
        public static ClipSetAsset CreateClipSet(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            ClipSetAsset newClipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            newClipSet.EnsureStableIds();
            newClipSet.name = ExtractAssetName(assetPath);

            AssetDatabase.CreateAsset(newClipSet, assetPath);
            AssetDatabase.SaveAssets();
            newClipSet.MarkStableIdPersisted();

            return newClipSet;
        }

        /// <summary>Un-registers a clip from a set, leaving the asset on disk. One undo step.</summary>
        public static bool RemoveClipFromSet(ClipSetAsset clipSet, int clipIndex)
        {
            return RemoveClipEntry(clipSet, clipIndex, true);
        }

        private static bool RemoveClipEntry(ClipSetAsset clipSet, int clipIndex, bool recordUndo)
        {
            if (clipSet == null)
            {
                return false;
            }

            SerializedObject serializedSet = new SerializedObject(clipSet);
            SerializedProperty clipsProperty = serializedSet.FindProperty("clips");
            if (clipsProperty == null || clipIndex < 0 || clipIndex >= clipsProperty.arraySize)
            {
                return false;
            }

            // Opened only once the edit is known to be possible, so a refused call does not leave an
            // empty step in the user's undo history.
            int undoGroup = 0;
            if (recordUndo)
            {
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(RemoveUndoActionName);
            }

            // DeleteArrayElementAtIndex on a non-null object reference only nulls it on the first
            // call; a second call at the same index actually removes the now-empty slot.
            bool wasNonNullReference =
                clipsProperty.GetArrayElementAtIndex(clipIndex).objectReferenceValue != null;
            clipsProperty.DeleteArrayElementAtIndex(clipIndex);

            if (wasNonNullReference
                && clipIndex < clipsProperty.arraySize
                && clipsProperty.GetArrayElementAtIndex(clipIndex).objectReferenceValue == null)
            {
                clipsProperty.DeleteArrayElementAtIndex(clipIndex);
            }

            if (recordUndo)
            {
                serializedSet.ApplyModifiedProperties();
                Undo.CollapseUndoOperations(undoGroup);
            }
            else
            {
                serializedSet.ApplyModifiedPropertiesWithoutUndo();
            }
            return true;
        }

        /// <summary>Un-registers a clip and sends its asset to the OS trash.</summary>
        public static bool DeleteClipFromSet(ClipSetAsset clipSet, int clipIndex, ClipAsset clip)
        {
            if (clip == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(clip);
            // Not undoable, unlike RemoveClipFromSet: undo cannot un-trash the file, so an undone
            // removal would leave the set pointing at an asset that no longer exists.
            RemoveClipEntry(clipSet, clipIndex, false);

            if (string.IsNullOrEmpty(assetPath))
            {
                // Never saved, so there is no file to trash; un-registering was the whole job.
                return true;
            }

            if (!AssetDatabase.MoveAssetToTrash(assetPath))
            {
                Debug.LogWarning(
                    LogPrefix + "Removed the clip from the set, but could not move '" + assetPath +
                    "' to the trash. The file is still on disk.", clipSet);
                return false;
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// Renames a clip asset on disk, keeping the file and the object name in step.
        /// </summary>
        /// <returns>True when the rename happened; false when it was refused or unnecessary.</returns>
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
