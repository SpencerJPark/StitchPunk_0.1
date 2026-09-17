// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed class CatalogColumnOptions<TAsset> where TAsset : UnityEngine.Object
    {
        public string elementName;
        public string namePrefix;
        public string title;
        public string newButtonIconName = "Toolbar Plus";
        public string newButtonTooltip;
        public string refreshButtonIconName = "Refresh";
        public string refreshButtonTooltip;
        public string emptyProjectMessage;
        public string emptySearchMessage;
        public Func<IReadOnlyList<TAsset>> scan;
        public Func<TAsset, string> secondLine;
        public Func<TAsset, string> tooltip;
        public bool allowRename;
        public bool allowDelete;

        // Null offers Rename/Delete on every row; otherwise only on rows it returns true for.
        public Func<TAsset, bool> rowAllowsRenameAndDelete;
    }

    /// <summary>Shared catalog column: search, New/Refresh, boxed two-line rows with right-click Rename/Delete. The host supplies the asset list and owns every write; the column only raises events.</summary>
    public class ToolkitCatalogColumn<TAsset> : VisualElement where TAsset : UnityEngine.Object
    {
        private readonly CatalogColumnOptions<TAsset> options;
        private readonly List<TAsset> catalogAssets = new List<TAsset>();
        private readonly List<TAsset> filteredAssets = new List<TAsset>();

        private string searchText = string.Empty;
        private readonly ToolbarSearchField searchField;
        private readonly ListView assetsListView;
        private readonly Label emptyLabel;

        public event Action NewRequested;
        public event Action RefreshRequested;
        public event Action<TAsset> AssetSelected;
        public event Action<TAsset, string> RenameRequested;
        public event Action<TAsset> DeleteRequested;

        public TAsset SelectedAsset { get; private set; }

        public VisualElement HeaderActions { get; }

        public ToolkitCatalogColumn(CatalogColumnOptions<TAsset> options)
        {
            this.options = options;

            name = options.elementName;
            AddToClassList("toolkit-column");
            style.flexGrow = 1f;
            style.minWidth = 200f;

            HeaderActions = new VisualElement();
            HeaderActions.AddToClassList("toolkit-pane-actions");

            Button newButton = ToolkitIcons.MakeIconTextButton(
                RaiseNewRequested, options.newButtonIconName, options.newButtonTooltip, "New");
            newButton.name = options.namePrefix + "-new-button";
            HeaderActions.Add(newButton);

            Button refreshButton = ToolkitIcons.MakeIconTextButton(
                Rescan, options.refreshButtonIconName, options.refreshButtonTooltip, "Refresh");
            refreshButton.name = options.namePrefix + "-refresh-button";
            ToolkitChrome.StyleButton(refreshButton, ToolkitButtonVariant.Ghost);
            HeaderActions.Add(refreshButton);

            if (!string.IsNullOrEmpty(options.title))
            {
                VisualElement header = new VisualElement();
                header.AddToClassList("toolkit-pane-header");

                Label titleLabel = new Label(options.title);
                titleLabel.AddToClassList("toolkit-pane-title");
                header.Add(titleLabel);

                header.Add(HeaderActions);
                Add(header);
            }

            searchField = new ToolbarSearchField();
            searchField.name = options.namePrefix + "-search";
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

            assetsListView = new ListView();
            assetsListView.name = options.namePrefix + "-list";
            // DynamicHeight virtualization renders zero rows in this Unity version (verified live:
            // itemsSource.Count == 2 but the ListView's own childCount == 0) -- stick with
            // FixedHeight. ListView positions each slot at a fixed index * fixedItemHeight
            // regardless of the row's actual content height, so any slack left over here adds
            // straight onto the visual gap on top of the row's own margin -- a row is now a
            // single flat 22px line with no margin, so fixedItemHeight matches it exactly.
            assetsListView.fixedItemHeight = 22f;
            assetsListView.selectionType = SelectionType.Single;
            assetsListView.style.flexGrow = 1f;
            assetsListView.style.marginTop = 4f;
            assetsListView.makeItem = MakeRow;
            assetsListView.bindItem = BindRow;
            assetsListView.itemsSource = filteredAssets;
            assetsListView.selectionChanged += OnListSelectionChanged;
            assetsListView.AddToClassList("toolkit-list-surface");
            Add(assetsListView);

            emptyLabel = new Label();
            emptyLabel.AddToClassList("toolkit-hint");
            Add(emptyLabel);

            RefreshEmptyState();
        }

        public void SetItems(IReadOnlyList<TAsset> items)
        {
            catalogAssets.Clear();
            if (items != null)
            {
                for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
                {
                    catalogAssets.Add(items[itemIndex]);
                }
            }

            ApplyFilter();
        }

        public void Rescan()
        {
            // scan is optional: a host that owns its own asset discovery can wire RefreshRequested
            // instead and call SetItems itself.
            if (options.scan != null)
            {
                SetItems(options.scan());
            }

            RaiseRefreshRequested();
        }

        public void Select(TAsset asset)
        {
            SelectedAsset = asset;
            assetsListView.SetSelectionWithoutNotify(
                asset != null && filteredAssets.Contains(asset)
                    ? new List<int> { filteredAssets.IndexOf(asset) }
                    : new List<int>());
            assetsListView.Rebuild();
        }

        public void ClearSelection()
        {
            SelectedAsset = null;
            assetsListView.SetSelectionWithoutNotify(new List<int>());
            assetsListView.Rebuild();
        }

        public void RefreshRows()
        {
            assetsListView.RefreshItems();
        }

        private void RaiseNewRequested()
        {
            NewRequested?.Invoke();
        }

        private void RaiseRefreshRequested()
        {
            RefreshRequested?.Invoke();
        }

        private VisualElement MakeRow()
        {
            // Why a slot around a row: ToolkitChrome.MakeListRowSlot.
            VisualElement itemSlot = ToolkitChrome.MakeListRowSlot(options.namePrefix + "-row-box", out VisualElement row);

            Label titleLabel = new Label();
            titleLabel.name = options.namePrefix + "-row-title";
            titleLabel.AddToClassList("toolkit-list-row__title");
            row.Add(titleLabel);

            Label infoLabel = new Label();
            infoLabel.name = options.namePrefix + "-row-info";
            infoLabel.AddToClassList("toolkit-list-row__meta");
            row.Add(infoLabel);

            if (options.allowRename || options.allowDelete)
            {
                // Closes over the row element itself (stable identity, never recreated) rather than
                // any per-bind data -- the callback reads row.userData live when the menu opens, so a
                // recycled row always acts on whatever it is currently showing.
                row.AddManipulator(new ContextualMenuManipulator(
                    populateEvent => PopulateRowContextMenu(populateEvent, row)));
            }

            return itemSlot;
        }

        private void PopulateRowContextMenu(ContextualMenuPopulateEvent populateEvent, VisualElement row)
        {
            TAsset targetAsset = row.userData as TAsset;
            if (targetAsset == null)
            {
                return;
            }

            if (options.rowAllowsRenameAndDelete != null && !options.rowAllowsRenameAndDelete(targetAsset))
            {
                return;
            }

            if (options.allowRename)
            {
                Label titleLabel = row.Q<Label>(options.namePrefix + "-row-title");
                populateEvent.menu.AppendAction(
                    "Rename",
                    renameAction => InlineRenameEditing.Begin(
                        titleLabel,
                        targetAsset.name,
                        committedName => RenameRequested?.Invoke(targetAsset, committedName)),
                    DropdownMenuAction.AlwaysEnabled);
            }

            if (options.allowDelete)
            {
                populateEvent.menu.AppendAction(
                    "Delete",
                    deleteAction => DeleteRequested?.Invoke(targetAsset),
                    DropdownMenuAction.AlwaysEnabled);
            }
        }

        private void BindRow(VisualElement element, int index)
        {
            if (index < 0 || index >= filteredAssets.Count)
            {
                return;
            }

            TAsset asset = filteredAssets[index];

            // The boxed row (userData, the selected-state class) lives one level below the item
            // slot ListView hands bindItem -- see MakeRow.
            VisualElement row = element.Q<VisualElement>(options.namePrefix + "-row-box");
            row.userData = asset;

            Label titleLabel = row.Q<Label>(options.namePrefix + "-row-title");
            titleLabel.text = asset != null ? asset.name : string.Empty;

            Label infoLabel = row.Q<Label>(options.namePrefix + "-row-info");
            infoLabel.text = asset != null ? options.secondLine(asset) : string.Empty;

            // Both the title and meta ellipsize on a 22px line, so the tooltip carries the full text.
            string tooltipText = asset != null ? asset.name : string.Empty;
            string infoText = infoLabel.text;
            if (!string.IsNullOrEmpty(infoText))
            {
                tooltipText += "\n" + infoText;
            }

            if (asset != null && options.tooltip != null)
            {
                string extraTooltipText = options.tooltip(asset);
                if (!string.IsNullOrEmpty(extraTooltipText))
                {
                    tooltipText += "\n" + extraTooltipText;
                }
            }

            row.tooltip = tooltipText;

            row.EnableInClassList("toolkit-list-row--selected", asset == SelectedAsset);
        }

        private void OnListSelectionChanged(IEnumerable<object> selectedItems)
        {
            foreach (object selectedItem in selectedItems)
            {
                TAsset asset = selectedItem as TAsset;
                SelectedAsset = asset;
                AssetSelected?.Invoke(asset);
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
            filteredAssets.Clear();
            for (int index = 0; index < catalogAssets.Count; index++)
            {
                TAsset asset = catalogAssets[index];
                if (asset == null)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(searchText)
                    || asset.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filteredAssets.Add(asset);
                }
            }

            assetsListView.Rebuild();
            RefreshEmptyState();

            if (SelectedAsset != null)
            {
                assetsListView.SetSelectionWithoutNotify(
                    filteredAssets.Contains(SelectedAsset)
                        ? new List<int> { filteredAssets.IndexOf(SelectedAsset) }
                        : new List<int>());
            }
        }

        private void RefreshEmptyState()
        {
            bool isEmpty = filteredAssets.Count == 0;
            assetsListView.style.display = isEmpty ? DisplayStyle.None : DisplayStyle.Flex;
            emptyLabel.style.display = isEmpty ? DisplayStyle.Flex : DisplayStyle.None;
            if (isEmpty)
            {
                emptyLabel.text = catalogAssets.Count == 0
                    ? options.emptyProjectMessage
                    : options.emptySearchMessage;
            }
        }
    }
}
