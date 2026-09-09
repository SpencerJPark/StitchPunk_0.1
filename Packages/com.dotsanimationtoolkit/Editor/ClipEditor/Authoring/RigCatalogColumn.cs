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
    /// <summary>Catalog column listing project rigs with search, selection, and new/refresh actions.</summary>
    public sealed class RigCatalogColumn : VisualElement
    {
        private readonly List<RigAsset> catalogRigs = new List<RigAsset>();
        private readonly List<RigAsset> filteredRigs = new List<RigAsset>();

        private string searchText = string.Empty;
        private readonly ToolbarSearchField searchField;
        private readonly ListView rigsListView;
        private readonly Label emptyLabel;

        public event Action NewRequested;
        public event Action RefreshRequested;
        public event Action<RigAsset> RigSelected;

        public RigAsset SelectedRig { get; private set; }

        public RigCatalogColumn()
        {
            name = "rig-catalog-column";
            style.flexGrow = 1f;
            style.minWidth = 200f;
            style.paddingTop = 8f;
            style.paddingLeft = 10f;
            style.paddingRight = 10f;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");

            Label title = new Label("Rigs");
            title.AddToClassList("toolkit-pane-title");
            header.Add(title);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("toolkit-pane-actions");

            Button newButton = ToolkitIcons.MakeIconTextButton(RaiseNewRequested, "Toolbar Plus", null, "New");
            newButton.name = "rigs-new-button";
            actions.Add(newButton);

            Button refreshButton = ToolkitIcons.MakeIconTextButton(
                RaiseRefreshRequested, "Refresh", "Rescan the project for rigs", "Refresh");
            refreshButton.name = "rigs-refresh-button";
            actions.Add(refreshButton);

            header.Add(actions);
            Add(header);

            searchField = new ToolbarSearchField();
            searchField.name = "rigs-search";
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

            rigsListView = new ListView();
            rigsListView.name = "rigs-list";
            // DynamicHeight virtualization renders zero rows in this Unity version (verified live:
            // itemsSource.Count == 2 but the ListView's own childCount == 0) -- stick with
            // FixedHeight. ListView positions each slot at a fixed index * fixedItemHeight
            // regardless of the row's actual content height, so any slack left over here adds
            // straight onto the visual gap on top of the row's own margin -- sized tight to the
            // row's measured content (56px) + its 4px top/bottom margin, not generously, so the
            // margin is the only thing producing the gap.
            rigsListView.fixedItemHeight = 64f;
            rigsListView.selectionType = SelectionType.Single;
            rigsListView.style.flexGrow = 1f;
            rigsListView.style.marginTop = 4f;
            rigsListView.makeItem = MakeRigRow;
            rigsListView.bindItem = BindRigRow;
            rigsListView.itemsSource = filteredRigs;
            rigsListView.selectionChanged += OnRigsListSelectionChanged;
            Add(rigsListView);

            emptyLabel = new Label("No rigs in this project yet. Press New.");
            emptyLabel.AddToClassList("clip-editor__hint");
            Add(emptyLabel);

            RefreshEmptyState();
        }

        public void SetRigs(IReadOnlyList<RigAsset> rigs)
        {
            catalogRigs.Clear();
            if (rigs != null)
            {
                for (int rigIndex = 0; rigIndex < rigs.Count; rigIndex++)
                {
                    catalogRigs.Add(rigs[rigIndex]);
                }
            }

            ApplyFilter();
        }

        public void SetSelectedRig(RigAsset rig)
        {
            SelectedRig = rig;
            rigsListView.SetSelectionWithoutNotify(
                rig != null && filteredRigs.Contains(rig)
                    ? new List<int> { filteredRigs.IndexOf(rig) }
                    : new List<int>());
            rigsListView.Rebuild();
        }

        public void ClearSelection()
        {
            SelectedRig = null;
            rigsListView.SetSelectionWithoutNotify(new List<int>());
            rigsListView.Rebuild();
        }

        private void RaiseNewRequested()
        {
            NewRequested?.Invoke();
        }

        private void RaiseRefreshRequested()
        {
            RefreshRequested?.Invoke();
        }

        private VisualElement MakeRigRow()
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
            row.name = "rig-row-box";
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
            titleLabel.name = "rig-row-title";
            titleLabel.AddToClassList("toolkit-box__title");
            headerRow.Add(titleLabel);

            row.Add(headerRow);

            Label infoLabel = new Label();
            infoLabel.name = "rig-row-info";
            infoLabel.AddToClassList("toolkit-box__label");
            infoLabel.AddToClassList("clip-editor__hint");
            row.Add(infoLabel);

            itemSlot.Add(row);
            return itemSlot;
        }

        private void BindRigRow(VisualElement element, int index)
        {
            if (index < 0 || index >= filteredRigs.Count)
            {
                return;
            }

            RigAsset rig = filteredRigs[index];

            // The boxed row (userData, the selected-state class) lives one level below the item
            // slot ListView hands bindItem -- see MakeRigRow.
            VisualElement row = element.Q<VisualElement>("rig-row-box");
            row.userData = rig;

            Label titleLabel = row.Q<Label>("rig-row-title");
            titleLabel.text = rig != null ? rig.name : string.Empty;

            Label infoLabel = row.Q<Label>("rig-row-info");
            int targetCount = rig != null && rig.targets != null ? rig.targets.Count : 0;
            string assetPath = rig != null ? AssetDatabase.GetAssetPath(rig) : string.Empty;
            string folderPath = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            infoLabel.text = targetCount.ToString() + " targets"
                + (string.IsNullOrEmpty(folderPath) ? string.Empty : " · " + folderPath);

            row.tooltip = rig != null && rig.sourcePrefab != null
                ? rig.sourcePrefab.name
                : "(no source prefab)";

            row.EnableInClassList("toolkit-box--selected", rig == SelectedRig);
        }

        private void OnRigsListSelectionChanged(IEnumerable<object> selectedItems)
        {
            foreach (object selectedItem in selectedItems)
            {
                RigAsset rig = selectedItem as RigAsset;
                SelectedRig = rig;
                RigSelected?.Invoke(rig);
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
            filteredRigs.Clear();
            for (int index = 0; index < catalogRigs.Count; index++)
            {
                RigAsset rig = catalogRigs[index];
                if (rig == null)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(searchText)
                    || rig.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filteredRigs.Add(rig);
                }
            }

            rigsListView.Rebuild();
            RefreshEmptyState();

            if (SelectedRig != null)
            {
                rigsListView.SetSelectionWithoutNotify(
                    filteredRigs.Contains(SelectedRig)
                        ? new List<int> { filteredRigs.IndexOf(SelectedRig) }
                        : new List<int>());
            }
        }

        private void RefreshEmptyState()
        {
            bool isEmpty = filteredRigs.Count == 0;
            rigsListView.style.display = isEmpty ? DisplayStyle.None : DisplayStyle.Flex;
            emptyLabel.style.display = isEmpty ? DisplayStyle.Flex : DisplayStyle.None;
            if (isEmpty)
            {
                emptyLabel.text = catalogRigs.Count == 0
                    ? "No rigs in this project yet. Press New."
                    : "No rigs match your search.";
            }
        }
    }
}
