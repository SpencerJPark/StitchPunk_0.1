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
    /// <summary>The Flipbooks tab: catalogs, frame list and contact sheet around one flipbook's working copy, with Bake, Save and discard.</summary>
    public sealed class FlipbooksPanel : VisualElement, IDisposable
    {
        public FlipbookAsset LoadedFlipbook { get; private set; }
        public Texture2DArray LoadedArray { get; private set; }
        public bool HasUnsavedChanges { get; private set; }

        private readonly FlipbookCatalogColumn catalog;
        private readonly ImageCatalogColumn images;
        private readonly FlipbookFramesColumn frames;
        private readonly FlipbookPreviewElement preview;
        private readonly FlipbookBaker baker = new FlipbookBaker();

        private readonly Label flipbookLabel;
        private readonly Label infoLabel;
        private readonly PathPickerRowElement outputPathRow;
        private readonly Label importedHintLabel;
        private readonly Label depthWarningLabel;
        private readonly EnumField filterModeField;
        private readonly EnumField wrapModeField;
        private readonly Toggle generateMipsToggle;
        private readonly Toggle linearToggle;
        private readonly ObjectField importSettingsSourceField;
        private readonly Button bakeButton;
        private readonly Button saveButton;
        private readonly Button makeEditableButton;

        private const string MakeEditableTooltip =
            "Copy every layer out as a PNG frame and make a flipbook you can reorder, rename and bake. The imported array is left untouched.";
        private const string BakeButtonNormalTooltip =
            "Compose the frames into a grid PNG at the output path and import it as a Texture2DArray.";
        private const string BakeButtonImportedTooltip =
            "Unity's importer builds this array. Press Make editable to bake your own copy.";
        private const string SaveButtonNormalTooltip = "Write this flipbook to its asset.";
        private const string SaveButtonImportedDisabledTooltip =
            "Rename a frame to enable Save (it stores the frame names).";
        private readonly VisualElement headerActions;
        private readonly VisualElement bodyHost;
        private readonly VisualElement flipbookContent;
        private readonly VisualElement noFlipbookEmptyState;
        private readonly VisualElement outputCard;
        private const string NothingSelectedTooltip = "Select a flipbook on the left first.";

        private FlipbookAsset workingCopy;

        public FlipbooksPanel()
        {
            style.flexGrow = 1f;

            catalog = new FlipbookCatalogColumn();
            catalog.FlipbookSelected += OnFlipbookSelected;
            catalog.ArraySelected += OnArraySelected;
            catalog.NewRequested += OnNewRequested;
            catalog.FlipbookRenameRequested += OnFlipbookRenameRequested;
            catalog.FlipbookDeleteRequested += OnFlipbookDeleteRequested;

            images = new ImageCatalogColumn();
            images.ImagesActivated += OnImagesActivated;

            // FlipbookCatalogColumn's own options.title is non-empty ("Flipbooks"), so its
            // internal header already carries HeaderActions once; the sidebar mode header renders
            // the same HeaderActions a second time above it.
            CatalogSidebarElement sidebar = new CatalogSidebarElement { name = "flipbooks-sidebar" };
            sidebar.AddMode("flipbooks", "Flipbooks", catalog, catalog.HeaderActions);
            sidebar.AddMode("images", "Images", images, images.HeaderActions);
            sidebar.SetMode("flipbooks");

            frames = new FlipbookFramesColumn();
            frames.FramesChanged += OnFramesChanged;
            frames.FrameSelected += OnFrameSelected;

            preview = new FlipbookPreviewElement();
            preview.FrameClicked += OnFrameClicked;

            Slider zoomSlider = new Slider(
                "Zoom", FlipbookPreviewElement.MinimumThumbnailSize, FlipbookPreviewElement.MaximumThumbnailSize);
            zoomSlider.value = FlipbookPreviewElement.DefaultThumbnailSize;
            zoomSlider.RegisterValueChangedCallback(
                zoomChangeEvent => preview.SetThumbnailSize(zoomChangeEvent.newValue));
            zoomSlider.style.flexGrow = 1f;

            VisualElement zoomToolbar = new VisualElement();
            zoomToolbar.AddToClassList("toolkit-pane-actions");
            zoomToolbar.AddToClassList("flipbooks-zoom-toolbar");
            zoomToolbar.style.height = 24f;
            zoomToolbar.style.flexShrink = 0f;
            zoomToolbar.Add(zoomSlider);

            VisualElement previewColumn = new VisualElement();
            previewColumn.style.flexGrow = 1f;
            previewColumn.Add(zoomToolbar);
            previewColumn.Add(preview);

            VisualElement previewStatusRow = ToolkitChrome.MakeStatusRow(out Label previewStatusLabel, out _, true);
            previewStatusRow.AddToClassList("flipbooks-status-row");
            ToolkitChrome.SetStatus(previewStatusLabel, FlipbookPreviewElement.DefaultHoverText, ToolkitStatusTone.Neutral);
            preview.HoverTextChanged += hoverText => ToolkitChrome.SetStatus(previewStatusLabel, hoverText, ToolkitStatusTone.Neutral);
            previewColumn.Add(previewStatusRow);

            CoverPaneSplitView framesSplit = new CoverPaneSplitView("Flipbooks.Frames", 0, 300f, TwoPaneSplitViewOrientation.Horizontal);
            framesSplit.style.flexGrow = 1f;
            // Shrinks below its content so the Output row under it stays on screen.
            framesSplit.style.flexShrink = 1f;
            framesSplit.style.minHeight = 0f;
            framesSplit.Add(frames);
            framesSplit.Add(previewColumn);

            framesSplit.AddToClassList("flipbooks-section-gap");
            bodyHost = framesSplit;

            filterModeField = new EnumField(FilterMode.Bilinear);
            filterModeField.RegisterValueChangedCallback(OnFilterModeChanged);

            wrapModeField = new EnumField(TextureWrapMode.Clamp);
            wrapModeField.RegisterValueChangedCallback(OnWrapModeChanged);

            generateMipsToggle = new Toggle();
            generateMipsToggle.RegisterValueChangedCallback(OnGenerateMipsChanged);

            linearToggle = new Toggle();
            linearToggle.RegisterValueChangedCallback(OnLinearChanged);

            const string importSettingsSourceTooltip =
                "Bake copies this array's import settings (compression, filter, mips, sRGB). Empty uses the project defaults.";
            importSettingsSourceField = new ObjectField
            {
                objectType = typeof(Texture2DArray),
                allowSceneObjects = false,
                tooltip = importSettingsSourceTooltip
            };
            importSettingsSourceField.name = "flipbook-import-settings-source";
            importSettingsSourceField.RegisterValueChangedCallback(OnImportSettingsSourceChanged);

            bakeButton = ToolkitChrome.MakePrimaryAction(
                Bake, "d_PreTextureRGB",
                "Compose the frames into a grid PNG at the output path and import it as a Texture2DArray.", "Bake");
            ToolkitIcons.SetButtonGlyph(bakeButton, ToolkitGlyphId.VatBake);

            saveButton = ToolkitIcons.MakeIconTextButton(Save, "d_SaveAs", "Write this flipbook to its asset.", "Save");

            VisualElement header = ToolkitChrome.MakePaneHeader(string.Empty, out flipbookLabel, out headerActions);
            infoLabel = ToolkitChrome.MakeBadge(string.Empty, ToolkitStatusTone.Neutral);
            makeEditableButton = ToolkitChrome.MakeSecondaryAction(
                OnMakeEditableClicked, "d_Toolbar Plus", MakeEditableTooltip, "Make editable");
            makeEditableButton.name = "flipbooks-make-editable-button";
            makeEditableButton.style.display = DisplayStyle.None;

            // In the actions run, not between title and actions: the header spreads its children apart,
            // which left the badge floating mid-header.
            headerActions.Add(infoLabel);
            headerActions.Add(makeEditableButton);
            headerActions.Add(bakeButton);
            headerActions.Add(saveButton);

            importedHintLabel = ToolkitChrome.MakeHint(
                "Imported array: Unity's importer owns the layers. Rename frames and Save to keep the names, or press Make editable to reorder, remove and bake.");
            importedHintLabel.name = "flipbook-imported-hint";
            importedHintLabel.style.display = DisplayStyle.None;
            importedHintLabel.style.flexShrink = 0f;

            depthWarningLabel = new Label();
            depthWarningLabel.name = "flipbook-depth-warning";
            depthWarningLabel.AddToClassList("toolkit-hint");
            depthWarningLabel.AddToClassList("toolkit-text--warning");
            depthWarningLabel.style.display = DisplayStyle.None;
            depthWarningLabel.style.flexShrink = 0f;

            VisualElement importSettingsCard = ToolkitChrome.MakeCard(
                "flipbook-import-settings-card", "Import settings",
                out VisualElement importSettingsCardBody, out VisualElement importSettingsCardHeaderActions);
            importSettingsCard.style.flexShrink = 0f;

            importSettingsCardBody.Add(ToolkitChrome.MakePropertyRow("Filter", filterModeField, null));
            importSettingsCardBody.Add(ToolkitChrome.MakePropertyRow("Wrap", wrapModeField, null));
            importSettingsCardBody.Add(ToolkitChrome.MakePropertyRow("Mips", generateMipsToggle, null));
            importSettingsCardBody.Add(ToolkitChrome.MakePropertyRow("Linear", linearToggle, null));
            importSettingsCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Match import settings of", importSettingsSourceField, importSettingsSourceTooltip));

            outputPathRow = new PathPickerRowElement(null, "Choose where the baked flipbook is written.");
            outputPathRow.BrowseRequested += OnChooseOutputPathClicked;
            outputPathRow.style.flexShrink = 0f;

            outputCard = ToolkitChrome.MakeCard("flipbook-output-card", "Output", out VisualElement outputCardBody, out _);
            outputCard.style.flexShrink = 0f;
            outputCardBody.Add(ToolkitChrome.MakePropertyRow("Folder", outputPathRow, null));

            flipbookContent = new VisualElement { name = "flipbook-content" };
            flipbookContent.style.flexGrow = 1f;
            flipbookContent.style.minHeight = 0f;
            flipbookContent.Add(importSettingsCard);
            flipbookContent.Add(bodyHost);
            flipbookContent.Add(outputCard);

            noFlipbookEmptyState = ToolkitChrome.MakeEmptyState(
                "flipbooks-empty-state", "No flipbook selected",
                "Pick a flipbook on the left, or create one from images.", "New flipbook", OnNewRequested);

            VisualElement flipbookColumn = new VisualElement();
            flipbookColumn.AddToClassList("toolkit-column");
            flipbookColumn.Add(header);
            flipbookColumn.Add(importedHintLabel);
            flipbookColumn.Add(depthWarningLabel);
            flipbookColumn.Add(noFlipbookEmptyState);
            flipbookColumn.Add(flipbookContent);

            CoverPaneSplitView sidebarSplit = new CoverPaneSplitView("Flipbooks.Sidebar", 0, 280f, TwoPaneSplitViewOrientation.Horizontal);
            sidebarSplit.style.flexGrow = 1f;
            sidebarSplit.Add(sidebar);
            sidebarSplit.Add(flipbookColumn);
            Add(sidebarSplit);

            RefreshFlipbookLabel();
            RefreshInfoLabel();
            SetControlsEnabled(false);
            RefreshModeControls();
        }

        public void RescanProject()
        {
            catalog.RescanProject();
            images.RescanProject();
        }

        public void LoadFlipbook(FlipbookAsset flipbook)
        {
            if (workingCopy != null)
            {
                UnityEngine.Object.DestroyImmediate(workingCopy);
            }

            LoadedFlipbook = flipbook;
            LoadedArray = null;
            workingCopy = FlipbookAssetUtility.CreateWorkingCopy(flipbook);

            if (!workingCopy.IsImportedArray && string.IsNullOrEmpty(workingCopy.outputPath))
            {
                // A default, not an edit: does not mark the flipbook unsaved.
                workingCopy.outputPath = FlipbookBaker.DefaultOutputPathFor(AssetDatabase.GetAssetPath(flipbook));
            }

            frames.SetFlipbook(workingCopy);
            preview.SetFlipbook(workingCopy);

            filterModeField.SetValueWithoutNotify(workingCopy.filterMode);
            wrapModeField.SetValueWithoutNotify(workingCopy.wrapMode);
            generateMipsToggle.SetValueWithoutNotify(workingCopy.generateMips);
            linearToggle.SetValueWithoutNotify(workingCopy.linear);
            importSettingsSourceField.SetValueWithoutNotify(workingCopy.importSettingsSource);
            outputPathRow.Path = workingCopy.outputPath;

            HasUnsavedChanges = false;

            int droppedFrameCount = 0;
            if (workingCopy.IsImportedArray)
            {
                int frameCountBefore = workingCopy.frames.Count;
                droppedFrameCount = FlipbookAssetUtility.ReconcileFramesWithArrayDepth(workingCopy);
                if (workingCopy.frames.Count != frameCountBefore)
                {
                    HasUnsavedChanges = true;
                }
            }

            SetControlsEnabled(true);
            RefreshFlipbookLabel();
            RefreshInfoLabel();
            RefreshModeControls();

            depthWarningLabel.text = droppedFrameCount + " frame names dropped: the array has fewer layers now.";
            depthWarningLabel.style.display = droppedFrameCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            catalog.SetSelectedFlipbook(flipbook);

            string flipbookAssetPath = AssetDatabase.GetAssetPath(flipbook);
            string flipbookFolder = System.IO.Path.GetDirectoryName(flipbookAssetPath);
            if (!string.IsNullOrEmpty(flipbookFolder))
            {
                FlipbookAssetUtility.RememberFlipbookFolder(flipbookFolder.Replace('\\', '/'));
            }
        }

        public void LoadArray(Texture2DArray array)
        {
            if (workingCopy != null)
            {
                UnityEngine.Object.DestroyImmediate(workingCopy);
                workingCopy = null;
            }

            LoadedFlipbook = null;
            LoadedArray = array;
            workingCopy = FlipbookAssetUtility.CreateWorkingCopyForArray(array);

            if (workingCopy == null)
            {
                LoadedArray = null;
                frames.SetFlipbook(null);
                preview.SetFlipbook(null);
                SetControlsEnabled(false);
                RefreshFlipbookLabel();
                RefreshInfoLabel();
                RefreshModeControls();
                depthWarningLabel.style.display = DisplayStyle.None;
                return;
            }

            frames.SetFlipbook(workingCopy);
            preview.SetFlipbook(workingCopy);

            filterModeField.SetValueWithoutNotify(workingCopy.filterMode);
            wrapModeField.SetValueWithoutNotify(workingCopy.wrapMode);
            generateMipsToggle.SetValueWithoutNotify(workingCopy.generateMips);
            linearToggle.SetValueWithoutNotify(workingCopy.linear);
            importSettingsSourceField.SetValueWithoutNotify(workingCopy.importSettingsSource);
            outputPathRow.Path = workingCopy.outputPath;

            HasUnsavedChanges = false;
            SetControlsEnabled(true);
            RefreshFlipbookLabel();
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

        private void OnFlipbookSelected(FlipbookAsset flipbook)
        {
            if (ReferenceEquals(flipbook, LoadedFlipbook))
            {
                return;
            }

            if (!ConfirmDiscardIfUnsaved())
            {
                RestoreCatalogSelection();
                return;
            }

            LoadFlipbook(flipbook);
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
            if (LoadedFlipbook != null)
            {
                catalog.SetSelectedFlipbook(LoadedFlipbook);
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

            FlipbookAsset createdFlipbook = FlipbookAssetUtility.CreateFlipbookWithPrompt();
            if (createdFlipbook == null)
            {
                return;
            }

            catalog.RescanProject();
            LoadFlipbook(createdFlipbook);
        }

        private void OnFlipbookRenameRequested(FlipbookAsset flipbook, string newName)
        {
            bool renamed = FlipbookAssetUtility.RenameFlipbook(flipbook, newName);
            if (!renamed)
            {
                return;
            }

            catalog.RescanProject();

            if (ReferenceEquals(flipbook, LoadedFlipbook))
            {
                workingCopy.name = LoadedFlipbook.name;
                RefreshFlipbookLabel();
            }
        }

        private void OnFlipbookDeleteRequested(FlipbookAsset flipbook)
        {
            bool confirmedDelete = EditorUtility.DisplayDialog(
                "Delete Flipbook",
                "Delete '" + flipbook.name + "'? This cannot be undone.",
                "Delete", "Cancel");

            if (!confirmedDelete)
            {
                return;
            }

            if (ReferenceEquals(flipbook, LoadedFlipbook))
            {
                if (workingCopy != null)
                {
                    UnityEngine.Object.DestroyImmediate(workingCopy);
                    workingCopy = null;
                }

                LoadedFlipbook = null;
                LoadedArray = null;
                frames.SetFlipbook(null);
                preview.SetFlipbook(null);
                SetControlsEnabled(false);
                RefreshFlipbookLabel();
                RefreshInfoLabel();
                RefreshModeControls();
                depthWarningLabel.style.display = DisplayStyle.None;
            }

            FlipbookAssetUtility.TrashFlipbook(flipbook);
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

            if (workingCopy.IsImportedArray)
            {
                FilterMode newFilterMode = (FilterMode)changeEvent.newValue;
                WriteToImportedArrayImporter(arrayImporter => arrayImporter.filterMode = newFilterMode);
                return;
            }

            workingCopy.filterMode = (FilterMode)changeEvent.newValue;
            MarkUnsaved();
        }

        private void WriteToImportedArrayImporter(Action<TextureImporter> applySetting)
        {
            TextureImporter arrayImporter =
                AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(workingCopy.texture)) as TextureImporter;
            if (arrayImporter != null)
            {
                applySetting(arrayImporter);
                arrayImporter.SaveAndReimport();
            }

            RefreshModeControls();
        }

        private void OnMakeEditableClicked()
        {
            if (workingCopy == null || !workingCopy.IsImportedArray || !ConfirmDiscardIfUnsaved())
            {
                return;
            }

            FlipbookAsset editable = FlipbookAssetUtility.ExtractArrayLayersToEditableFlipbook(workingCopy.texture);
            if (editable == null)
            {
                return;
            }

            LoadFlipbook(editable);
            RescanProject();
        }

        private void OnWrapModeChanged(ChangeEvent<Enum> changeEvent)
        {
            if (workingCopy == null)
            {
                return;
            }

            if (workingCopy.IsImportedArray)
            {
                TextureWrapMode newWrapMode = (TextureWrapMode)changeEvent.newValue;
                WriteToImportedArrayImporter(arrayImporter => arrayImporter.wrapMode = newWrapMode);
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

            if (workingCopy.IsImportedArray)
            {
                bool newMipmapEnabled = changeEvent.newValue;
                WriteToImportedArrayImporter(arrayImporter => arrayImporter.mipmapEnabled = newMipmapEnabled);
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

            if (workingCopy.IsImportedArray)
            {
                bool newSrgbTexture = !changeEvent.newValue;
                WriteToImportedArrayImporter(arrayImporter => arrayImporter.sRGBTexture = newSrgbTexture);
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
                ? "T_Flipbook_Array"
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
                "Flipbook output", startName, "png",
                "Choose where the grid PNG is written; it imports as a Texture2DArray.", startDirectory);

            if (string.IsNullOrEmpty(chosenPath))
            {
                return;
            }

            workingCopy.outputPath = chosenPath;
            outputPathRow.Path = workingCopy.outputPath;
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
                workingCopy.outputPath = FlipbookBaker.DefaultOutputPathFor(AssetDatabase.GetAssetPath(LoadedFlipbook));
                outputPathRow.Path = workingCopy.outputPath;
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
                FlipbookAsset namesFlipbook = FlipbookAssetUtility.GetOrCreateFlipbookForArray(LoadedArray);
                if (namesFlipbook == null)
                {
                    EditorUtility.DisplayDialog(
                        "Save failed",
                        "Could not create a names asset beside '" + LoadedArray.name + "'.",
                        "OK");
                    return;
                }

                workingCopy.name = namesFlipbook.name;
                FlipbookAssetUtility.SaveWorkingCopy(workingCopy, namesFlipbook);
                LoadedFlipbook = namesFlipbook;
                LoadedArray = null;
                catalog.RescanProject();
                catalog.SetSelectedFlipbook(namesFlipbook);
            }
            else
            {
                FlipbookAssetUtility.SaveWorkingCopy(workingCopy, LoadedFlipbook);
            }

            HasUnsavedChanges = false;
            RefreshFlipbookLabel();
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

            string message = LoadedFlipbook != null
                ? "'" + LoadedFlipbook.name + "' has unsaved changes. Discard them?"
                : "The flipbook has unsaved changes.";

            return EditorUtility.DisplayDialog("Unsaved changes", message, "Discard", "Cancel");
        }

        private void MarkUnsaved()
        {
            HasUnsavedChanges = true;
            RefreshFlipbookLabel();
            RefreshModeControls();
        }

        private void RefreshFlipbookLabel()
        {
            string baseText = LoadedFlipbook != null
                ? LoadedFlipbook.name
                : LoadedArray != null ? LoadedArray.name : "Flipbook";
            flipbookLabel.text = HasUnsavedChanges ? baseText + "  ●" : baseText;
        }

        private void RefreshModeControls()
        {
            bool importedMode = workingCopy != null && workingCopy.IsImportedArray;

            bakeButton.SetEnabled(workingCopy != null && !importedMode);
            bakeButton.tooltip = workingCopy == null
                ? NothingSelectedTooltip
                : importedMode ? BakeButtonImportedTooltip : BakeButtonNormalTooltip;
            importSettingsSourceField.SetEnabled(!importedMode);
            importSettingsSourceField.tooltip = importedMode
                ? "An imported array takes its settings from its own importer."
                : "Bake copies this array's import settings (compression, filter, mips, sRGB). Empty uses the project defaults.";
            makeEditableButton.style.display = importedMode ? DisplayStyle.Flex : DisplayStyle.None;

            outputCard.style.display = importedMode ? DisplayStyle.None : DisplayStyle.Flex;
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

            bool saveEnabled = workingCopy != null
                && (LoadedArray == null || AnyFrameNameDiffersFromLayerIndex(workingCopy));
            saveButton.SetEnabled(saveEnabled);
            saveButton.tooltip = workingCopy == null
                ? NothingSelectedTooltip
                : importedMode && !saveEnabled
                ? SaveButtonImportedDisabledTooltip
                : SaveButtonNormalTooltip;
        }

        private static bool AnyFrameNameDiffersFromLayerIndex(FlipbookAsset flipbook)
        {
            for (int frameListPosition = 0; frameListPosition < flipbook.frames.Count; frameListPosition++)
            {
                FlipbookFrame frame = flipbook.frames[frameListPosition];
                if (frame.name != frame.index.ToString())
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshInfoLabel()
        {
            string infoText = BuildInfoText();
            infoLabel.text = infoText;
            infoLabel.style.display = infoText.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private string BuildInfoText()
        {
            if (workingCopy == null)
            {
                return string.Empty;
            }

            if (workingCopy.texture != null)
            {
                return workingCopy.layerSize.x + "x" + workingCopy.layerSize.y + " · " + workingCopy.frames.Count + " layers";
            }

            if (workingCopy.frames.Count > 0 && workingCopy.frames[0].source != null)
            {
                Texture2D firstSource = workingCopy.frames[0].source;
                return firstSource.width + "x" + firstSource.height + " · " + workingCopy.frames.Count + " layers";
            }

            return string.Empty;
        }

        private void SetControlsEnabled(bool enabled)
        {
            flipbookContent.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
            noFlipbookEmptyState.style.display = enabled ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
