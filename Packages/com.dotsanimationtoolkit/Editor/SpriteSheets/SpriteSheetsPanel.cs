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
        public Texture2DArray LoadedArray { get; private set; }
        public bool HasUnsavedChanges { get; private set; }

        private readonly SpriteSheetCatalogColumn catalog;
        private readonly ImageCatalogColumn images;
        private readonly SpriteSheetFramesColumn frames;
        private readonly SpriteSheetPreviewElement preview;
        private readonly SpriteSheetBaker baker = new SpriteSheetBaker();

        private readonly Label sheetLabel;
        private readonly Label infoLabel;
        private readonly Label outputPathLabel;
        private readonly Label importedHintLabel;
        private readonly Label depthWarningLabel;
        private readonly EnumField filterModeField;
        private readonly EnumField wrapModeField;
        private readonly Toggle generateMipsToggle;
        private readonly Toggle linearToggle;
        private readonly ObjectField importSettingsSourceField;
        private readonly Button bakeButton;
        private readonly Button saveButton;
        private readonly VisualElement outputRow;
        private readonly VisualElement headerActions;
        private readonly VisualElement bodyHost;

        private SpriteSheetAsset workingCopy;

        public SpriteSheetsPanel()
        {
            style.flexGrow = 1f;

            catalog = new SpriteSheetCatalogColumn();
            catalog.SheetSelected += OnSheetSelected;
            catalog.ArraySelected += OnArraySelected;
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

            importSettingsSourceField = new ObjectField("Match import settings of")
            {
                objectType = typeof(Texture2DArray),
                allowSceneObjects = false,
                tooltip = "Bake copies this array's import settings (compression, filter, mips, sRGB). Empty uses the project defaults."
            };
            importSettingsSourceField.name = "sprite-sheet-import-settings-source";
            importSettingsSourceField.RegisterValueChangedCallback(OnImportSettingsSourceChanged);

            bakeButton = ToolkitIcons.MakeIconTextButton(
                Bake, "d_PreTextureRGB",
                "Compose the frames into a grid PNG at the output path and import it as a Texture2DArray.", "Bake");

            saveButton = ToolkitIcons.MakeIconTextButton(Save, "d_SaveAs", "Write this sheet to its asset.", "Save");

            headerActions = new VisualElement();
            headerActions.AddToClassList("toolkit-pane-actions");
            headerActions.Add(filterModeField);
            headerActions.Add(wrapModeField);
            headerActions.Add(generateMipsToggle);
            headerActions.Add(linearToggle);
            headerActions.Add(importSettingsSourceField);
            headerActions.Add(bakeButton);
            headerActions.Add(saveButton);

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            header.Add(sheetLabel);
            header.Add(infoLabel);
            header.Add(headerActions);

            importedHintLabel = new Label("The importer owns the layer order: rename frames, then Save to keep the names.");
            importedHintLabel.name = "sprite-sheet-imported-hint";
            importedHintLabel.AddToClassList("clip-editor__hint");
            importedHintLabel.style.display = DisplayStyle.None;

            depthWarningLabel = new Label();
            depthWarningLabel.name = "sprite-sheet-depth-warning";
            depthWarningLabel.style.display = DisplayStyle.None;

            outputPathLabel = new Label();
            Button chooseOutputButton = new Button(OnChooseOutputPathClicked) { text = "…" };

            outputRow = new VisualElement();
            outputRow.style.flexDirection = FlexDirection.Row;
            outputRow.Add(outputPathLabel);
            outputRow.Add(chooseOutputButton);

            VisualElement sheetColumn = new VisualElement();
            sheetColumn.style.flexGrow = 1f;
            sheetColumn.Add(header);
            sheetColumn.Add(importedHintLabel);
            sheetColumn.Add(depthWarningLabel);
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
            LoadedArray = null;
            workingCopy = SpriteSheetAssetUtility.CreateWorkingCopy(sheet);

            if (!workingCopy.IsImportedArray && string.IsNullOrEmpty(workingCopy.outputPath))
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
            importSettingsSourceField.SetValueWithoutNotify(workingCopy.importSettingsSource);
            outputPathLabel.text = "out: " + workingCopy.outputPath;

            HasUnsavedChanges = false;

            int droppedFrameCount = 0;
            if (workingCopy.IsImportedArray)
            {
                int frameCountBefore = workingCopy.frames.Count;
                droppedFrameCount = SpriteSheetAssetUtility.ReconcileFramesWithArrayDepth(workingCopy);
                if (workingCopy.frames.Count != frameCountBefore)
                {
                    HasUnsavedChanges = true;
                }
            }

            SetControlsEnabled(true);
            RefreshSheetLabel();
            RefreshInfoLabel();
            RefreshModeControls();

            depthWarningLabel.text = droppedFrameCount + " frame names dropped: the array has fewer layers now.";
            depthWarningLabel.style.display = droppedFrameCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            catalog.SetSelectedSheet(sheet);

            string sheetAssetPath = AssetDatabase.GetAssetPath(sheet);
            string sheetFolder = System.IO.Path.GetDirectoryName(sheetAssetPath);
            if (!string.IsNullOrEmpty(sheetFolder))
            {
                SpriteSheetAssetUtility.RememberSheetFolder(sheetFolder.Replace('\\', '/'));
            }
        }

        public void LoadArray(Texture2DArray array)
        {
            if (workingCopy != null)
            {
                UnityEngine.Object.DestroyImmediate(workingCopy);
                workingCopy = null;
            }

            LoadedSheet = null;
            LoadedArray = array;
            workingCopy = SpriteSheetAssetUtility.CreateWorkingCopyForArray(array);

            if (workingCopy == null)
            {
                LoadedArray = null;
                frames.SetSheet(null);
                preview.SetSheet(null);
                SetControlsEnabled(false);
                RefreshSheetLabel();
                RefreshInfoLabel();
                RefreshModeControls();
                depthWarningLabel.style.display = DisplayStyle.None;
                return;
            }

            frames.SetSheet(workingCopy);
            preview.SetSheet(workingCopy);

            filterModeField.SetValueWithoutNotify(workingCopy.filterMode);
            wrapModeField.SetValueWithoutNotify(workingCopy.wrapMode);
            generateMipsToggle.SetValueWithoutNotify(workingCopy.generateMips);
            linearToggle.SetValueWithoutNotify(workingCopy.linear);
            importSettingsSourceField.SetValueWithoutNotify(workingCopy.importSettingsSource);
            outputPathLabel.text = "out: " + workingCopy.outputPath;

            HasUnsavedChanges = false;
            SetControlsEnabled(true);
            RefreshSheetLabel();
            RefreshInfoLabel();
            RefreshModeControls();
            depthWarningLabel.style.display = DisplayStyle.None;

            catalog.SetSelectedArray(array);
        }

        public void Dispose()
        {
            baker.ClearSourceCache();
            if (workingCopy != null)
            {
                UnityEngine.Object.DestroyImmediate(workingCopy);
                workingCopy = null;
            }

            preview.Dispose();
            frames.Dispose();
        }

        private void OnSheetSelected(SpriteSheetAsset sheet)
        {
            if (ReferenceEquals(sheet, LoadedSheet))
            {
                return;
            }

            if (!ConfirmDiscardIfUnsaved())
            {
                RestoreCatalogSelection();
                return;
            }

            LoadSheet(sheet);
        }

        private void OnArraySelected(Texture2DArray array)
        {
            if (ReferenceEquals(array, LoadedArray))
            {
                return;
            }

            if (!ConfirmDiscardIfUnsaved())
            {
                RestoreCatalogSelection();
                return;
            }

            LoadArray(array);
        }

        private void RestoreCatalogSelection()
        {
            if (LoadedSheet != null)
            {
                catalog.SetSelectedSheet(LoadedSheet);
            }
            else
            {
                catalog.SetSelectedArray(LoadedArray);
            }
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
                LoadedArray = null;
                frames.SetSheet(null);
                preview.SetSheet(null);
                SetControlsEnabled(false);
                RefreshSheetLabel();
                RefreshInfoLabel();
                RefreshModeControls();
                depthWarningLabel.style.display = DisplayStyle.None;
            }

            SpriteSheetAssetUtility.TrashSheet(sheet);
            catalog.RescanProject();
        }

        private void OnImagesActivated(IReadOnlyList<Texture2D> activatedTextures)
        {
            if (workingCopy == null || workingCopy.IsImportedArray)
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

        private void OnImportSettingsSourceChanged(ChangeEvent<UnityEngine.Object> changeEvent)
        {
            if (workingCopy == null)
            {
                return;
            }

            workingCopy.importSettingsSource = changeEvent.newValue as Texture2DArray;
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
                ? "T_SpriteSheet_Array"
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
                "Sprite sheet output", startName, "png",
                "Choose where the grid PNG is written; it imports as a Texture2DArray.", startDirectory);

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
            if (workingCopy == null || workingCopy.IsImportedArray)
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
            RefreshModeControls();
            EditorGUIUtility.PingObject(workingCopy.texture);
        }

        private void Save()
        {
            if (workingCopy == null)
            {
                return;
            }

            if (LoadedArray != null)
            {
                SpriteSheetAsset namesSheet = SpriteSheetAssetUtility.GetOrCreateSheetForArray(LoadedArray);
                if (namesSheet == null)
                {
                    EditorUtility.DisplayDialog(
                        "Save failed",
                        "Could not create a names asset beside '" + LoadedArray.name + "'.",
                        "OK");
                    return;
                }

                workingCopy.name = namesSheet.name;
                SpriteSheetAssetUtility.SaveWorkingCopy(workingCopy, namesSheet);
                LoadedSheet = namesSheet;
                LoadedArray = null;
                catalog.RescanProject();
                catalog.SetSelectedSheet(namesSheet);
            }
            else
            {
                SpriteSheetAssetUtility.SaveWorkingCopy(workingCopy, LoadedSheet);
            }

            HasUnsavedChanges = false;
            RefreshSheetLabel();
            RefreshInfoLabel();
            RefreshModeControls();
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
            RefreshModeControls();
        }

        private void RefreshSheetLabel()
        {
            string baseText = LoadedSheet != null
                ? LoadedSheet.name
                : LoadedArray != null ? LoadedArray.name : "No sheet";
            sheetLabel.text = HasUnsavedChanges ? baseText + "  ●" : baseText;
        }

        private void RefreshModeControls()
        {
            bool importedMode = workingCopy != null && workingCopy.IsImportedArray;

            bakeButton.SetEnabled(!importedMode);
            importSettingsSourceField.SetEnabled(!importedMode);
            filterModeField.SetEnabled(!importedMode);
            wrapModeField.SetEnabled(!importedMode);
            generateMipsToggle.SetEnabled(!importedMode);
            linearToggle.SetEnabled(!importedMode);

            outputRow.style.display = importedMode ? DisplayStyle.None : DisplayStyle.Flex;
            importedHintLabel.style.display = importedMode ? DisplayStyle.Flex : DisplayStyle.None;

            if (importedMode)
            {
                TextureImporter arrayImporter =
                    AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(workingCopy.texture)) as TextureImporter;

                if (arrayImporter != null)
                {
                    filterModeField.SetValueWithoutNotify(arrayImporter.filterMode);
                    wrapModeField.SetValueWithoutNotify(arrayImporter.wrapMode);
                    generateMipsToggle.SetValueWithoutNotify(arrayImporter.mipmapEnabled);
                    linearToggle.SetValueWithoutNotify(!arrayImporter.sRGBTexture);
                }
                else if (workingCopy.texture != null)
                {
                    filterModeField.SetValueWithoutNotify(workingCopy.texture.filterMode);
                    wrapModeField.SetValueWithoutNotify(workingCopy.texture.wrapMode);
                    generateMipsToggle.SetValueWithoutNotify(workingCopy.texture.mipmapCount > 1);
                    linearToggle.SetValueWithoutNotify(false);
                }
            }

            saveButton.SetEnabled(workingCopy != null
                && (LoadedArray == null || AnyFrameNameDiffersFromLayerIndex(workingCopy)));
        }

        private static bool AnyFrameNameDiffersFromLayerIndex(SpriteSheetAsset sheet)
        {
            for (int frameListPosition = 0; frameListPosition < sheet.frames.Count; frameListPosition++)
            {
                SpriteSheetFrame frame = sheet.frames[frameListPosition];
                if (frame.name != frame.index.ToString())
                {
                    return true;
                }
            }

            return false;
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
