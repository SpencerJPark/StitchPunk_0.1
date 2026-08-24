// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Creates a <see cref="RigAsset"/> from a scanned source prefab — the write path behind the
    /// New Rig flow (Phase D11, <see cref="NewRigPanel"/>).
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="ClipAssetUtility.CreateClipSet"/>'s shape for the same reason that class
    /// gives for its own existence: one path for "mint the asset, mint its ids, save it" means a
    /// rig built through the wizard is indistinguishable from one built any other way once it is on
    /// disk, rather than the wizard quietly growing its own rules for what a valid rig looks like.
    /// </remarks>
    public static class RigAssetUtility
    {
        /// <summary>
        /// Creates a rig at <paramref name="assetPath"/>, points it at <paramref name="sourcePrefab"/>,
        /// and gives it one target per entry in <paramref name="targets"/>.
        /// </summary>
        /// <returns>The new rig, or null when the path is unusable.</returns>
        /// <remarks>
        /// <see cref="RigAsset.EnsureStableIds"/> is called after <paramref name="targets"/> is
        /// assigned, never before — its own doc comment explains why: called on an empty list it
        /// mints nothing, and a rig saved with every target id still 0 fails validation rules V02
        /// and V05 the moment a clip references it. Both shipped samples hit exactly that before
        /// the ordering was fixed there; the same trap applies to any other caller that populates
        /// <see cref="RigAsset.targets"/> after construction, this one included.
        /// </remarks>
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

            // A rig needs at least one layer to pass validation rule V13, and RigAsset.layers
            // starts empty — CreateAssetMenu creation leaves the same gap, and both shipped sample
            // builders (QuickStartActorBuilder, CompositeActorBuilder) fill it exactly this way:
            // one layer, active by default, so a fresh rig plays without the author having to know
            // layers exist yet.
            newRig.layers.Add(new LayerDefinition { displayName = "Base", defaultActive = true });

            // After populating targets, per EnsureStableIds' own remarks — see this method's.
            newRig.EnsureStableIds();
            newRig.name = ExtractAssetName(assetPath);

            AssetDatabase.CreateAsset(newRig, assetPath);
            AssetDatabase.SaveAssets();

            // The minted ids are on disk now, so the "not yet persisted" report is discharged —
            // the same close-out ClipAssetUtility.CreateClipSet performs for a freshly minted set.
            newRig.MarkStableIdPersisted();

            return newRig;
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
