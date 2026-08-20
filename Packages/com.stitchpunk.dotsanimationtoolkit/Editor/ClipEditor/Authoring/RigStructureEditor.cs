// Copyright (c) 2026 Stitch Punk. All rights reserved.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Writes structural edits to a prefab asset through Unity's own prefab APIs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Nothing here touches serialized prefab data directly.</strong> Every write goes
    /// through one of the two supported routes, and which one is used depends on whether the user
    /// already has the prefab open:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <strong>A prefab stage is open for this asset</strong> — the edit is applied to
    /// <c>prefabContentsRoot</c> and the stage's scene is marked dirty. This is the better route
    /// whenever it is available: the edit lands on Unity's undo stack, the user sees it happen in
    /// prefab mode, and it is saved by the stage's own save. Editing the asset behind the back of an
    /// open stage would be worse than useless — the stage would overwrite it on save.
    /// </description></item>
    /// <item><description>
    /// <strong>No stage is open</strong> — <c>LoadPrefabContents</c> gives an isolated copy of the
    /// asset in a temporary scene, the edit is applied there, and <c>SaveAsPrefabAsset</c> writes it
    /// back. This is Unity's supported way to edit a prefab asset without opening it.
    /// </description></item>
    /// </list>
    /// <para>
    /// <strong>The second route is an immediate asset write and is not undoable.</strong> The
    /// temporary scene is discarded straight after saving, so there is no object left for the undo
    /// system to restore. That is a real limitation, not an oversight: callers should say so at the
    /// point of use, and it is the reason the stage route is preferred whenever a stage exists.
    /// </para>
    /// </remarks>
    public static class RigStructureEditor
    {
        /// <summary>
        /// Sets a transform's local pose on the prefab asset.
        /// </summary>
        /// <param name="prefab">The prefab asset, or an instance of it.</param>
        /// <param name="hierarchyPath">Path below the prefab root. Empty addresses the root.</param>
        /// <param name="localPosition">New local position.</param>
        /// <param name="localEulerAngles">New local rotation, in degrees.</param>
        /// <param name="localScale">New local scale.</param>
        /// <param name="error">Why it failed, or empty on success.</param>
        public static bool TrySetLocalPose(
            GameObject prefab,
            string hierarchyPath,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale,
            out string error)
        {
            error = string.Empty;
            string assetPath = PrefabAuthoringBridge.ResolveAssetPath(prefab);
            if (string.IsNullOrEmpty(assetPath))
            {
                error = "No prefab asset is assigned.";
                return false;
            }

            PrefabStage openStage = FindOpenStage(assetPath);
            if (openStage != null)
            {
                Transform staged = PrefabAuthoringBridge.ResolveByPath(
                    openStage.prefabContentsRoot.transform, hierarchyPath);
                if (staged == null)
                {
                    error = "\"" + hierarchyPath + "\" is not in the open prefab stage.";
                    return false;
                }

                Undo.RecordObject(staged, "Edit Rig Base Pose");
                ApplyPose(staged, localPosition, localEulerAngles, localScale);
                EditorSceneManager.MarkSceneDirty(openStage.scene);
                return true;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);
            if (contents == null)
            {
                error = "Could not open " + assetPath + " for editing.";
                return false;
            }

            try
            {
                Transform target =
                    PrefabAuthoringBridge.ResolveByPath(contents.transform, hierarchyPath);
                if (target == null)
                {
                    error = "\"" + hierarchyPath + "\" is not in the prefab.";
                    return false;
                }

                ApplyPose(target, localPosition, localEulerAngles, localScale);
                PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
                return true;
            }
            finally
            {
                // In a finally because the temporary scene leaks otherwise, and it leaks whether the
                // save threw or an early return skipped it.
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Moves one object under another inside the prefab asset.
        /// </summary>
        /// <param name="prefab">The prefab asset, or an instance of it.</param>
        /// <param name="childPath">Path of the object to move. Must not be empty — the root cannot move.</param>
        /// <param name="newParentPath">Path of the new parent. Empty means the prefab root.</param>
        /// <param name="error">Why it failed, or empty on success.</param>
        /// <remarks>
        /// <strong>World pose is preserved.</strong> Reparenting with <c>worldPositionStays: true</c>
        /// means the part does not jump when its parent changes, which is what someone restructuring
        /// a rig means by "move this under that" — they are changing who drives it, not where it is.
        /// </remarks>
        public static bool TryReparent(
            GameObject prefab, string childPath, string newParentPath, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(childPath))
            {
                error = "The prefab root cannot be reparented.";
                return false;
            }

            string assetPath = PrefabAuthoringBridge.ResolveAssetPath(prefab);
            if (string.IsNullOrEmpty(assetPath))
            {
                error = "No prefab asset is assigned.";
                return false;
            }

            PrefabStage openStage = FindOpenStage(assetPath);
            if (openStage != null)
            {
                Transform stagedChild = PrefabAuthoringBridge.ResolveByPath(
                    openStage.prefabContentsRoot.transform, childPath);
                Transform stagedParent = PrefabAuthoringBridge.ResolveByPath(
                    openStage.prefabContentsRoot.transform, newParentPath);
                if (!ValidateReparent(stagedChild, stagedParent, out error))
                {
                    return false;
                }

                Undo.SetTransformParent(stagedChild, stagedParent, "Reparent Rig Part");
                EditorSceneManager.MarkSceneDirty(openStage.scene);
                return true;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);
            if (contents == null)
            {
                error = "Could not open " + assetPath + " for editing.";
                return false;
            }

            try
            {
                Transform child = PrefabAuthoringBridge.ResolveByPath(contents.transform, childPath);
                Transform parent =
                    PrefabAuthoringBridge.ResolveByPath(contents.transform, newParentPath);
                if (!ValidateReparent(child, parent, out error))
                {
                    return false;
                }

                child.SetParent(parent, true);
                PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>Whether an edit will land on Unity's undo stack rather than writing the asset.</summary>
        /// <remarks>
        /// The window uses this to say which of the two it is about to do, because "this is undoable"
        /// and "this writes the asset now" are things a user should be told before, not after.
        /// </remarks>
        public static bool HasOpenStage(GameObject prefab)
        {
            return FindOpenStage(PrefabAuthoringBridge.ResolveAssetPath(prefab)) != null;
        }

        private static PrefabStage FindOpenStage(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.prefabContentsRoot == null)
            {
                return null;
            }
            return stage.assetPath == assetPath ? stage : null;
        }

        private static void ApplyPose(
            Transform target, Vector3 localPosition, Vector3 localEulerAngles, Vector3 localScale)
        {
            target.localPosition = localPosition;
            target.localRotation = Quaternion.Euler(localEulerAngles);
            target.localScale = localScale;
        }

        /// <summary>
        /// Rejects the reparents that would corrupt the hierarchy rather than change it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Parenting something under itself or under one of its own descendants detaches that whole
        /// subtree from the prefab into a cycle. Unity does guard this, but it guards it with an
        /// error after the fact; refusing here means the asset is never written at all.
        /// </para>
        /// <para>
        /// Public so a drag can ask before it drops. The preview hierarchy is a copy of the
        /// prefab's, so the same question answered against the copy predicts the answer against the
        /// asset — which turns an illegal drop into a rejected cursor rather than a notification
        /// after the fact.
        /// </para>
        /// </remarks>
        public static bool ValidateReparent(Transform child, Transform parent, out string error)
        {
            error = string.Empty;
            if (child == null)
            {
                error = "The object being moved is no longer in the prefab.";
                return false;
            }
            if (parent == null)
            {
                error = "The new parent is no longer in the prefab.";
                return false;
            }
            if (child == parent)
            {
                error = "An object cannot be parented under itself.";
                return false;
            }
            if (child.parent == parent)
            {
                error = "\"" + child.name + "\" is already a child of \"" + parent.name + "\".";
                return false;
            }
            if (IsDescendantOf(parent, child))
            {
                error = "\"" + parent.name + "\" is inside \"" + child.name
                    + "\", so moving it there would detach the branch.";
                return false;
            }
            return true;
        }

        private static bool IsDescendantOf(Transform candidate, Transform ancestor)
        {
            Transform walker = candidate;
            while (walker != null)
            {
                if (walker == ancestor)
                {
                    return true;
                }
                walker = walker.parent;
            }
            return false;
        }
    }
}
