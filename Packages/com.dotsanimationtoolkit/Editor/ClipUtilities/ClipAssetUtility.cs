// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Creates, renames, removes and deletes the assets a <see cref="ClipSetAsset"/> is made of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One lifecycle path, several callers.</strong> The clip set's inspector and the clip
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
    public static class ClipAssetUtility
    {
        private const string LogPrefix = "[DOTS Animation Toolkit] ";
        private const string NewClipAssetBaseName = "NewClip";
        private const string AssetExtension = ".asset";
        private const string UndoActionName = "Create Clip In Set";
        private const string RemoveUndoActionName = "Remove Clip From Set";

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
        /// Creates an empty <see cref="ClipSetAsset"/> at <paramref name="assetPath"/>.
        /// </summary>
        /// <returns>The new set, or null when the path is unusable.</returns>
        /// <remarks>
        /// <para>
        /// The path is chosen by the caller rather than derived, because a clip set has no anchor to
        /// sit beside the way a clip sits beside its set — it is the root of the graph, so there is
        /// nothing to infer a home from and guessing one would scatter sets across a project.
        /// </para>
        /// <para>
        /// The rig is left null deliberately. There is no rig in scope at creation, and inheriting
        /// one from whatever the window happened to have open would silently bind a new set to a rig
        /// nobody chose. Validation reports the empty rig immediately, which is the honest prompt.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Un-registers a clip from a set, leaving the asset on disk. One undo step.
        /// </summary>
        /// <remarks>
        /// <see cref="SerializedProperty.DeleteArrayElementAtIndex"/> on an array of
        /// <see cref="Object"/> references only <em>nulls</em> a non-null element on its first call;
        /// a second call at the same index removes the now-empty slot. Skipping that second call
        /// leaves a null entry behind instead of shortening the list, which is silently wrong — the
        /// set would report one more clip than it shows. <c>ClipSetAssetEditor.RemoveClipAt</c>
        /// documented this quirk first; centralising it here is what stops the next caller
        /// rediscovering it the hard way.
        /// </remarks>
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

        /// <summary>
        /// Un-registers a clip and sends its asset to the OS trash.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Trash, not <see cref="AssetDatabase.DeleteAsset"/>.</strong> Undo does not bring a
        /// deleted file back, so the only recovery for a mis-click is the one the operating system
        /// provides — and it only provides it if the file went to the trash. The cost is nothing; the
        /// difference on the day it matters is the whole clip.
        /// </para>
        /// <para>
        /// The set is updated <em>before</em> the file goes, so the set never holds a reference to an
        /// asset that no longer exists. Doing it the other way round leaves a missing-reference entry
        /// in the list if the trash call fails.
        /// </para>
        /// <para>
        /// <strong>The list edit is deliberately not undoable on this path</strong>, unlike
        /// <see cref="RemoveClipFromSet"/>. Undo cannot bring the file back, so an undoable removal
        /// would restore an entry pointing at an asset that is now in the trash — a missing
        /// reference sitting in the set, which no validation rule reports. A delete that cannot be
        /// half-undone into a broken state is worth more than one that can be half-undone at all.
        /// </para>
        /// </remarks>
        public static bool DeleteClipFromSet(ClipSetAsset clipSet, int clipIndex, ClipAsset clip)
        {
            if (clip == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(clip);
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
