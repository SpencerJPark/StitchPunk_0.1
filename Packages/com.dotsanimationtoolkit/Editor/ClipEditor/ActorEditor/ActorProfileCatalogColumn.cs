// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Catalog column listing project actor profiles with search, selection, and new/refresh actions.</summary>
    public sealed class ActorProfileCatalogColumn : VisualElement
    {
        private readonly List<ActorProfileAsset> catalogProfiles = new List<ActorProfileAsset>();
        private readonly List<ActorProfileAsset> filteredProfiles = new List<ActorProfileAsset>();

        private string searchText = string.Empty;
        private readonly ToolbarSearchField searchField;
        private readonly ListView profilesListView;
        private readonly Label emptyLabel;

        public event Action NewRequested;
        public event Action RefreshRequested;
        public event Action<ActorProfileAsset> ProfileSelected;
        public event Action<ActorProfileAsset, string> ProfileRenameRequested;
        public event Action<ActorProfileAsset> ProfileDeleteRequested;

        public ActorProfileAsset SelectedProfile { get; private set; }

        public ActorProfileCatalogColumn()
        {
            name = "profile-catalog-column";
            style.flexGrow = 1f;
            style.minWidth = 200f;
            style.paddingTop = 8f;
            style.paddingLeft = 10f;
            style.paddingRight = 10f;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");

            Label title = new Label("Profiles");
            title.AddToClassList("toolkit-pane-title");
            header.Add(title);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("toolkit-pane-actions");

            Button newButton = ToolkitIcons.MakeIconTextButton(RaiseNewRequested, "Toolbar Plus", null, "New");
            newButton.name = "profiles-new-button";
            actions.Add(newButton);

            Button refreshButton = ToolkitIcons.MakeIconTextButton(
                RaiseRefreshRequested, "Refresh", "Rescan the project for actor profiles", "Refresh");
            refreshButton.name = "profiles-refresh-button";
            actions.Add(refreshButton);

            header.Add(actions);
            Add(header);

            searchField = new ToolbarSearchField();
            searchField.name = "profiles-search";
            // alignSelf: Stretch alone was not enough -- the field's own internal content
            // (text input + icon + cancel button) imposes a min-content width Yoga still honours
            // over stretch, so it kept overflowing a narrow column regardless of min-width: 0.
            // An explicit percentage width is clamped to the parent's box unconditionally.
            searchField.style.width = new Length(100f, LengthUnit.Percent);
            searchField.style.minWidth = 0f;
            searchField.style.marginTop = 4f;
            // ToolbarSearchField's own default USS ships a 4px-left/2px-right margin (verified
            // live) -- on top of an already-100%-wide box that pushes its right edge past the
            // rows below, which is the "overshoot" this was reported as. Zero it so the field is
            // flush with the list.
            searchField.style.marginLeft = 0f;
            searchField.style.marginRight = 0f;
            searchField.RegisterValueChangedCallback(OnSearchTextChanged);
            Add(searchField);

            profilesListView = new ListView();
            profilesListView.name = "profiles-list";
            // DynamicHeight virtualization renders zero rows in this Unity version (verified live:
            // itemsSource.Count == 2 but the ListView's own childCount == 0) -- stick with
            // FixedHeight. ListView positions each slot at a fixed index * fixedItemHeight
            // regardless of the row's actual content height, so any slack left over here adds
            // straight onto the visual gap on top of the row's own margin -- sized tight to the
            // row's measured content (56px) + its 4px top/bottom margin, not generously, so the
            // margin is the only thing producing the gap.
            profilesListView.fixedItemHeight = 64f;
            profilesListView.selectionType = SelectionType.Single;
            profilesListView.style.flexGrow = 1f;
            profilesListView.style.marginTop = 4f;
            profilesListView.makeItem = MakeProfileRow;
            profilesListView.bindItem = BindProfileRow;
            profilesListView.itemsSource = filteredProfiles;
            profilesListView.selectionChanged += OnProfilesListSelectionChanged;
            Add(profilesListView);

            emptyLabel = new Label("No actor profiles in this project yet. Press New.");
            emptyLabel.AddToClassList("clip-editor__hint");
            Add(emptyLabel);

            RefreshEmptyState();
        }

        public void SetProfiles(IReadOnlyList<ActorProfileAsset> profiles)
        {
            catalogProfiles.Clear();
            if (profiles != null)
            {
                for (int profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
                {
                    catalogProfiles.Add(profiles[profileIndex]);
                }
            }

            ApplyFilter();
        }

        public void SetSelectedProfile(ActorProfileAsset profile)
        {
            SelectedProfile = profile;
            profilesListView.SetSelectionWithoutNotify(
                profile != null && filteredProfiles.Contains(profile)
                    ? new List<int> { filteredProfiles.IndexOf(profile) }
                    : new List<int>());
            profilesListView.Rebuild();
        }

        public void ClearSelection()
        {
            SelectedProfile = null;
            profilesListView.SetSelectionWithoutNotify(new List<int>());
            profilesListView.Rebuild();
        }

        private void RaiseNewRequested()
        {
            NewRequested?.Invoke();
        }

        private void RaiseRefreshRequested()
        {
            RefreshRequested?.Invoke();
        }

        private VisualElement MakeProfileRow()
        {
            // ListView (FixedHeight virtualization) tags whatever makeItem returns with its own
            // internal item classes and forcibly zeroes ITS margin to keep the fixed-slot math
            // exact (verified live: an 8px inline margin set directly on that root read back as 0).
            // A margin on this outer slot is a no-op, so the boxed row that actually wants the gap
            // has to live one level deeper, as a plain child Unity's pooling never touches.
            VisualElement itemSlot = new VisualElement();
            // Unity also paints its own hover/selected background straight onto this slot (verified
            // live: unity-collection-view__item--selected resolves a solid grey fill across the
            // WHOLE slot, gap margin included) -- an inline override beats that USS state styling
            // unconditionally, so the slot itself never shades and only the boxed row below reacts.
            itemSlot.style.backgroundColor = new StyleColor(Color.clear);

            VisualElement row = new VisualElement();
            row.name = "profile-row-box";
            row.AddToClassList("toolkit-box");
            // Enough to read as separated instead of touching, without the gap dominating a
            // 56px-tall row -- fixedItemHeight is sized to match (content height + this margin).
            row.style.marginTop = 4f;
            row.style.marginBottom = 4f;
            // No horizontal margin: the row is left flush with the ListView's own bounds, which
            // stretch to the same catalog-column width the search field's 100% width fills --
            // an inset here would leave the row short of the search field's right edge.
            row.style.marginLeft = 0f;
            row.style.marginRight = 0f;

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("toolkit-box__header");

            Label titleLabel = new Label();
            titleLabel.name = "profile-row-title";
            titleLabel.AddToClassList("toolkit-box__title");
            headerRow.Add(titleLabel);

            row.Add(headerRow);

            Label infoLabel = new Label();
            infoLabel.name = "profile-row-info";
            infoLabel.AddToClassList("toolkit-box__label");
            infoLabel.AddToClassList("clip-editor__hint");
            row.Add(infoLabel);

            // Closes over the row element itself (stable identity, never recreated) rather than
            // any per-bind data -- the callback reads row.userData live when the menu opens, so a
            // recycled row always acts on whatever it is currently showing.
            row.AddManipulator(new ContextualMenuManipulator(
                populateEvent => PopulateProfileRowContextMenu(populateEvent, row)));

            itemSlot.Add(row);
            return itemSlot;
        }

        private void PopulateProfileRowContextMenu(ContextualMenuPopulateEvent populateEvent, VisualElement row)
        {
            ActorProfileAsset targetProfile = row.userData as ActorProfileAsset;
            if (targetProfile == null)
            {
                return;
            }

            Label titleLabel = row.Q<Label>("profile-row-title");
            populateEvent.menu.AppendAction(
                "Rename",
                renameAction => InlineRenameEditing.Begin(
                    titleLabel,
                    targetProfile.name,
                    committedName => ProfileRenameRequested?.Invoke(targetProfile, committedName)),
                DropdownMenuAction.AlwaysEnabled);
            populateEvent.menu.AppendAction(
                "Delete",
                deleteAction => ProfileDeleteRequested?.Invoke(targetProfile),
                DropdownMenuAction.AlwaysEnabled);
        }

        private void BindProfileRow(VisualElement element, int index)
        {
            if (index < 0 || index >= filteredProfiles.Count)
            {
                return;
            }

            ActorProfileAsset profile = filteredProfiles[index];

            // The boxed row (userData, the selected-state class) lives one level below the item
            // slot ListView hands bindItem -- see MakeProfileRow.
            VisualElement row = element.Q<VisualElement>("profile-row-box");
            row.userData = profile;

            Label titleLabel = row.Q<Label>("profile-row-title");
            titleLabel.text = profile != null ? profile.name : string.Empty;

            Label infoLabel = row.Q<Label>("profile-row-info");
            int layerCount = profile != null && profile.layers != null ? profile.layers.Count : 0;
            string rigName = profile != null && profile.rig != null ? profile.rig.name : null;
            infoLabel.text = layerCount.ToString() + " layers · " + (rigName ?? "no rig");

            string assetPath = profile != null ? AssetDatabase.GetAssetPath(profile) : string.Empty;
            row.tooltip = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');

            row.EnableInClassList("toolkit-box--selected", profile == SelectedProfile);
        }

        private void OnProfilesListSelectionChanged(IEnumerable<object> selectedItems)
        {
            foreach (object selectedItem in selectedItems)
            {
                ActorProfileAsset profile = selectedItem as ActorProfileAsset;
                SelectedProfile = profile;
                ProfileSelected?.Invoke(profile);
                return;
            }
        }

        private void OnSearchTextChanged(ChangeEvent<string> changeEvent)
        {
            searchText = changeEvent.newValue ?? string.Empty;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            filteredProfiles.Clear();
            for (int index = 0; index < catalogProfiles.Count; index++)
            {
                ActorProfileAsset profile = catalogProfiles[index];
                if (profile == null)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(searchText)
                    || profile.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filteredProfiles.Add(profile);
                }
            }

            profilesListView.Rebuild();
            RefreshEmptyState();

            if (SelectedProfile != null)
            {
                profilesListView.SetSelectionWithoutNotify(
                    filteredProfiles.Contains(SelectedProfile)
                        ? new List<int> { filteredProfiles.IndexOf(SelectedProfile) }
                        : new List<int>());
            }
        }

        private void RefreshEmptyState()
        {
            bool isEmpty = filteredProfiles.Count == 0;
            profilesListView.style.display = isEmpty ? DisplayStyle.None : DisplayStyle.Flex;
            emptyLabel.style.display = isEmpty ? DisplayStyle.Flex : DisplayStyle.None;
            if (isEmpty)
            {
                emptyLabel.text = catalogProfiles.Count == 0
                    ? "No actor profiles in this project yet. Press New."
                    : "No profiles match your search.";
            }
        }
    }
}
