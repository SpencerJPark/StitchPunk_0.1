// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The contact sheet: every frame of a sheet as a thumbnail in layer order, the only way to see a Texture2DArray's layers.</summary>
    public sealed class SpriteSheetPreviewElement : VisualElement, IDisposable
    {
        public const float DefaultThumbnailSize = 64f;
        public const float MinimumThumbnailSize = 24f;
        public const float MaximumThumbnailSize = 256f;

        private const string HoverDefaultText = "Hover a frame to name it.";
        private const string SelectedCellClassName = "sprite-sheet-preview-cell--selected";

        private static readonly Color SelectedBorderColor = new Color(0.24f, 0.70f, 0.68f);
        private static readonly Color CellBorderColor = new Color(0.35f, 0.35f, 0.35f);
        private static readonly Color CellBackgroundColor = new Color(0.15f, 0.15f, 0.15f);

        // The list position of the clicked thumbnail.
        public event Action<int> FrameClicked;

        private readonly Label hoverLabel;
        private readonly ScrollView scrollView;
        private readonly VisualElement frameContainer;
        private readonly List<VisualElement> cellElements = new List<VisualElement>();

        private SpriteSheetAsset sheetAsset;
        private float thumbnailSize = DefaultThumbnailSize;
        private int highlightedListPosition = -1;

        public SpriteSheetPreviewElement()
        {
            this.name = "sprite-sheet-preview";
            this.style.flexGrow = 1f;

            this.hoverLabel = new Label(HoverDefaultText)
            {
                name = "sprite-sheet-preview-hover",
            };
            this.Add(this.hoverLabel);

            this.frameContainer = new VisualElement();
            this.frameContainer.style.flexDirection = FlexDirection.Row;
            this.frameContainer.style.flexWrap = Wrap.Wrap;

            this.scrollView = new ScrollView(ScrollViewMode.Vertical);
            this.scrollView.style.flexGrow = 1f;
            this.scrollView.Add(this.frameContainer);
            this.Add(this.scrollView);
        }

        public void SetSheet(SpriteSheetAsset sheet)
        {
            this.sheetAsset = sheet;
            this.Refresh();
        }

        public void Dispose()
        {
        }

        public void SetThumbnailSize(float thumbnailPixels)
        {
            this.thumbnailSize = Mathf.Clamp(thumbnailPixels, MinimumThumbnailSize, MaximumThumbnailSize);

            foreach (VisualElement cellElement in this.cellElements)
            {
                cellElement.style.width = this.thumbnailSize;
                cellElement.style.height = this.thumbnailSize;
            }
        }

        public void Refresh()
        {
            this.frameContainer.Clear();
            this.cellElements.Clear();
            this.hoverLabel.text = HoverDefaultText;

            if (this.sheetAsset == null)
            {
                this.frameContainer.Add(new Label("No sheet selected."));
                return;
            }

            if (this.sheetAsset.frames == null || this.sheetAsset.frames.Count == 0)
            {
                this.frameContainer.Add(new Label("No frames yet."));
                return;
            }

            for (int listPosition = 0; listPosition < this.sheetAsset.frames.Count; listPosition++)
            {
                SpriteSheetFrame frame = this.sheetAsset.frames[listPosition];
                VisualElement cellElement = this.BuildCellElement(frame, listPosition);
                this.cellElements.Add(cellElement);
                this.frameContainer.Add(cellElement);
            }

            if (this.highlightedListPosition >= 0 && this.highlightedListPosition < this.cellElements.Count)
            {
                this.ApplyHighlightBorder(this.cellElements[this.highlightedListPosition]);
            }
            else
            {
                this.highlightedListPosition = -1;
            }
        }

        public void HighlightFrame(int listPosition)
        {
            if (this.highlightedListPosition >= 0 && this.highlightedListPosition < this.cellElements.Count)
            {
                this.ClearHighlightBorder(this.cellElements[this.highlightedListPosition]);
            }

            if (listPosition < 0 || listPosition >= this.cellElements.Count)
            {
                this.highlightedListPosition = -1;
                return;
            }

            this.highlightedListPosition = listPosition;
            this.ApplyHighlightBorder(this.cellElements[listPosition]);
        }

        private VisualElement BuildCellElement(SpriteSheetFrame frame, int listPosition)
        {
            VisualElement cellElement = new VisualElement
            {
                tooltip = frame.name + "  #" + frame.index,
            };
            cellElement.style.width = this.thumbnailSize;
            cellElement.style.height = this.thumbnailSize;
            cellElement.style.marginTop = 2f;
            cellElement.style.marginBottom = 2f;
            cellElement.style.marginLeft = 2f;
            cellElement.style.marginRight = 2f;
            cellElement.style.borderTopWidth = 1f;
            cellElement.style.borderBottomWidth = 1f;
            cellElement.style.borderLeftWidth = 1f;
            cellElement.style.borderRightWidth = 1f;
            this.SetCellBorderColor(cellElement, CellBorderColor);
            cellElement.style.backgroundColor = CellBackgroundColor;

            if (frame.source != null)
            {
                Image thumbnailImage = new Image
                {
                    image = frame.source,
                    scaleMode = ScaleMode.ScaleToFit,
                };
                thumbnailImage.style.width = new Length(100f, LengthUnit.Percent);
                thumbnailImage.style.height = new Length(100f, LengthUnit.Percent);
                cellElement.Add(thumbnailImage);
            }
            else
            {
                Label missingLabel = new Label("missing");
                cellElement.Add(missingLabel);
            }

            cellElement.RegisterCallback<PointerEnterEvent>(pointerEnterEvent =>
            {
                this.hoverLabel.text = frame.name + "  #" + frame.index;
            });
            cellElement.RegisterCallback<PointerLeaveEvent>(pointerLeaveEvent =>
            {
                this.hoverLabel.text = HoverDefaultText;
            });
            cellElement.RegisterCallback<ClickEvent>(clickEvent =>
            {
                this.HighlightFrame(listPosition);
                this.FrameClicked?.Invoke(listPosition);
            });

            return cellElement;
        }

        private void ApplyHighlightBorder(VisualElement cellElement)
        {
            cellElement.style.borderTopWidth = 2f;
            cellElement.style.borderBottomWidth = 2f;
            cellElement.style.borderLeftWidth = 2f;
            cellElement.style.borderRightWidth = 2f;
            this.SetCellBorderColor(cellElement, SelectedBorderColor);
            cellElement.AddToClassList(SelectedCellClassName);
        }

        private void ClearHighlightBorder(VisualElement cellElement)
        {
            cellElement.style.borderTopWidth = 1f;
            cellElement.style.borderBottomWidth = 1f;
            cellElement.style.borderLeftWidth = 1f;
            cellElement.style.borderRightWidth = 1f;
            this.SetCellBorderColor(cellElement, CellBorderColor);
            cellElement.RemoveFromClassList(SelectedCellClassName);
        }

        private void SetCellBorderColor(VisualElement cellElement, Color borderColor)
        {
            cellElement.style.borderTopColor = borderColor;
            cellElement.style.borderBottomColor = borderColor;
            cellElement.style.borderLeftColor = borderColor;
            cellElement.style.borderRightColor = borderColor;
        }
    }
}
