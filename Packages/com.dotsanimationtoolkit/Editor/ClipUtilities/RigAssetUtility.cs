// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Creates a <see cref="RigAsset"/> from a scanned source prefab, and edits an existing one's
    /// targets, tags and source prefab — the write path behind the Rigs tab.
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

        /// Appends a target for sourceNodePath as one undo step, minting its stable id. Returns the
        /// new target, or null when the rig is null or already has one for that path.
        public static RigTargetDefinition AddTargetToRig(
            RigAsset rig, string sourceNodePath, string displayName)
        {
            if (rig == null || rig.targets == null)
            {
                return null;
            }

            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition existingTarget = rig.targets[targetIndex];
                if (existingTarget != null
                    && string.Equals(existingTarget.sourceNodePath, sourceNodePath, System.StringComparison.Ordinal))
                {
                    return null;
                }
            }

            Undo.RecordObject(rig, "Add Rig Target");
            RigTargetDefinition newTarget = new RigTargetDefinition
            {
                displayName = displayName,
                sourceNodePath = sourceNodePath,
            };
            rig.targets.Add(newTarget);
            // Nothing else can mint the id: stableId is internal to the Authoring assembly.
            rig.EnsureStableIds();

            EditorUtility.SetDirty(rig);
            AssetDatabase.SaveAssetIfDirty(rig);

            return newTarget;
        }

        /// Removes the target carrying targetStableId as one undo step. False when the rig is null
        /// or no target matches.
        public static bool RemoveTargetFromRig(RigAsset rig, uint targetStableId)
        {
            if (rig == null || rig.targets == null)
            {
                return false;
            }

            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition candidateTarget = rig.targets[targetIndex];
                if (candidateTarget != null && candidateTarget.Id.Value == targetStableId)
                {
                    Undo.RecordObject(rig, "Remove Rig Target");
                    rig.targets.RemoveAt(targetIndex);

                    EditorUtility.SetDirty(rig);
                    AssetDatabase.SaveAssetIfDirty(rig);

                    return true;
                }
            }

            return false;
        }

        /// Writes a target's tag as one undo step. False when the rig is null or no target matches.
        /// tagId 0 means untagged, which is legal.
        public static bool SetTargetTag(RigAsset rig, uint targetStableId, uint tagId)
        {
            if (rig == null || rig.targets == null)
            {
                return false;
            }

            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition candidateTarget = rig.targets[targetIndex];
                if (candidateTarget != null && candidateTarget.Id.Value == targetStableId)
                {
                    Undo.RecordObject(rig, "Set Rig Target Tag");
                    candidateTarget.tagId = tagId;

                    EditorUtility.SetDirty(rig);
                    AssetDatabase.SaveAssetIfDirty(rig);

                    return true;
                }
            }

            return false;
        }

        /// Repoints the rig at a different source prefab as one undo step, leaving every target as
        /// it is. False when the rig is null or the prefab is already assigned.
        public static bool SetRigSourcePrefab(RigAsset rig, GameObject sourcePrefab)
        {
            if (rig == null || rig.sourcePrefab == sourcePrefab)
            {
                return false;
            }

            Undo.RecordObject(rig, "Set Rig Source Prefab");
            rig.sourcePrefab = sourcePrefab;

            EditorUtility.SetDirty(rig);
            AssetDatabase.SaveAssetIfDirty(rig);

            return true;
        }

        /// Renames the rig's asset file. False when the rig is null, unsaved, the name is blank, or it is already that name.
        public static bool RenameRig(RigAsset rig, string newName)
        {
            if (rig == null || string.IsNullOrWhiteSpace(newName) || rig.name == newName)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(rig);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            // Returns a message rather than throwing when the name is illegal or already taken.
            string failureReason = AssetDatabase.RenameAsset(assetPath, newName);
            if (!string.IsNullOrEmpty(failureReason))
            {
                Debug.LogWarning(
                    "[DOTS Animation Toolkit] Rigs: Could not rename rig to '" + newName + "': " + failureReason, rig);
                return false;
            }

            AssetDatabase.SaveAssets();
            return true;
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
