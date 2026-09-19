// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Flipbooks tab's frame list: the layer order, a drop target for images, and drag-to-reorder.</summary>
    public sealed class FlipbookFramesColumn : VisualElement, IDisposable
    {
        // Raised after any add, remove, reorder or rename; the frames' index fields are already renumbered.
        public event Action FramesChanged;

        // The list position of the newly selected frame.
        public event Action<int> FrameSelected;

        private readonly Label titleLabel;
        private readonly ListView framesListView;
        private readonly Label emptyLabel;
        private readonly Button removeButton;
        private readonly List<FlipbookFrame> emptyFrames = new List<FlipbookFrame>();
        private readonly FlipbookLayerThumbnailCache layerThumbnailCache = new FlipbookLayerThumbnailCache();

        private FlipbookAsset flipbook;

        public FlipbookFramesColumn()
        {
            name = "flipbook-frames-column";
            AddToClassList("toolkit-column");
            AddToClassList("toolkit-column--raised");
            style.flexGrow = 1f;
            style.minWidth = 220f;

            VisualElement paneHeader = ToolkitChrome.MakePaneHeader(
                "Frames (0)", out titleLabel, out VisualElement actionsRow);
            titleLabel.name = "flipbook-frames-title";
            Add(paneHeader);

            removeButton = ToolkitIcons.MakeIconTextButton(
                RemoveSelectedFrames, ToolkitIcons.Trash, "Remove the selected frames", "Remove");
            removeButton.name = "flipbook-frames-remove-button";
            ToolkitChrome.StyleButton(removeButton, ToolkitButtonVariant.Destructive);
            actionsRow.Add(removeButton);

            framesListView = new ListView();
            framesListView.name = "flipbook-frames-list";
            framesListView.fixedItemHeight = 28f;
            framesListView.selectionType = SelectionType.Multiple;
            framesListView.reorderable = true;
            framesListView.reorderMode = ListViewReorderMode.Simple;
            framesListView.style.flexGrow = 1f;
            framesListView.style.marginTop = 4f;
            framesListView.makeItem = MakeFrameRow;
            framesListView.bindItem = BindFrameRow;
            framesListView.itemsSource = emptyFrames;
            framesListView.itemIndexChanged += OnFrameIndexChanged;
            framesListView.selectionChanged += OnFrameSelectionChanged;
            framesListView.RegisterCallback<KeyDownEvent>(OnFramesListKeyDown);
            framesListView.AddToClassList("toolkit-list-surface");
            Add(framesListView);

            emptyLabel = ToolkitChrome.MakeHint("Select or create a flipbook.");
            Add(emptyLabel);

            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerformed);

            RefreshRows();
        }

        public void Dispose()
        {
            layerThumbnailCache.Dispose();
        }

        // The flipbook is edited in place (the panel passes its working copy); null clears the list.
        public void SetFlipbook(FlipbookAsset targetFlipbook)
        {
            layerThumbnailCache.Clear();
            flipbook = targetFlipbook;
            RefreshRows();
        }

        public void AddSources(IReadOnlyList<Texture2D> sourceTextures)
        {
            if (IsImportedMode || flipbook == null || sourceTextures == null)
            {
                return;
            }

            HashSet<string> takenNames = new HashSet<string>();
            for (int frameIndex = 0; frameIndex < flipbook.frames.Count; frameIndex++)
            {
                takenNames.Add(flipbook.frames[frameIndex].name);
            }

            for (int sourceIndex = 0; sourceIndex < sourceTextures.Count; sourceIndex++)
            {
                Texture2D sourceTexture = sourceTextures[sourceIndex];
                if (sourceTexture == null)
                {
                    continue;
                }

                string frameName = FlipbookValidation.DedupeFrameName(sourceTexture.name, takenNames);
                flipbook.frames.Add(new FlipbookFrame
                {
                    name = frameName,
                    index = flipbook.frames.Count,
                    source = sourceTexture,
                });
                takenNames.Add(frameName);
            }

            RefreshRows();
            FramesChanged?.Invoke();
        }

        public void SelectFrame(int listPosition)
        {
            if (flipbook == null || listPosition < 0 || listPosition >= flipbook.frames.Count)
            {
                return;
            }

            framesListView.SetSelectionWithoutNotify(new int[] { listPosition });
            framesListView.ScrollToItem(listPosition);
        }

        // The importer owns the layer order and layer identity for an imported array: no reorder, add or remove.
        private bool IsImportedMode => flipbook != null && flipbook.IsImportedArray;

        public void RefreshRows()
        {
            int frameCount = flipbook != null ? flipbook.frames.Count : 0;
            titleLabel.text = "Frames (" + frameCount.ToString() + ")";
            framesListView.itemsSource = flipbook != null ? flipbook.frames : emptyFrames;
            framesListView.reorderable = !IsImportedMode;
            removeButton.SetEnabled(!IsImportedMode);
            framesListView.RefreshItems();

            bool isEmpty = frameCount == 0;
            framesListView.style.display = isEmpty ? DisplayStyle.None : DisplayStyle.Flex;
            emptyLabel.style.display = isEmpty ? DisplayStyle.Flex : DisplayStyle.None;
            if (isEmpty)
            {
                emptyLabel.text = flipbook == null
                    ? "Select or create a flipbook."
                    : "Drag images here from the Images column, or double-click one.";
            }
        }

        private VisualElement MakeFrameRow()
        {
            VisualElement itemSlot = ToolkitChrome.MakeListRowSlot("flipbook-frame-row", out VisualElement row);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.AddToClassList("toolkit-list-row--media");

            Image thumbnailImage = new Image();
            thumbnailImage.name = "flipbook-frame-thumbnail";
            thumbnailImage.scaleMode = ScaleMode.ScaleToFit;
            thumbnailImage.style.width = 24f;
            thumbnailImage.style.height = 24f;
            row.Add(thumbnailImage);

            Label nameLabel = new Label();
            nameLabel.name = "flipbook-frame-name";
            nameLabel.AddToClassList("toolkit-list-row__title");
            nameLabel.style.flexGrow = 1f;
            nameLabel.tooltip = "Double-click to rename";
            nameLabel.RegisterCallback<MouseDownEvent>(mouseDownEvent =>
            {
                if (mouseDownEvent.clickCount != 2)
                {
                    return;
                }

                mouseDownEvent.StopPropagation();
                FlipbookFrame boundFrame = row.userData as FlipbookFrame;
                if (boundFrame == null)
                {
                    return;
                }

                InlineRenameEditing.Begin(
                    nameLabel, boundFrame.name, committedName => CommitFrameRename(boundFrame, committedName));
            });
            row.Add(nameLabel);

            Label sizeLabel = new Label();
            sizeLabel.name = "flipbook-frame-size";
            sizeLabel.AddToClassList("toolkit-list-row__meta");
            row.Add(sizeLabel);

            return itemSlot;
        }

        private void BindFrameRow(VisualElement element, int listPosition)
        {
            if (flipbook == null || listPosition < 0 || listPosition >= flipbook.frames.Count)
            {
                return;
            }

            FlipbookFrame frame = flipbook.frames[listPosition];
            VisualElement row = element.Q<VisualElement>("flipbook-frame-row");
            row.userData = frame;

            Image thumbnailImage = row.Q<Image>("flipbook-frame-thumbnail");
            thumbnailImage.image = frame.source != null
                ? frame.source
                : (flipbook.texture != null ? layerThumbnailCache.GetLayerThumbnail(flipbook.texture, frame.index) : null);

            Label nameLabel = row.Q<Label>("flipbook-frame-name");
            nameLabel.text = frame.name;
            row.tooltip = frame.name + "  ·  frame #" + frame.index.ToString();

            Label sizeLabel = row.Q<Label>("flipbook-frame-size");
            if (frame.source == null)
            {
                if (flipbook.texture != null)
                {
                    sizeLabel.text = flipbook.layerSize.x.ToString() + "x" + flipbook.layerSize.y.ToString();
                    sizeLabel.EnableInClassList("toolkit-text--error", false);
                    return;
                }

                sizeLabel.text = "missing";
                sizeLabel.EnableInClassList("toolkit-text--error", false);
                return;
            }

            sizeLabel.text = frame.source.width.ToString() + "x" + frame.source.height.ToString();

            Texture2D firstSource = flipbook.frames[0].source;
            bool mismatched = firstSource != null
                && (frame.source.width != firstSource.width || frame.source.height != firstSource.height);
            sizeLabel.EnableInClassList("toolkit-text--error", mismatched);
        }

        private void OnFrameIndexChanged(int oldListPosition, int newListPosition)
        {
            RenumberFrames();
            RefreshRows();
            FramesChanged?.Invoke();
        }

        private void OnFrameSelectionChanged(IEnumerable<object> selectedItems)
        {
            foreach (int selectedIndex in framesListView.selectedIndices)
            {
                FrameSelected?.Invoke(selectedIndex);
                return;
            }
        }

        private void OnFramesListKeyDown(KeyDownEvent keyDownEvent)
        {
            if (IsImportedMode)
            {
                return;
            }

            if (keyDownEvent.keyCode == KeyCode.Delete || keyDownEvent.keyCode == KeyCode.Backspace)
            {
                RemoveSelectedFrames();
                keyDownEvent.StopPropagation();
            }
        }

        private void CommitFrameRename(FlipbookFrame frame, string committedName)
        {
            string trimmedName = committedName != null ? committedName.Trim() : string.Empty;
            if (trimmedName.Length == 0 || trimmedName == frame.name)
            {
                RefreshRows();
                return;
            }

            HashSet<string> takenNames = new HashSet<string>();
            for (int frameIndex = 0; frameIndex < flipbook.frames.Count; frameIndex++)
            {
                FlipbookFrame otherFrame = flipbook.frames[frameIndex];
                if (otherFrame != frame)
                {
                    takenNames.Add(otherFrame.name);
                }
            }

            frame.name = FlipbookValidation.DedupeFrameName(trimmedName, takenNames);
            RefreshRows();
            FramesChanged?.Invoke();
        }

        private void RemoveSelectedFrames()
        {
            if (IsImportedMode || flipbook == null)
            {
                return;
            }

            List<int> selectedPositions = new List<int>(framesListView.selectedIndices);
            if (selectedPositions.Count == 0)
            {
                return;
            }

            selectedPositions.Sort();
            for (int removeIndex = selectedPositions.Count - 1; removeIndex >= 0; removeIndex--)
            {
                int listPosition = selectedPositions[removeIndex];
                if (listPosition >= 0 && listPosition < flipbook.frames.Count)
                {
                    flipbook.frames.RemoveAt(listPosition);
                }
            }

            RenumberFrames();
            RefreshRows();
            FramesChanged?.Invoke();
        }

        private void RenumberFrames()
        {
            if (flipbook == null)
            {
                return;
            }

            for (int listPosition = 0; listPosition < flipbook.frames.Count; listPosition++)
            {
                flipbook.frames[listPosition].index = listPosition;
            }
        }

        private void OnDragUpdated(DragUpdatedEvent dragUpdatedEvent)
        {
            if (!IsImportedMode && ContainsDraggedTexture())
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
        }

        private void OnDragPerformed(DragPerformEvent dragPerformEvent)
        {
            if (IsImportedMode || !ContainsDraggedTexture())
            {
                return;
            }

            DragAndDrop.AcceptDrag();

            List<Texture2D> draggedTextures = new List<Texture2D>();
            UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
            for (int objectIndex = 0; objectIndex < draggedObjects.Length; objectIndex++)
            {
                Texture2D draggedTexture = draggedObjects[objectIndex] as Texture2D;
                if (draggedTexture != null)
                {
                    draggedTextures.Add(draggedTexture);
                }
            }

            AddSources(draggedTextures);
        }

        private bool ContainsDraggedTexture()
        {
            UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
            for (int objectIndex = 0; objectIndex < draggedObjects.Length; objectIndex++)
            {
                if (draggedObjects[objectIndex] is Texture2D)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
