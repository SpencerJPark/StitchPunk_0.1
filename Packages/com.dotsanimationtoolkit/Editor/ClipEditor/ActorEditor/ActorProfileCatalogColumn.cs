// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Actor Editor's profile catalog: the shared column configured for ActorProfileAsset, with the profile-named events its host wires.</summary>
    public sealed class ActorProfileCatalogColumn : ToolkitCatalogColumn<ActorProfileAsset>
    {
        public event Action<ActorProfileAsset> ProfileSelected;
        public event Action<ActorProfileAsset, string> ProfileRenameRequested;
        public event Action<ActorProfileAsset> ProfileDeleteRequested;

        public ActorProfileAsset SelectedProfile => SelectedAsset;

        public ActorProfileCatalogColumn() : base(BuildOptions())
        {
            AssetSelected += RaiseProfileSelected;
            RenameRequested += RaiseProfileRenameRequested;
            DeleteRequested += RaiseProfileDeleteRequested;
        }

        public void SetProfiles(IReadOnlyList<ActorProfileAsset> profiles)
        {
            SetItems(profiles);
        }

        public void SetSelectedProfile(ActorProfileAsset profile)
        {
            Select(profile);
        }

        private static CatalogColumnOptions<ActorProfileAsset> BuildOptions()
        {
            return new CatalogColumnOptions<ActorProfileAsset>
            {
                elementName = "profile-catalog-column",
                namePrefix = "profiles",
                title = "Profiles",
                newButtonIconName = "Toolbar Plus",
                newButtonTooltip = null,
                refreshButtonIconName = "Refresh",
                refreshButtonTooltip = "Rescan the project for actor profiles",
                emptyProjectMessage = "No actor profiles in this project yet. Press New.",
                emptySearchMessage = "No profiles match your search.",
                scan = null,
                secondLine = DescribeProfile,
                tooltip = DescribeProfileFolder,
                allowRename = true,
                allowDelete = true,
            };
        }

        private static string DescribeProfile(ActorProfileAsset profile)
        {
            int layerCount = profile != null && profile.layers != null ? profile.layers.Count : 0;
            string rigName = profile != null && profile.rig != null ? profile.rig.name : null;
            return layerCount.ToString() + " layers · " + (rigName ?? "no rig");
        }

        private static string DescribeProfileFolder(ActorProfileAsset profile)
        {
            string assetPath = profile != null ? AssetDatabase.GetAssetPath(profile) : string.Empty;
            return string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : Path.GetDirectoryName(assetPath).Replace('\\', '/');
        }

        private void RaiseProfileSelected(ActorProfileAsset profile)
        {
            ProfileSelected?.Invoke(profile);
        }

        private void RaiseProfileRenameRequested(ActorProfileAsset profile, string newName)
        {
            ProfileRenameRequested?.Invoke(profile, newName);
        }

        private void RaiseProfileDeleteRequested(ActorProfileAsset profile)
        {
            ProfileDeleteRequested?.Invoke(profile);
        }
    }
}
