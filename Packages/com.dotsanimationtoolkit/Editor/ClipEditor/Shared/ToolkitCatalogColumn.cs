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
            style.flexGrow = 1f;
            style.minWidth = 200f;
            style.paddingTop = 8f;
            style.paddingLeft = 10f;
            style.paddingRight = 10f;

            HeaderActions = new VisualElement();
            HeaderActions.AddToClassList("toolkit-pane-actions");

            Button newButton = ToolkitIcons.MakeIconTextButton(
                RaiseNewRequested, options.newButtonIconName, options.newButtonTooltip, "New");
            newButton.name = options.namePrefix + "-new-button";
            HeaderActions.Add(newButton);

            Button refreshButton = ToolkitIcons.MakeIconTextButton(
                Rescan, options.refreshButtonIconName, options.refreshButtonTooltip, "Refresh");
            refreshButton.name = options.namePrefix + "-refresh-button";
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
            // straight onto the visual gap on top of the row's own margin -- sized tight to the
            // row's measured content (56px) + its 4px top/bottom margin, not generously, so the
            // margin is the only thing producing the gap.
            assetsListView.fixedItemHeight = 64f;
            assetsListView.selectionType = SelectionType.Single;
            assetsListView.style.flexGrow = 1f;
            assetsListView.style.marginTop = 4f;
            assetsListView.makeItem = MakeRow;
            assetsListView.bindItem = BindRow;
            assetsListView.itemsSource = filteredAssets;
            assetsListView.selectionChanged += OnListSelectionChanged;
            Add(assetsListView);

            emptyLabel = new Label();
            emptyLabel.AddToClassList("clip-editor__hint");
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
            row.name = options.namePrefix + "-row-box";
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
            titleLabel.name = options.namePrefix + "-row-title";
            titleLabel.AddToClassList("toolkit-box__title");
            headerRow.Add(titleLabel);

            row.Add(headerRow);

            Label infoLabel = new Label();
            infoLabel.name = options.namePrefix + "-row-info";
            infoLabel.AddToClassList("toolkit-box__label");
            infoLabel.AddToClassList("clip-editor__hint");
            row.Add(infoLabel);

            if (options.allowRename || options.allowDelete)
            {
                // Closes over the row element itself (stable identity, never recreated) rather than
                // any per-bind data -- the callback reads row.userData live when the menu opens, so a
                // recycled row always acts on whatever it is currently showing.
                row.AddManipulator(new ContextualMenuManipulator(
                    populateEvent => PopulateRowContextMenu(populateEvent, row)));
            }

            itemSlot.Add(row);
            return itemSlot;
        }

        private void PopulateRowContextMenu(ContextualMenuPopulateEvent populateEvent, VisualElement row)
        {
            TAsset targetAsset = row.userData as TAsset;
            if (targetAsset == null)
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

            row.tooltip = asset != null && options.tooltip != null ? options.tooltip(asset) : string.Empty;

            row.EnableInClassList("toolkit-box--selected", asset == SelectedAsset);
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
