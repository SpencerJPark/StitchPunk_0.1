// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed class ImageCatalogEntry
    {
        public string Guid;
        public string AssetPath;
        public string Name;
        public string Folder;
        public Texture2D LoadedTexture;
        public bool IsOnCanvas;

        public Texture2D Load()
        {
            if (LoadedTexture == null)
            {
                LoadedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
            }
            return LoadedTexture;
        }
    }

    /// <summary>Catalog column listing project textures with search, multi-select, and drag-out.</summary>
    public sealed class ImageCatalogColumn : VisualElement
    {
        private readonly List<ImageCatalogEntry> catalogImages = new List<ImageCatalogEntry>();
        private readonly List<ImageCatalogEntry> filteredImages = new List<ImageCatalogEntry>();
        private readonly HashSet<string> onCanvasGuids = new HashSet<string>();

        private string searchText = string.Empty;
        private bool hideImagesAlreadyOnCanvas;
        private readonly ToolbarSearchField searchField;
        private readonly ListView imagesListView;
        private readonly Label emptyLabel;
        private readonly VisualElement headerActions;

        public event Action<IReadOnlyList<Texture2D>> ImagesActivated;

        public VisualElement HeaderActions => headerActions;

        public ImageCatalogColumn()
        {
            name = "image-catalog-column";
            style.flexGrow = 1f;
            style.minWidth = 200f;
            style.paddingTop = 8f;
            style.paddingLeft = 10f;
            style.paddingRight = 10f;

            headerActions = new VisualElement();
            headerActions.AddToClassList("toolkit-pane-actions");

            ToolbarToggle hideOnCanvasToggle = new ToolbarToggle();
            hideOnCanvasToggle.name = "images-hide-on-canvas-toggle";
            hideOnCanvasToggle.tooltip = "Hide images already on the canvas";
            Image hideOnCanvasIcon = new Image { pickingMode = PickingMode.Ignore };
            hideOnCanvasToggle.Add(hideOnCanvasIcon);
            ToolkitIcons.SetToggleIcon(hideOnCanvasToggle, hideOnCanvasIcon, "d_scenevis_hidden_hover", "On canvas");
            hideOnCanvasToggle.RegisterValueChangedCallback(OnHideOnCanvasToggleChanged);
            headerActions.Add(hideOnCanvasToggle);

            Button refreshButton = ToolkitIcons.MakeIconTextButton(
                RescanProject, "d_Refresh", "Rescan the project for images", "Refresh");
            refreshButton.name = "images-refresh-button";
            headerActions.Add(refreshButton);

            searchField = new ToolbarSearchField();
            searchField.name = "images-search";
            // Same overshoot fix as RigCatalogColumn: an explicit percentage width is clamped to
            // the parent's box, alignSelf: Stretch alone is not.
            searchField.style.width = new Length(100f, LengthUnit.Percent);
            searchField.style.minWidth = 0f;
            searchField.style.marginTop = 4f;
            searchField.style.marginLeft = 0f;
            searchField.style.marginRight = 0f;
            searchField.RegisterValueChangedCallback(OnSearchTextChanged);
            Add(searchField);

            imagesListView = new ListView();
            imagesListView.name = "images-list";
            imagesListView.fixedItemHeight = 64f;
            imagesListView.selectionType = SelectionType.Multiple;
            imagesListView.style.flexGrow = 1f;
            imagesListView.style.marginTop = 4f;
            imagesListView.makeItem = MakeImageRow;
            imagesListView.bindItem = BindImageRow;
            imagesListView.itemsSource = filteredImages;
            imagesListView.RegisterCallback<KeyDownEvent>(OnImagesListKeyDown);
            Add(imagesListView);

            emptyLabel = new Label("No textures under Assets/ yet.");
            emptyLabel.AddToClassList("clip-editor__hint");
            Add(emptyLabel);

            RefreshEmptyState();
        }

        public void RescanProject()
        {
            catalogImages.Clear();

            string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D");
            for (int index = 0; index < textureGuids.Length; index++)
            {
                string guid = textureGuids[index];
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    continue;
                }

                ImageCatalogEntry entry = new ImageCatalogEntry
                {
                    Guid = guid,
                    AssetPath = assetPath,
                    Name = System.IO.Path.GetFileNameWithoutExtension(assetPath),
                    Folder = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'),
                    IsOnCanvas = onCanvasGuids.Contains(guid),
                };
                catalogImages.Add(entry);
            }

            catalogImages.Sort((leftEntry, rightEntry) =>
                string.Compare(leftEntry.Name, rightEntry.Name, StringComparison.OrdinalIgnoreCase));

            ApplyFilter();
        }

        public void SetOnCanvasGuids(IReadOnlyCollection<string> guids)
        {
            onCanvasGuids.Clear();
            if (guids != null)
            {
                foreach (string guid in guids)
                {
                    onCanvasGuids.Add(guid);
                }
            }

            for (int index = 0; index < catalogImages.Count; index++)
            {
                catalogImages[index].IsOnCanvas = onCanvasGuids.Contains(catalogImages[index].Guid);
            }

            ApplyFilter();
        }

        private VisualElement MakeImageRow()
        {
            // Same slot/box split as RigCatalogColumn.MakeRigRow: ListView forcibly zeroes any
            // margin on the item slot it hands out, so the boxed row that wants the gap lives one
            // level deeper, and the slot itself is painted transparent to dodge Unity's own
            // hover/selected fill on the whole slot.
            VisualElement itemSlot = new VisualElement();
            itemSlot.style.backgroundColor = new StyleColor(Color.clear);

            VisualElement row = new VisualElement();
            row.name = "image-row-box";
            row.AddToClassList("toolkit-box");
            row.style.marginTop = 4f;
            row.style.marginBottom = 4f;
            row.style.marginLeft = 0f;
            row.style.marginRight = 0f;
            row.style.flexDirection = FlexDirection.Row;

            Image thumbnail = new Image();
            thumbnail.name = "image-row-thumbnail";
            thumbnail.scaleMode = ScaleMode.ScaleToFit;
            thumbnail.pickingMode = PickingMode.Ignore;
            thumbnail.style.width = 48f;
            thumbnail.style.height = 48f;
            row.Add(thumbnail);

            VisualElement textColumn = new VisualElement();
            textColumn.style.flexGrow = 1f;
            textColumn.style.marginLeft = 8f;

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("toolkit-box__header");
            headerRow.style.flexDirection = FlexDirection.Row;

            Label titleLabel = new Label();
            titleLabel.name = "image-row-title";
            titleLabel.AddToClassList("toolkit-box__title");
            titleLabel.style.flexGrow = 1f;
            headerRow.Add(titleLabel);

            Label onCanvasMark = new Label("✓");
            onCanvasMark.name = "image-row-on-canvas-mark";
            onCanvasMark.style.display = DisplayStyle.None;
            headerRow.Add(onCanvasMark);

            textColumn.Add(headerRow);

            Label infoLabel = new Label();
            infoLabel.name = "image-row-info";
            infoLabel.AddToClassList("toolkit-box__label");
            infoLabel.AddToClassList("clip-editor__hint");
            textColumn.Add(infoLabel);

            row.Add(textColumn);

            // Closes over the row element itself (stable identity), never per-bind data -- the
            // handlers read row.userData live so a recycled row always acts on what it shows now.
            row.RegisterCallback<PointerMoveEvent>(pointerEvent => OnImageRowPointerMove(pointerEvent, row));
            row.RegisterCallback<ClickEvent>(clickEvent => OnImageRowClicked(clickEvent, row));

            itemSlot.Add(row);
            return itemSlot;
        }

        private void BindImageRow(VisualElement element, int index)
        {
            if (index < 0 || index >= filteredImages.Count)
            {
                return;
            }

            ImageCatalogEntry entry = filteredImages[index];

            VisualElement row = element.Q<VisualElement>("image-row-box");
            row.userData = entry;

            Texture2D texture = entry != null ? entry.Load() : null;

            Image thumbnail = row.Q<Image>("image-row-thumbnail");
            thumbnail.image = texture;

            Label titleLabel = row.Q<Label>("image-row-title");
            titleLabel.text = entry != null ? entry.Name : string.Empty;

            Label onCanvasMark = row.Q<Label>("image-row-on-canvas-mark");
            onCanvasMark.style.display = entry != null && entry.IsOnCanvas ? DisplayStyle.Flex : DisplayStyle.None;

            Label infoLabel = row.Q<Label>("image-row-info");
            int textureWidth = texture != null ? texture.width : 0;
            int textureHeight = texture != null ? texture.height : 0;
            string folder = entry != null ? entry.Folder : string.Empty;
            infoLabel.text = textureWidth.ToString() + " x " + textureHeight.ToString()
                + (string.IsNullOrEmpty(folder) ? string.Empty : " · " + folder);

            row.tooltip = entry != null ? entry.AssetPath : string.Empty;
        }

        private void OnSearchTextChanged(ChangeEvent<string> changeEvent)
        {
            searchText = changeEvent.newValue ?? string.Empty;
            ApplyFilter();
        }

        private void OnHideOnCanvasToggleChanged(ChangeEvent<bool> changeEvent)
        {
            hideImagesAlreadyOnCanvas = changeEvent.newValue;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            filteredImages.Clear();
            for (int index = 0; index < catalogImages.Count; index++)
            {
                ImageCatalogEntry entry = catalogImages[index];
                if (entry == null)
                {
                    continue;
                }

                bool matchesSearch = string.IsNullOrEmpty(searchText)
                    || entry.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                bool passesCanvasFilter = !hideImagesAlreadyOnCanvas || !entry.IsOnCanvas;
                if (matchesSearch && passesCanvasFilter)
                {
                    filteredImages.Add(entry);
                }
            }

            imagesListView.Rebuild();
            RefreshEmptyState();
        }

        private void RefreshEmptyState()
        {
            bool isEmpty = filteredImages.Count == 0;
            imagesListView.style.display = isEmpty ? DisplayStyle.None : DisplayStyle.Flex;
            emptyLabel.style.display = isEmpty ? DisplayStyle.Flex : DisplayStyle.None;
            if (isEmpty)
            {
                emptyLabel.text = catalogImages.Count == 0
                    ? "No textures under Assets/ yet."
                    : "No images match your search.";
            }
        }

        private void OnImageRowPointerMove(PointerMoveEvent pointerEvent, VisualElement row)
        {
            if (pointerEvent.pressedButtons != 1)
            {
                return;
            }

            ImageCatalogEntry pressedEntry = row.userData as ImageCatalogEntry;
            if (pressedEntry == null)
            {
                return;
            }

            List<ImageCatalogEntry> selectedEntries = GetSelectedEntries();
            List<ImageCatalogEntry> draggedEntries = selectedEntries.Contains(pressedEntry)
                ? selectedEntries
                : new List<ImageCatalogEntry> { pressedEntry };

            List<Texture2D> draggedTextures = new List<Texture2D>();
            for (int index = 0; index < draggedEntries.Count; index++)
            {
                Texture2D texture = draggedEntries[index].Load();
                if (texture != null)
                {
                    draggedTextures.Add(texture);
                }
            }

            if (draggedTextures.Count == 0)
            {
                return;
            }

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = draggedTextures.ToArray();
            DragAndDrop.StartDrag(draggedTextures.Count.ToString() + " image(s)");
            // Stops the ListView's own pointer handling from also starting a rectangle selection.
            pointerEvent.StopPropagation();
        }

        private void OnImageRowClicked(ClickEvent clickEvent, VisualElement row)
        {
            if (clickEvent.clickCount != 2)
            {
                return;
            }

            ImageCatalogEntry clickedEntry = row.userData as ImageCatalogEntry;
            if (clickedEntry == null)
            {
                return;
            }

            RaiseImagesActivated(clickedEntry);
        }

        private void OnImagesListKeyDown(KeyDownEvent keyDownEvent)
        {
            if (keyDownEvent.keyCode != KeyCode.Return)
            {
                return;
            }

            List<ImageCatalogEntry> selectedEntries = GetSelectedEntries();
            if (selectedEntries.Count == 0)
            {
                return;
            }

            List<Texture2D> activatedTextures = new List<Texture2D>();
            for (int index = 0; index < selectedEntries.Count; index++)
            {
                Texture2D texture = selectedEntries[index].Load();
                if (texture != null)
                {
                    activatedTextures.Add(texture);
                }
            }

            if (activatedTextures.Count > 0)
            {
                ImagesActivated?.Invoke(activatedTextures);
                keyDownEvent.StopPropagation();
            }
        }

        private void RaiseImagesActivated(ImageCatalogEntry clickedEntry)
        {
            List<ImageCatalogEntry> selectedEntries = GetSelectedEntries();
            List<ImageCatalogEntry> sourceEntries = selectedEntries.Contains(clickedEntry)
                ? selectedEntries
                : new List<ImageCatalogEntry> { clickedEntry };

            List<Texture2D> activatedTextures = new List<Texture2D>();
            for (int index = 0; index < sourceEntries.Count; index++)
            {
                Texture2D texture = sourceEntries[index].Load();
                if (texture != null)
                {
                    activatedTextures.Add(texture);
                }
            }

            if (activatedTextures.Count > 0)
            {
                ImagesActivated?.Invoke(activatedTextures);
            }
        }

        private List<ImageCatalogEntry> GetSelectedEntries()
        {
            List<ImageCatalogEntry> selectedEntries = new List<ImageCatalogEntry>();
            foreach (object selectedItem in imagesListView.selectedItems)
            {
                ImageCatalogEntry entry = selectedItem as ImageCatalogEntry;
                if (entry != null)
                {
                    selectedEntries.Add(entry);
                }
            }
            return selectedEntries;
        }
    }
}
