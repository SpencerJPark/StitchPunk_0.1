// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Rigs tab's catalog: the shared column configured for RigAsset, with the rig-named events RigsPanel wires.</summary>
    public sealed class RigCatalogColumn : ToolkitCatalogColumn<RigAsset>
    {
        public event Action<RigAsset> RigSelected;
        public event Action<RigAsset, string> RigRenameRequested;
        public event Action<RigAsset> RigDeleteRequested;

        public RigAsset SelectedRig => SelectedAsset;

        public RigCatalogColumn() : base(BuildOptions())
        {
            AssetSelected += RaiseRigSelected;
            RenameRequested += RaiseRigRenameRequested;
            DeleteRequested += RaiseRigDeleteRequested;
        }

        public void SetRigs(IReadOnlyList<RigAsset> rigs)
        {
            SetItems(rigs);
        }

        public void SetSelectedRig(RigAsset rig)
        {
            Select(rig);
        }

        private static CatalogColumnOptions<RigAsset> BuildOptions()
        {
            return new CatalogColumnOptions<RigAsset>
            {
                elementName = "rig-catalog-column",
                namePrefix = "rigs",
                title = "Rigs",
                newButtonIconName = "Toolbar Plus",
                newButtonTooltip = null,
                refreshButtonIconName = "Refresh",
                refreshButtonTooltip = "Rescan the project for rigs",
                emptyProjectMessage = "No rigs in this project yet. Press New.",
                emptySearchMessage = "No rigs match your search.",
                scan = null,
                secondLine = DescribeRig,
                tooltip = DescribeRigSource,
                allowRename = true,
                allowDelete = true,
            };
        }

        private static string DescribeRig(RigAsset rig)
        {
            int targetCount = rig != null && rig.targets != null ? rig.targets.Count : 0;
            string assetPath = rig != null ? AssetDatabase.GetAssetPath(rig) : string.Empty;
            string folderPath = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            return targetCount.ToString() + " targets"
                + (string.IsNullOrEmpty(folderPath) ? string.Empty : " · " + folderPath);
        }

        private static string DescribeRigSource(RigAsset rig)
        {
            return rig != null && rig.sourcePrefab != null
                ? rig.sourcePrefab.name
                : "(no source prefab)";
        }

        private void RaiseRigSelected(RigAsset rig)
        {
            RigSelected?.Invoke(rig);
        }

        private void RaiseRigRenameRequested(RigAsset rig, string newName)
        {
            RigRenameRequested?.Invoke(rig, newName);
        }

        private void RaiseRigDeleteRequested(RigAsset rig)
        {
            RigDeleteRequested?.Invoke(rig);
        }
    }
}
