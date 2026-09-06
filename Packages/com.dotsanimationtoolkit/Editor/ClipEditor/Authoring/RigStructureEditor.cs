// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Writes structural edits to a prefab asset through Unity's own prefab APIs, never touching
    /// serialized prefab data directly. Prefers an open prefab stage (undoable, on-screen) over
    /// <c>LoadPrefabContents</c>/<c>SaveAsPrefabAsset</c> (an immediate, non-undoable asset write);
    /// a pose edit tries a third, undoable <see cref="SerializedObject"/> route first.
    /// </summary>
    public static class RigStructureEditor
    {
        /// <summary>How far the read-back rotation may sit from the one asked for and still count as
        /// written — a quaternion round-tripped through serialization is not bit-identical.</summary>
        private const float RotationEpsilonDegrees = 0.01f;

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

            // Tried before the load-and-save route below because it is the only one of the two a
            // person can undo, and a rig move they cannot take back is the complaint this exists to
            // answer. Writing the asset's own Transform through the serialization layer puts the
            // change on Unity's undo stack; the load-and-save route cannot, because the object it
            // edits is a copy that is thrown away before the stack could ever refer to it.
            GameObject assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            Transform assetTarget = assetRoot != null
                ? PrefabAuthoringBridge.ResolveByPath(assetRoot.transform, hierarchyPath)
                : null;
            if (assetTarget != null
                && TryWriteUndoablePose(assetTarget, localPosition, localEulerAngles, localScale))
            {
                AssetDatabase.SaveAssetIfDirty(assetRoot);
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
        // World pose is preserved (worldPositionStays: true): the part does not jump when its
        // parent changes, since restructuring a rig changes who drives a part, not where it is.
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

        // Writes a pose onto a prefab asset's own Transform through SerializedObject, whose
        // ApplyModifiedProperties registers the undo step a direct assignment would not. The result
        // is read back rather than assumed: a refused write leaves the old value with no error.
        /// <returns>False when the write did not take, leaving the caller to fall back to the load-and-save route.</returns>
        private static bool TryWriteUndoablePose(
            Transform assetTarget,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale)
        {
            SerializedObject serializedTransform = new SerializedObject(assetTarget);
            SerializedProperty positionProperty = serializedTransform.FindProperty("m_LocalPosition");
            SerializedProperty rotationProperty = serializedTransform.FindProperty("m_LocalRotation");
            SerializedProperty scaleProperty = serializedTransform.FindProperty("m_LocalScale");
            if (positionProperty == null || rotationProperty == null || scaleProperty == null)
            {
                return false;
            }

            Quaternion localRotation = Quaternion.Euler(localEulerAngles);
            positionProperty.vector3Value = localPosition;
            rotationProperty.quaternionValue = localRotation;
            scaleProperty.vector3Value = localScale;

            // The hint is what the inspector shows and what keeps a 180° rotation from being read
            // back as -180°. Optional only because it is an internal field, not because a rig that
            // reads its angles differently after every edit would be acceptable.
            SerializedProperty eulerHintProperty =
                serializedTransform.FindProperty("m_LocalEulerAnglesHint");
            if (eulerHintProperty != null)
            {
                eulerHintProperty.vector3Value = localEulerAngles;
            }

            serializedTransform.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("Edit Rig Base Pose");

            return assetTarget.localPosition == localPosition
                && assetTarget.localScale == localScale
                && Quaternion.Angle(assetTarget.localRotation, localRotation) <= RotationEpsilonDegrees;
        }

        // Rejects the reparents that would corrupt the hierarchy rather than change it. Public so a
        // drag can ask before it drops: the preview hierarchy is a copy of the prefab's, so the same
        // question answered against the copy predicts the answer against the asset.
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
