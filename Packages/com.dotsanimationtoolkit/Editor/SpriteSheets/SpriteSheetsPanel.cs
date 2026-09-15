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
    /// <summary>The Sprite Sheets tab: catalogs, frame list and contact sheet around one sheet's working copy, with Bake, Save and discard.</summary>
    public sealed class SpriteSheetsPanel : VisualElement, IDisposable
    {
        public SpriteSheetAsset LoadedSheet { get; private set; }
        public bool HasUnsavedChanges { get; private set; }

        private readonly SpriteSheetCatalogColumn catalog;
        private readonly ImageCatalogColumn images;
        private readonly SpriteSheetFramesColumn frames;
        private readonly SpriteSheetPreviewElement preview;
        private readonly SpriteSheetBaker baker = new SpriteSheetBaker();

        private readonly Label sheetLabel;
        private readonly Label infoLabel;
        private readonly Label outputPathLabel;
        private readonly EnumField filterModeField;
        private readonly EnumField wrapModeField;
        private readonly Toggle generateMipsToggle;
        private readonly Toggle linearToggle;
        private readonly VisualElement headerActions;
        private readonly VisualElement bodyHost;

        private SpriteSheetAsset workingCopy;

        public SpriteSheetsPanel()
        {
            style.flexGrow = 1f;

            catalog = new SpriteSheetCatalogColumn();
            catalog.SheetSelected += OnSheetSelected;
            catalog.NewRequested += OnNewRequested;
            catalog.SheetRenameRequested += OnSheetRenameRequested;
            catalog.SheetDeleteRequested += OnSheetDeleteRequested;

            images = new ImageCatalogColumn();
            images.ImagesActivated += OnImagesActivated;

            CoverPaneSplitView catalogsSplit = new CoverPaneSplitView("SpriteSheets.Catalogs", 0, 260f, TwoPaneSplitViewOrientation.Vertical);
            catalogsSplit.Add(catalog);
            catalogsSplit.Add(images);

            frames = new SpriteSheetFramesColumn();
            frames.FramesChanged += OnFramesChanged;
            frames.FrameSelected += OnFrameSelected;

            preview = new SpriteSheetPreviewElement();
            preview.FrameClicked += OnFrameClicked;

            Slider zoomSlider = new Slider(
                "Zoom", SpriteSheetPreviewElement.MinimumThumbnailSize, SpriteSheetPreviewElement.MaximumThumbnailSize);
            zoomSlider.value = SpriteSheetPreviewElement.DefaultThumbnailSize;
            zoomSlider.RegisterValueChangedCallback(evt => preview.SetThumbnailSize(evt.newValue));

            VisualElement previewColumn = new VisualElement();
            previewColumn.style.flexGrow = 1f;
            previewColumn.Add(zoomSlider);
            previewColumn.Add(preview);

            CoverPaneSplitView framesSplit = new CoverPaneSplitView("SpriteSheets.Frames", 0, 300f, TwoPaneSplitViewOrientation.Horizontal);
            framesSplit.style.flexGrow = 1f;
            framesSplit.Add(frames);
            framesSplit.Add(previewColumn);

            bodyHost = framesSplit;

            sheetLabel = new Label();
            infoLabel = new Label();

            filterModeField = new EnumField("Filter", FilterMode.Bilinear);
            filterModeField.RegisterValueChangedCallback(OnFilterModeChanged);

            wrapModeField = new EnumField("Wrap", TextureWrapMode.Clamp);
            wrapModeField.RegisterValueChangedCallback(OnWrapModeChanged);

            generateMipsToggle = new Toggle("Mips");
            generateMipsToggle.RegisterValueChangedCallback(OnGenerateMipsChanged);

            linearToggle = new Toggle("Linear");
            linearToggle.RegisterValueChangedCallback(OnLinearChanged);

            Button bakeButton = ToolkitIcons.MakeIconTextButton(
                Bake, "d_PreTextureRGB",
                "Stack the frames into the Texture2DArray at the output path, overwriting it in place.", "Bake");

            Button saveButton = ToolkitIcons.MakeIconTextButton(Save, "d_SaveAs", "Write this sheet to its asset.", "Save");

            headerActions = new VisualElement();
            headerActions.AddToClassList("toolkit-pane-actions");
            headerActions.Add(filterModeField);
            headerActions.Add(wrapModeField);
            headerActions.Add(generateMipsToggle);
            headerActions.Add(linearToggle);
            headerActions.Add(bakeButton);
            headerActions.Add(saveButton);

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            header.Add(sheetLabel);
            header.Add(infoLabel);
            header.Add(headerActions);

            outputPathLabel = new Label();
            Button chooseOutputButton = new Button(OnChooseOutputPathClicked) { text = "…" };

            VisualElement outputRow = new VisualElement();
            outputRow.style.flexDirection = FlexDirection.Row;
            outputRow.Add(outputPathLabel);
            outputRow.Add(chooseOutputButton);

            VisualElement sheetColumn = new VisualElement();
            sheetColumn.style.flexGrow = 1f;
            sheetColumn.Add(header);
            sheetColumn.Add(bodyHost);
            sheetColumn.Add(outputRow);

            CoverPaneSplitView sidebarSplit = new CoverPaneSplitView("SpriteSheets.Sidebar", 0, 280f, TwoPaneSplitViewOrientation.Horizontal);
            sidebarSplit.style.flexGrow = 1f;
            sidebarSplit.Add(catalogsSplit);
            sidebarSplit.Add(sheetColumn);
            Add(sidebarSplit);

            RefreshSheetLabel();
            RefreshInfoLabel();
            SetControlsEnabled(false);
        }

        public void RescanProject()
        {
            catalog.RescanProject();
            images.RescanProject();
        }

        public void LoadSheet(SpriteSheetAsset sheet)
        {
            if (workingCopy != null)
            {
                UnityEngine.Object.DestroyImmediate(workingCopy);
            }

            LoadedSheet = sheet;
            workingCopy = SpriteSheetAssetUtility.CreateWorkingCopy(sheet);

            if (string.IsNullOrEmpty(workingCopy.outputPath))
            {
                // A default, not an edit: does not mark the sheet unsaved.
                workingCopy.outputPath = SpriteSheetBaker.DefaultOutputPathFor(AssetDatabase.GetAssetPath(sheet));
            }

            frames.SetSheet(workingCopy);
            preview.SetSheet(workingCopy);

            filterModeField.SetValueWithoutNotify(workingCopy.filterMode);
            wrapModeField.SetValueWithoutNotify(workingCopy.wrapMode);
            generateMipsToggle.SetValueWithoutNotify(workingCopy.generateMips);
            linearToggle.SetValueWithoutNotify(workingCopy.linear);
            outputPathLabel.text = "out: " + workingCopy.outputPath;

            HasUnsavedChanges = false;
            SetControlsEnabled(true);
            RefreshSheetLabel();
            RefreshInfoLabel();

            catalog.SetSelectedSheet(sheet);

            string sheetAssetPath = AssetDatabase.GetAssetPath(sheet);
            string sheetFolder = System.IO.Path.GetDirectoryName(sheetAssetPath);
            if (!string.IsNullOrEmpty(sheetFolder))
            {
                SpriteSheetAssetUtility.RememberSheetFolder(sheetFolder.Replace('\\', '/'));
            }
        }

        public void Dispose()
        {
            baker.ClearSourceCache();
            if (workingCopy != null)
            {
                UnityEngine.Object.DestroyImmediate(workingCopy);
                workingCopy = null;
            }
        }

        private void OnSheetSelected(SpriteSheetAsset sheet)
        {
            if (ReferenceEquals(sheet, LoadedSheet))
            {
                return;
            }

            if (!ConfirmDiscardIfUnsaved())
            {
                catalog.SetSelectedSheet(LoadedSheet);
                return;
            }

            LoadSheet(sheet);
        }

        private void OnNewRequested()
        {
            if (!ConfirmDiscardIfUnsaved())
            {
                return;
            }

            SpriteSheetAsset createdSheet = SpriteSheetAssetUtility.CreateSheetWithPrompt();
            if (createdSheet == null)
            {
                return;
            }

            catalog.RescanProject();
            LoadSheet(createdSheet);
        }

        private void OnSheetRenameRequested(SpriteSheetAsset sheet, string newName)
        {
            bool renamed = SpriteSheetAssetUtility.RenameSheet(sheet, newName);
            if (!renamed)
            {
                return;
            }

            catalog.RescanProject();

            if (ReferenceEquals(sheet, LoadedSheet))
            {
                workingCopy.name = LoadedSheet.name;
                RefreshSheetLabel();
            }
        }

        private void OnSheetDeleteRequested(SpriteSheetAsset sheet)
        {
            bool confirmedDelete = EditorUtility.DisplayDialog(
                "Delete Sprite Sheet",
                "Delete '" + sheet.name + "'? This cannot be undone.",
                "Delete", "Cancel");

            if (!confirmedDelete)
            {
                return;
            }

            if (ReferenceEquals(sheet, LoadedSheet))
            {
                if (workingCopy != null)
                {
                    UnityEngine.Object.DestroyImmediate(workingCopy);
                    workingCopy = null;
                }

                LoadedSheet = null;
                frames.SetSheet(null);
                preview.SetSheet(null);
                SetControlsEnabled(false);
                RefreshSheetLabel();
                RefreshInfoLabel();
            }

            SpriteSheetAssetUtility.TrashSheet(sheet);
            catalog.RescanProject();
        }

        private void OnImagesActivated(IReadOnlyList<Texture2D> activatedTextures)
        {
            if (workingCopy == null)
            {
                return;
            }

            frames.AddSources(activatedTextures);
        }

        private void OnFramesChanged()
        {
            MarkUnsaved();
            preview.Refresh();
            RefreshInfoLabel();
        }

        private void OnFrameSelected(int frameIndex)
        {
            preview.HighlightFrame(frameIndex);
        }

        private void OnFrameClicked(int frameIndex)
        {
            frames.SelectFrame(frameIndex);
        }

        private void OnFilterModeChanged(ChangeEvent<Enum> changeEvent)
        {
            if (workingCopy == null)
            {
                return;
            }

            workingCopy.filterMode = (FilterMode)changeEvent.newValue;
            MarkUnsaved();
        }

        private void OnWrapModeChanged(ChangeEvent<Enum> changeEvent)
        {
            if (workingCopy == null)
            {
                return;
            }

            workingCopy.wrapMode = (TextureWrapMode)changeEvent.newValue;
            MarkUnsaved();
        }

        private void OnGenerateMipsChanged(ChangeEvent<bool> changeEvent)
        {
            if (workingCopy == null)
            {
                return;
            }

            workingCopy.generateMips = changeEvent.newValue;
            MarkUnsaved();
        }

        private void OnLinearChanged(ChangeEvent<bool> changeEvent)
        {
            if (workingCopy == null)
            {
                return;
            }

            workingCopy.linear = changeEvent.newValue;
            MarkUnsaved();
        }

        private void OnChooseOutputPathClicked()
        {
            if (workingCopy == null)
            {
                return;
            }

            string startDirectory = "Assets";
            string startName = string.IsNullOrEmpty(workingCopy.outputPath)
                ? "T_SpriteSheet"
                : System.IO.Path.GetFileNameWithoutExtension(workingCopy.outputPath);

            if (!string.IsNullOrEmpty(workingCopy.outputPath))
            {
                string existingDirectory = System.IO.Path.GetDirectoryName(workingCopy.outputPath);
                if (!string.IsNullOrEmpty(existingDirectory))
                {
                    startDirectory = existingDirectory.Replace('\\', '/');
                }
            }

            string chosenPath = EditorUtility.SaveFilePanelInProject(
                "Sprite sheet output", startName, "asset",
                "Choose where the packed Texture2DArray is written.", startDirectory);

            if (string.IsNullOrEmpty(chosenPath))
            {
                return;
            }

            workingCopy.outputPath = chosenPath;
            outputPathLabel.text = "out: " + workingCopy.outputPath;
            MarkUnsaved();
        }

        private void Bake()
        {
            if (workingCopy == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(workingCopy.outputPath))
            {
                workingCopy.outputPath = SpriteSheetBaker.DefaultOutputPathFor(AssetDatabase.GetAssetPath(LoadedSheet));
                outputPathLabel.text = "out: " + workingCopy.outputPath;
            }

            if (!baker.Bake(workingCopy, out string error))
            {
                EditorUtility.DisplayDialog("Bake failed", error, "OK");
                return;
            }

            MarkUnsaved();
            frames.RefreshRows();
            preview.Refresh();
            RefreshInfoLabel();
            EditorGUIUtility.PingObject(workingCopy.texture);
        }

        private void Save()
        {
            if (workingCopy == null)
            {
                return;
            }

            SpriteSheetAssetUtility.SaveWorkingCopy(workingCopy, LoadedSheet);
            HasUnsavedChanges = false;
            RefreshSheetLabel();
            RefreshInfoLabel();
            catalog.RefreshRows();
        }

        private bool ConfirmDiscardIfUnsaved()
        {
            if (!HasUnsavedChanges)
            {
                return true;
            }

            string message = LoadedSheet != null
                ? "'" + LoadedSheet.name + "' has unsaved changes. Discard them?"
                : "The sheet has unsaved changes.";

            return EditorUtility.DisplayDialog("Unsaved changes", message, "Discard", "Cancel");
        }

        private void MarkUnsaved()
        {
            HasUnsavedChanges = true;
            RefreshSheetLabel();
        }

        private void RefreshSheetLabel()
        {
            string baseText = LoadedSheet != null ? LoadedSheet.name : "No sheet";
            sheetLabel.text = HasUnsavedChanges ? baseText + "  ●" : baseText;
        }

        private void RefreshInfoLabel()
        {
            if (workingCopy == null)
            {
                infoLabel.text = "-";
                return;
            }

            if (workingCopy.texture != null)
            {
                infoLabel.text = workingCopy.layerSize.x + "x" + workingCopy.layerSize.y + " · " + workingCopy.frames.Count + " layers";
                return;
            }

            if (workingCopy.frames.Count > 0 && workingCopy.frames[0].source != null)
            {
                Texture2D firstSource = workingCopy.frames[0].source;
                infoLabel.text = firstSource.width + "x" + firstSource.height + " · " + workingCopy.frames.Count + " layers";
                return;
            }

            infoLabel.text = "-";
        }

        private void SetControlsEnabled(bool enabled)
        {
            headerActions.SetEnabled(enabled);
            bodyHost.SetEnabled(enabled);
        }
    }
}
