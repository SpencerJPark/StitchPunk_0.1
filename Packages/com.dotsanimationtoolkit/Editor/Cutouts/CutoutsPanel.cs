// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed class CutoutsPanel : VisualElement, IDisposable
    {
        private const string CutoutsModeName = "cutouts";
        private const string FlipbooksModeName = "flipbooks";
        private const string ImagesModeName = "images";

        private readonly Label cutoutNameLabel;
        private readonly VisualElement badgeRow;
        private readonly Label vertexCountBadge;
        private readonly Label areaBadge;
        private readonly Button saveMeshButton;
        private readonly CatalogSidebarElement sidebar;
        private readonly CutoutCatalogColumn cutoutCatalog;
        private readonly FlipbookCatalogColumn flipbookCatalog;
        private readonly ImageCatalogColumn imageCatalog;
        private readonly ViewportFrameElement viewportFrame;
        private readonly CutoutCanvasElement canvas;
        private readonly CutoutShapeManipulator manipulator;
        private readonly CutoutSheetOverlay sheetOverlay;
        private readonly CutoutInspectorColumn inspector;
        private readonly Label statusLabel;
        private readonly FlipbookLayerThumbnailCache thumbnailCache = new FlipbookLayerThumbnailCache();

        private VisualElement editModeSegmented;
        private ToolbarToggle editMeshToggle;
        private CutoutCanvasMode lastEditMode = CutoutCanvasMode.EditVertices;
        private CutoutAsset workingCopy;
        private List<bool[]> layerMasks;
        private bool[] unionMask;
        private Texture2D ghostTexture;
        private List<FrameOverhang> overhangs = new List<FrameOverhang>();
        private int frameIndex;
        private bool isSyncingCatalogSelection;
        private List<CutoutIsland> sheetIslands = new List<CutoutIsland>();
        private List<List<int>> sheetGroups = new List<List<int>>();
        private bool isDisposed;

        public CutoutAsset LoadedCutout { get; private set; }

        public bool HasUnsavedChanges { get; private set; }

        public CutoutsPanel()
        {
            name = "cutouts-panel";
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            VisualElement assetBar = ToolkitChrome.MakeAssetBar("cutouts-asset-bar");
            Add(assetBar);
            assetBar.Add(ToolkitChrome.MakeAssetBarLabel("Cutout"));
            cutoutNameLabel = new Label("None") { name = "cutouts-asset-name" };
            cutoutNameLabel.AddToClassList("cutouts-asset-name");
            assetBar.Add(cutoutNameLabel);
            assetBar.Add(ToolkitChrome.MakeAssetBarSpacer());

            vertexCountBadge = ToolkitChrome.MakeBadge(string.Empty, ToolkitStatusTone.Neutral);
            areaBadge = ToolkitChrome.MakeBadge(string.Empty, ToolkitStatusTone.Neutral);
            badgeRow = ToolkitChrome.MakeBadgeRow("cutouts-badge-row");
            badgeRow.Add(vertexCountBadge);
            badgeRow.Add(areaBadge);
            assetBar.Add(badgeRow);

            saveMeshButton = ToolkitChrome.MakePrimaryAction(
                SaveMesh,
                "d_SaveAs",
                "Build the mesh and write it to the output path. Saving again rewrites the same asset, so every MeshFilter using it updates.",
                "Save Mesh");
            saveMeshButton.name = "cutouts-save-mesh";
            assetBar.Add(saveMeshButton);

            cutoutCatalog = new CutoutCatalogColumn();
            cutoutCatalog.AssetSelected += OnCutoutCatalogSelected;
            cutoutCatalog.NewRequested += OnCutoutNewRequested;
            cutoutCatalog.RenameRequested += OnCutoutRenameRequested;
            cutoutCatalog.DeleteRequested += OnCutoutDeleteRequested;

            flipbookCatalog = new FlipbookCatalogColumn();
            flipbookCatalog.FlipbookSelected += LoadFlipbookSource;
            flipbookCatalog.ArraySelected += LoadFlipbookSource;
            flipbookCatalog.NewRequested += OnFlipbookNewRequested;

            imageCatalog = new ImageCatalogColumn();
            imageCatalog.ImagesActivated += OnImagesActivated;

            sidebar = new CatalogSidebarElement { name = "cutouts-sidebar" };
            sidebar.AddMode(CutoutsModeName, "Cutouts", cutoutCatalog, cutoutCatalog.HeaderActions);
            sidebar.AddMode(FlipbooksModeName, "Flipbooks", flipbookCatalog, null);
            sidebar.AddMode(ImagesModeName, "Images", imageCatalog, imageCatalog.HeaderActions);
            sidebar.SetMode(CutoutsModeName);

            VisualElement centreColumn = ToolkitChrome.MakeColumn("cutouts-centre-column");
            centreColumn.AddToClassList("toolkit-column--flush");
            centreColumn.style.flexGrow = 1f;
            centreColumn.Add(ToolkitChrome.MakePaneHeader("Canvas", out Label _, out VisualElement canvasHeaderActions));
            editModeSegmented = ToolkitChrome.MakeSegmentedControl(
                "cutouts-edit-mode",
                new List<string> { "Vertex", "Edge" },
                0,
                selectedIndex => canvas.Mode = selectedIndex == 1 ? CutoutCanvasMode.EditEdges : CutoutCanvasMode.EditVertices);
            editModeSegmented.style.display = DisplayStyle.None;
            canvasHeaderActions.Add(editModeSegmented);

            viewportFrame = new ViewportFrameElement();
            viewportFrame.style.flexGrow = 1f;
            canvas = new CutoutCanvasElement();
            canvas.style.position = Position.Absolute;
            canvas.style.left = 0f;
            canvas.style.top = 0f;
            canvas.style.right = 0f;
            canvas.style.bottom = 0f;
            viewportFrame.ViewportImage.Add(canvas);
            sheetOverlay = new CutoutSheetOverlay();
            sheetOverlay.style.position = Position.Absolute;
            sheetOverlay.style.left = 0f;
            sheetOverlay.style.top = 0f;
            sheetOverlay.style.right = 0f;
            sheetOverlay.style.bottom = 0f;
            sheetOverlay.style.display = DisplayStyle.None;
            sheetOverlay.SheetPixelToElement = sheetPixel => canvas.PixelToElement(sheetPixel);
            sheetOverlay.SelectionChanged += OnSheetSelectionChanged;
            viewportFrame.ViewportImage.Add(sheetOverlay);
            canvas.ViewChanged += sheetOverlay.Refresh;
            manipulator = new CutoutShapeManipulator(canvas);
            canvas.AddManipulator(manipulator);
            canvas.ShapeChanged += OnCanvasShapeChanged;
            manipulator.EditStarting += OnManipulatorEditStarting;
            manipulator.EditFinished += OnManipulatorEditFinished;
            manipulator.EdgeRejected += OnEdgeRejected;
            canvas.ModeChanged += OnCanvasModeChanged;
            BuildRail();
            viewportFrame.SetEmptyState("cutouts-empty-state", "Nothing picked", "Pick a cutout, a flipbook or an image on the left.");
            centreColumn.Add(viewportFrame);

            VisualElement statusRow = ToolkitChrome.MakeStatusRow(out statusLabel, out VisualElement _, true);
            statusRow.name = "cutouts-status";
            centreColumn.Add(statusRow);

            inspector = new CutoutInspectorColumn();
            inspector.PixelsPerUnitChanged += OnPixelsPerUnitChanged;
            inspector.FrameStepRequested += OnFrameStepRequested;
            inspector.FrameSetRequested += OnFrameSetRequested;
            canvas.FrameStepKeyPressed += OnFrameStepRequested;
            inspector.AllFramesGhostToggled += OnAllFramesGhostToggled;
            inspector.FitToArtRequested += OnFitToArtRequested;
            inspector.RotationRequested += OnRotationRequested;
            inspector.FindPiecesRequested += OnFindPiecesRequested;
            inspector.GroupAsFlipbookRequested += OnGroupAsFlipbookRequested;
            inspector.SplitGroupRequested += OnSplitGroupRequested;
            inspector.MergeIslandsRequested += OnMergeIslandsRequested;
            inspector.CutPiecesRequested += OnCutPiecesRequested;
            inspector.FitSettingsChanged += OnFitSettingsChanged;
            inspector.OriginPixelsChanged += OnOriginPixelsChanged;
            inspector.OriginPresetRequested += OnOriginPresetRequested;
            inspector.FacingChanged += OnFacingChanged;
            inspector.NormalModeChanged += OnNormalModeChanged;
            inspector.RoundnessChanged += OnRoundnessChanged;
            inspector.ReferenceImageChanged += OnReferenceImageChanged;
            inspector.ReferenceRectChanged += OnReferenceRectChanged;
            inspector.ReferenceOpacityChanged += OnReferenceOpacityChanged;
            inspector.OutputBrowseRequested += OnOutputBrowseRequested;
            inspector.OutputNameChanged += OnOutputNameChanged;
            inspector.LocationChanged += OnLocationChanged;
            inspector.ZeroLocationRequested += OnZeroLocationRequested;
            inspector.ClearEdgesRequested += OnClearEdgesRequested;

            CoverPaneSplitView inspectorSplit =
                new CoverPaneSplitView("Cutouts.Inspector", 1, 320f, TwoPaneSplitViewOrientation.Horizontal);
            inspectorSplit.style.flexGrow = 1f;
            inspectorSplit.Add(centreColumn);
            inspectorSplit.Add(inspector);

            CoverPaneSplitView sidebarSplit =
                // Three modes plus two header buttons need ~300pt or the buttons wrap under the segmented control;
                // a new key so the old 260pt remembered width does not stick.
                new CoverPaneSplitView("Cutouts.SidebarThreeModes", 0, 310f, TwoPaneSplitViewOrientation.Horizontal);
            sidebarSplit.style.flexGrow = 1f;
            sidebarSplit.Add(sidebar);
            sidebarSplit.Add(inspectorSplit);
            Add(sidebarSplit);

            Undo.undoRedoPerformed += OnUndoRedo;
            RefreshAll();
        }

        public void RescanProject()
        {
            cutoutCatalog.RescanProject();
            flipbookCatalog.RescanProject();
            imageCatalog.RescanProject();
        }

        private void OnImagesActivated(IReadOnlyList<Texture2D> activatedImages)
        {
            if (activatedImages != null && activatedImages.Count > 0)
            {
                LoadFlipbookSource(activatedImages[0]);
            }
        }

        public void LoadCutout(CutoutAsset cutout)
        {
            if (cutout == null)
            {
                Unload();
                return;
            }

            InstallWorkingCopy(UnityEngine.Object.Instantiate(cutout), cutout, false);
            SelectInCutoutCatalog(cutout);
        }

        public void LoadFlipbookSource(UnityEngine.Object flipbookOrArray)
        {
            if (flipbookOrArray == null || !SourceHasArray(flipbookOrArray))
            {
                Unload();
                SetStatus("That source has no texture.", ToolkitStatusTone.Warning);
                return;
            }

            CutoutAsset existingCutout = FindCutoutWrapping(flipbookOrArray);
            if (existingCutout != null)
            {
                LoadCutout(existingCutout);
                return;
            }

            CutoutAsset fresh = ScriptableObject.CreateInstance<CutoutAsset>();
            fresh.flipbook = flipbookOrArray;
            fresh.ResetToFullQuad();
            fresh.outputPath = CutoutMeshWriter.DefaultMeshPathFor(flipbookOrArray);
            InstallWorkingCopy(fresh, null, true);
            SelectInCutoutCatalog(null);
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            Undo.undoRedoPerformed -= OnUndoRedo;
            DestroyWorkingState();
            thumbnailCache.Dispose();
        }

        private void BuildRail()
        {
            viewportFrame.AddRailButton(
                "d_ViewToolZoom",
                "Frame the art (F). Wheel zooms at the cursor; middle-drag or Alt+drag pans.",
                "Frame",
                canvas.FrameAll);

            ToolbarToggle shapeToggle = viewportFrame.AddRailToggle(
                ToolkitGlyphs.Resolve(ToolkitGlyphId.Cutouts), "Show the shape and its vertices", "Shape", true);
            shapeToggle.SetValueWithoutNotify(true);
            shapeToggle.RegisterValueChangedCallback(changeEvent => canvas.IsShapeVisible = changeEvent.newValue);

            ToolbarToggle originToggle = viewportFrame.AddRailToggle("d_ToolHandlePivot", "Show the origin", "Origin");
            originToggle.SetValueWithoutNotify(true);
            originToggle.RegisterValueChangedCallback(changeEvent => canvas.IsOriginVisible = changeEvent.newValue);

            ToolbarToggle referenceToggle =
                viewportFrame.AddRailToggle("d_RawImage Icon", "Show the reference image", "Reference");
            referenceToggle.SetValueWithoutNotify(true);
            referenceToggle.RegisterValueChangedCallback(changeEvent => canvas.IsReferenceVisible = changeEvent.newValue);

            editMeshToggle = viewportFrame.AddRailToggle(
                "d_EditCollider",
                "Edit the mesh (Tab): Vertex mode moves, adds and deletes outline vertices; Edge mode draws the edges the triangles must follow.",
                "Edit");
            editMeshToggle.RegisterValueChangedCallback(
                changeEvent => canvas.Mode = changeEvent.newValue ? lastEditMode : CutoutCanvasMode.Object);
        }

        private static bool SourceHasArray(UnityEngine.Object source)
        {
            FlipbookAsset flipbook = source as FlipbookAsset;
            if (flipbook != null)
            {
                return flipbook.texture != null;
            }

            return source is Texture2DArray || source is Texture2D;
        }

        private static CutoutAsset FindCutoutWrapping(UnityEngine.Object source)
        {
            string[] guids = AssetDatabase.FindAssets("t:CutoutAsset");
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                CutoutAsset candidate = AssetDatabase.LoadAssetAtPath<CutoutAsset>(AssetDatabase.GUIDToAssetPath(guids[guidIndex]));
                if (candidate != null && candidate.flipbook == source)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void SelectInCutoutCatalog(CutoutAsset cutout)
        {
            isSyncingCatalogSelection = true;
            try
            {
                if (cutout != null)
                {
                    cutoutCatalog.SetSelectedCutout(cutout);
                }
                else
                {
                    cutoutCatalog.ClearSelection();
                }
            }
            finally
            {
                isSyncingCatalogSelection = false;
            }
        }

        private void InstallWorkingCopy(CutoutAsset newWorkingCopy, CutoutAsset loadedFromDisk, bool isUnsaved)
        {
            DestroyWorkingState();
            workingCopy = newWorkingCopy;
            workingCopy.hideFlags = HideFlags.HideAndDontSave;
            LoadedCutout = loadedFromDisk;
            HasUnsavedChanges = isUnsaved;

            if (!workingCopy.HasSource)
            {
                Unload();
                SetStatus("That source has no texture.", ToolkitStatusTone.Warning);
                return;
            }

            layerMasks = CutoutAlphaTracer.ReadSourceMasks(workingCopy.ResolveSourceTexture(), CutoutAlphaTracer.DefaultAlphaThreshold);
            unionMask = CutoutAlphaTracer.Union(layerMasks);
            BuildGhostTexture(workingCopy.FrameSize);

            if (workingCopy.outlinePixels.Count == 0)
            {
                workingCopy.ResetToFullQuad();
            }

            frameIndex = 0;
            PushWorkingCopyIntoCanvas();
            canvas.FrameAll();
            RecomputeOverhangs();
            RefreshAll();
        }

        private void BuildGhostTexture(Vector2Int frameSize)
        {
            ghostTexture = new Texture2D(frameSize.x, frameSize.y, TextureFormat.RGBA32, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point
            };
            Color32 setPixel = new Color32(255, 255, 255, 90);
            Color32 clearPixel = new Color32(0, 0, 0, 0);
            Color32[] pixels = new Color32[unionMask.Length];
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                pixels[pixelIndex] = unionMask[pixelIndex] ? setPixel : clearPixel;
            }

            ghostTexture.SetPixels32(pixels);
            ghostTexture.Apply();
        }

        private void PushWorkingCopyIntoCanvas()
        {
            canvas.SetCutout(
                workingCopy.outlinePixels, workingCopy.innerEdges, workingCopy.originPixels, workingCopy.artPositionWorld,
                workingCopy.pixelsPerUnit, workingCopy.FrameSize, workingCopy.rotationDegrees);
            canvas.SetFrameTexture(FrameTextureAt(frameIndex));
            canvas.SetAllFramesGhost(ghostTexture);
            canvas.SetReference(workingCopy.referenceImage, workingCopy.referenceRectWorld, workingCopy.referenceOpacity);
            canvas.IsOutlineInvalid = PolygonTriangulator.IsSelfIntersecting(workingCopy.outlinePixels);
        }

        private void Unload()
        {
            DestroyWorkingState();
            LoadedCutout = null;
            HasUnsavedChanges = false;
            canvas.ClearCutout();
            canvas.SetFrameTexture(null);
            canvas.SetAllFramesGhost(null);
            RefreshAll();
        }

        private void DestroyWorkingState()
        {
            if (workingCopy != null)
            {
                UnityEngine.Object.DestroyImmediate(workingCopy);
            }

            if (ghostTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(ghostTexture);
            }

            workingCopy = null;
            ghostTexture = null;
            ResetSheetState();
            layerMasks = null;
            unionMask = null;
            overhangs = new List<FrameOverhang>();
        }

        private Texture FrameTextureAt(int textureFrameIndex)
        {
            Texture2D image = workingCopy.ResolveImage();
            if (image != null)
            {
                return image;
            }

            return thumbnailCache.GetLayerThumbnail(workingCopy.ResolveArray(), textureFrameIndex);
        }

        private string FrameName(int layerIndex)
        {
            Texture2D image = workingCopy.ResolveImage();
            if (image != null)
            {
                return image.name;
            }

            FlipbookAsset flipbook = workingCopy.flipbook as FlipbookAsset;
            if (flipbook != null)
            {
                FlipbookFrame frame = flipbook.FindFrameByLayerIndex(layerIndex);
                if (frame != null && !string.IsNullOrEmpty(frame.name))
                {
                    return frame.name;
                }
            }

            return "Layer " + layerIndex;
        }

        private void RecomputeOverhangs()
        {
            overhangs = workingCopy != null && layerMasks != null
                ? CutoutAlphaTracer.FindOverhangs(layerMasks, workingCopy.FrameSize, workingCopy.outlinePixels)
                : new List<FrameOverhang>();
            overhangs.Sort((left, right) => right.overhangPixels.CompareTo(left.overhangPixels));
        }

        private void RefreshAll()
        {
            bool hasCutout = workingCopy != null;
            viewportFrame.ShowEmptyState(!hasCutout);
            saveMeshButton.SetEnabled(hasCutout);
            if (!hasCutout)
            {
                RefreshSheetUi();
            }
            badgeRow.style.display = hasCutout ? DisplayStyle.Flex : DisplayStyle.None;

            if (!hasCutout)
            {
                cutoutNameLabel.text = "None";
                inspector.ShowCutout(null, 0, 0, string.Empty, canvas.IsAllFramesGhostVisible);
                UpdateStatus();
                return;
            }

            if (LoadedCutout != null)
            {
                cutoutNameLabel.text = LoadedCutout.name;
            }
            else
            {
                UnityEngine.Object source = workingCopy.flipbook;
                cutoutNameLabel.text = (source != null ? source.name : "Cutout") + " (unsaved)";
            }

            vertexCountBadge.text = workingCopy.outlinePixels.Count + " verts";
            int areaPercent = Mathf.RoundToInt(
                CutoutMeshBuilder.AreaFractionOfQuad(workingCopy.outlinePixels, workingCopy.FrameSize) * 100f);
            areaBadge.text = areaPercent + "% of quad";

            RefreshInspector();
            UpdateStatus();
        }

        private void RefreshInspector()
        {
            if (workingCopy == null)
            {
                return;
            }

            inspector.ShowCutout(
                workingCopy,
                frameIndex,
                workingCopy.FrameCount,
                FrameName(frameIndex),
                canvas.IsAllFramesGhostVisible);
            inspector.ShowRotation(workingCopy.rotationDegrees);
            RefreshSheetUi();
        }

        private void SetStatus(string text, ToolkitStatusTone tone)
        {
            ToolkitChrome.SetStatus(statusLabel, text, tone);
        }

        private void UpdateStatus()
        {
            if (workingCopy == null)
            {
                SetStatus("Pick a flipbook or an image on the left.", ToolkitStatusTone.Neutral);
                return;
            }

            if (PolygonTriangulator.IsSelfIntersecting(workingCopy.outlinePixels))
            {
                SetStatus("The outline crosses itself: Save is blocked until no two edges cross.", ToolkitStatusTone.Error);
                return;
            }

            int skippedEdgeCount = canvas.SkippedInnerEdgeIndices.Count;
            if (skippedEdgeCount > 0)
            {
                SetStatus(
                    skippedEdgeCount + " drawn edges no longer fit the shape and are ignored.", ToolkitStatusTone.Warning);
                return;
            }

            if (overhangs.Count > 0)
            {
                FrameOverhang worst = overhangs[0];
                // Numbered from 1 like the inspector's frame stepper, so the two never disagree.
                string message = "Frame " + (worst.layerIndex + 1) + " (" + FrameName(worst.layerIndex) + ") pokes out by "
                    + Mathf.CeilToInt(worst.overhangPixels) + " px";
                if (overhangs.Count > 1)
                {
                    message += ", and " + (overhangs.Count - 1) + " more frames";
                }

                SetStatus(message, ToolkitStatusTone.Warning);
                return;
            }

            SetStatus("Every frame fits inside the shape.", ToolkitStatusTone.Ok);
        }

        private void MarkEdited()
        {
            HasUnsavedChanges = true;
        }

        private void RecordUndo()
        {
            if (workingCopy != null)
            {
                Undo.RecordObject(workingCopy, "Edit Cutout");
            }
        }

        private void OnUndoRedo()
        {
            if (workingCopy == null)
            {
                return;
            }

            PushWorkingCopyIntoCanvas();
            RecomputeOverhangs();
            MarkEdited();
            RefreshAll();
        }

        private void OnCanvasShapeChanged()
        {
            if (workingCopy == null)
            {
                return;
            }

            workingCopy.originPixels = canvas.OriginPixels;
            workingCopy.rotationDegrees = canvas.RotationDegrees;
            workingCopy.artPositionWorld = canvas.ArtPositionWorld;
            workingCopy.pixelsPerUnit = canvas.PixelsPerUnit;
            workingCopy.referenceRectWorld = canvas.ReferenceRectWorld;
            MarkEdited();
            canvas.IsOutlineInvalid = PolygonTriangulator.IsSelfIntersecting(workingCopy.outlinePixels);
            sheetOverlay.Refresh();
            RefreshAll();
        }

        private void OnManipulatorEditStarting()
        {
            RecordUndo();
        }

        private void OnManipulatorEditFinished()
        {
            RecomputeOverhangs();
            UpdateStatus();
        }

        private void OnCutoutCatalogSelected(CutoutAsset cutout)
        {
            if (isSyncingCatalogSelection || cutout == null)
            {
                return;
            }

            LoadCutout(cutout);
        }

        private void OnCutoutNewRequested()
        {
            sidebar.SetMode(FlipbooksModeName);
            SetStatus("Pick the flipbook to cut out.", ToolkitStatusTone.Neutral);
        }

        private void OnFlipbookNewRequested()
        {
            SetStatus("Create flipbooks on the Flipbooks tab.", ToolkitStatusTone.Neutral);
        }

        private void OnCutoutRenameRequested(CutoutAsset cutout, string newName)
        {
            if (cutout == null)
            {
                return;
            }

            AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(cutout), newName);
            cutoutCatalog.RescanProject();
            if (LoadedCutout == cutout)
            {
                RefreshAll();
            }
        }

        private void OnCutoutDeleteRequested(CutoutAsset cutout)
        {
            if (cutout == null)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete cutout", "Delete " + cutout.name + "? Its mesh asset is kept.", "Delete", "Cancel");
            if (!confirmed)
            {
                return;
            }

            bool wasLoaded = LoadedCutout == cutout;
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(cutout));
            if (wasLoaded)
            {
                Unload();
            }

            cutoutCatalog.RescanProject();
        }

        private void OnPixelsPerUnitChanged(float newPixelsPerUnit)
        {
            if (workingCopy == null || newPixelsPerUnit <= 0f)
            {
                return;
            }

            RecordUndo();
            workingCopy.pixelsPerUnit = newPixelsPerUnit;
            PushWorkingCopyIntoCanvas();
            MarkEdited();
            RefreshAll();
        }

        private void OnFrameStepRequested(int step)
        {
            if (workingCopy == null)
            {
                return;
            }

            int frameCount = workingCopy.FrameCount;
            if (frameCount <= 0)
            {
                return;
            }

            frameIndex = ((frameIndex + step) % frameCount + frameCount) % frameCount;
            canvas.SetFrameTexture(FrameTextureAt(frameIndex));
            RefreshInspector();
        }

        private void OnFrameSetRequested(int requestedFrameIndex)
        {
            if (workingCopy == null || workingCopy.FrameCount <= 0)
            {
                return;
            }

            frameIndex = Mathf.Clamp(requestedFrameIndex, 0, workingCopy.FrameCount - 1);
            canvas.SetFrameTexture(FrameTextureAt(frameIndex));
            RefreshInspector();
        }

        private void OnAllFramesGhostToggled(bool isVisible)
        {
            canvas.IsAllFramesGhostVisible = isVisible;
            RefreshInspector();
        }

        private void OnFitToArtRequested()
        {
            if (workingCopy == null)
            {
                return;
            }

            int safeVertexBudget = Mathf.Max(CutoutAsset.MinimumVertexCount, workingCopy.fitVertexBudget);
            List<Vector2> fittedOutline = CutoutAlphaTracer.FitOutline(
                unionMask, workingCopy.FrameSize, safeVertexBudget, workingCopy.fitPaddingPixels);
            if (fittedOutline.Count < CutoutAsset.MinimumVertexCount)
            {
                SetStatus("No art found to fit; every frame is transparent.", ToolkitStatusTone.Warning);
                return;
            }

            RecordUndo();
            workingCopy.outlinePixels.Clear();
            workingCopy.outlinePixels.AddRange(fittedOutline);
            workingCopy.innerEdges.Clear();
            canvas.SelectedVertexIndex = -1;
            canvas.SelectedInnerEdgeIndex = -1;
            canvas.Refresh();
            canvas.RaiseShapeChanged();
            RecomputeOverhangs();
            UpdateStatus();
        }

        private void OnFitSettingsChanged(int vertexBudget, float paddingPixels)
        {
            if (workingCopy == null)
            {
                return;
            }

            RecordUndo();
            workingCopy.fitVertexBudget = vertexBudget;
            workingCopy.fitPaddingPixels = paddingPixels;
            MarkEdited();
            RefreshInspector();
        }

        private void OnOriginPixelsChanged(Vector2 newOriginPixels)
        {
            if (workingCopy == null)
            {
                return;
            }

            RecordUndo();
            workingCopy.originPixels = newOriginPixels;
            canvas.SetOriginPixels(newOriginPixels);
            MarkEdited();
            RefreshAll();
        }

        private void OnOriginPresetRequested(Vector2 normalisedFramePoint)
        {
            if (workingCopy == null)
            {
                return;
            }

            Vector2Int frameSize = workingCopy.FrameSize;
            OnOriginPixelsChanged(new Vector2(normalisedFramePoint.x * frameSize.x, normalisedFramePoint.y * frameSize.y));
        }

        private void OnFacingChanged(CutoutFacing newFacing)
        {
            ApplyMeshSetting(() => workingCopy.facing = newFacing);
        }

        private void OnNormalModeChanged(CutoutNormalMode newNormalMode)
        {
            ApplyMeshSetting(() => workingCopy.normalMode = newNormalMode);
        }

        private void OnRoundnessChanged(float newRoundness)
        {
            ApplyMeshSetting(() => workingCopy.roundness = newRoundness);
        }

        private void ApplyMeshSetting(Action applyChange)
        {
            if (workingCopy == null)
            {
                return;
            }

            RecordUndo();
            applyChange();
            MarkEdited();
            RefreshInspector();
        }

        private void OnReferenceImageChanged(Texture2D newReferenceImage)
        {
            ApplyReferenceSetting(() => workingCopy.referenceImage = newReferenceImage);
        }

        private void OnReferenceRectChanged(Rect newReferenceRectWorld)
        {
            ApplyReferenceSetting(() => workingCopy.referenceRectWorld = newReferenceRectWorld);
        }

        private void OnReferenceOpacityChanged(float newOpacity)
        {
            ApplyReferenceSetting(() => workingCopy.referenceOpacity = newOpacity);
        }

        private void ApplyReferenceSetting(Action applyChange)
        {
            if (workingCopy == null)
            {
                return;
            }

            RecordUndo();
            applyChange();
            canvas.SetReference(workingCopy.referenceImage, workingCopy.referenceRectWorld, workingCopy.referenceOpacity);
            MarkEdited();
            RefreshInspector();
        }

        private void OnOutputBrowseRequested()
        {
            if (workingCopy == null)
            {
                return;
            }

            string currentFolder = string.IsNullOrEmpty(workingCopy.outputPath)
                ? string.Empty
                : Path.GetDirectoryName(workingCopy.outputPath).Replace('\\', '/');
            string startFolder = AssetDatabase.IsValidFolder(currentFolder) ? currentFolder : CutoutMeshWriter.RecallMeshFolder();
            string fileName = string.IsNullOrEmpty(workingCopy.outputPath)
                ? "Cutout"
                : Path.GetFileNameWithoutExtension(workingCopy.outputPath);
            string absoluteStartFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", startFolder));
            string chosenAbsoluteFolder = EditorUtility.OpenFolderPanel("Cutout mesh folder", absoluteStartFolder, string.Empty);
            if (string.IsNullOrEmpty(chosenAbsoluteFolder))
            {
                return;
            }

            if (!ClipSetSaveLocation.TryMakeProjectRelative(chosenAbsoluteFolder, Application.dataPath, out string chosenFolder))
            {
                SetStatus("Pick a folder inside this project's Assets folder.", ToolkitStatusTone.Warning);
                return;
            }

            RecordUndo();
            workingCopy.outputPath = chosenFolder + "/" + fileName + ".asset";
            CutoutMeshWriter.RememberMeshFolder(chosenFolder);
            MarkEdited();
            RefreshInspector();
        }

        private void OnOutputNameChanged(string newName)
        {
            if (workingCopy == null || string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            string folder = string.IsNullOrEmpty(workingCopy.outputPath)
                ? CutoutMeshWriter.RecallMeshFolder()
                : Path.GetDirectoryName(workingCopy.outputPath).Replace('\\', '/');
            RecordUndo();
            workingCopy.outputPath = folder + "/" + newName + ".asset";
            MarkEdited();
            RefreshInspector();
        }

        private void OnLocationChanged(Vector2 newLocationWorld)
        {
            ApplyArtLocation(newLocationWorld);
        }

        private void OnZeroLocationRequested()
        {
            ApplyArtLocation(Vector2.zero);
        }

        private void ApplyArtLocation(Vector2 newLocationWorld)
        {
            if (workingCopy == null)
            {
                return;
            }

            RecordUndo();
            workingCopy.artPositionWorld = newLocationWorld;
            canvas.SetArtPositionWorld(newLocationWorld);
            MarkEdited();
            RefreshAll();
        }

        private void OnClearEdgesRequested()
        {
            if (workingCopy == null)
            {
                return;
            }

            RecordUndo();
            workingCopy.innerEdges.Clear();
            canvas.SelectedInnerEdgeIndex = -1;
            canvas.Refresh();
            MarkEdited();
            RefreshAll();
        }

        private void OnEdgeRejected(string reason)
        {
            SetStatus(reason, ToolkitStatusTone.Warning);
        }

        private void OnCanvasModeChanged()
        {
            bool isEditing = canvas.Mode != CutoutCanvasMode.Object;
            if (isEditing)
            {
                lastEditMode = canvas.Mode;
            }

            editMeshToggle.SetValueWithoutNotify(isEditing);
            editModeSegmented.style.display = isEditing ? DisplayStyle.Flex : DisplayStyle.None;
            ToolkitChrome.SetSegmentedSelection(editModeSegmented, canvas.Mode == CutoutCanvasMode.EditEdges ? 1 : 0);
        }

        private bool IsSheetSource => workingCopy != null && workingCopy.ResolveImage() != null;

        private void ResetSheetState()
        {
            sheetIslands = new List<CutoutIsland>();
            sheetGroups = new List<List<int>>();
            if (sheetOverlay != null)
            {
                sheetOverlay.SetIslands(sheetIslands, new List<int>());
                sheetOverlay.style.display = DisplayStyle.None;
            }
        }

        private void RefreshSheetUi()
        {
            bool hasPieces = IsSheetSource && sheetIslands.Count > 0;
            sheetOverlay.style.display = hasPieces ? DisplayStyle.Flex : DisplayStyle.None;
            saveMeshButton.style.display = hasPieces ? DisplayStyle.None : DisplayStyle.Flex;
            inspector.ShowSheetState(IsSheetSource, sheetIslands.Count, sheetOverlay.Selection.Count);
        }

        private List<int> BuildGroupIndexPerIsland()
        {
            List<int> groupIndexPerIsland = new List<int>(sheetIslands.Count);
            for (int islandIndex = 0; islandIndex < sheetIslands.Count; islandIndex++)
            {
                groupIndexPerIsland.Add(-1);
            }

            for (int groupIndex = 0; groupIndex < sheetGroups.Count; groupIndex++)
            {
                foreach (int islandIndex in sheetGroups[groupIndex])
                {
                    groupIndexPerIsland[islandIndex] = groupIndex;
                }
            }

            return groupIndexPerIsland;
        }

        private void PushSheetStateIntoOverlay(IReadOnlyList<int> selection)
        {
            sheetOverlay.SetIslands(sheetIslands, BuildGroupIndexPerIsland());
            sheetOverlay.SetSelection(selection);
            RefreshSheetUi();
        }

        private void OnSheetSelectionChanged(IReadOnlyList<int> selection)
        {
            RefreshSheetUi();
        }

        private void OnRotationRequested(float rotationDegrees)
        {
            if (workingCopy == null)
            {
                return;
            }

            RecordUndo();
            workingCopy.rotationDegrees = rotationDegrees;
            PushWorkingCopyIntoCanvas();
            MarkEdited();
            RefreshAll();
        }

        private void OnFindPiecesRequested()
        {
            Texture2D sheet = IsSheetSource ? workingCopy.ResolveImage() : null;
            if (sheet == null)
            {
                return;
            }

            List<bool[]> sheetMasks = CutoutAlphaTracer.ReadSourceMasks(sheet, inspector.SheetAlphaThreshold);
            if (sheetMasks == null || sheetMasks.Count == 0)
            {
                SetStatus("The sheet could not be read.", ToolkitStatusTone.Warning);
                return;
            }

            sheetIslands = CutoutIslandFinder.FindIslands(
                sheetMasks[0], new Vector2Int(sheet.width, sheet.height),
                inspector.SheetJoinDistancePixels, inspector.SheetMinimumPieceSizePixels);
            List<CutoutSheetFlipbookSuggestion> suggestions = CutoutIslandFinder.SuggestSheetFlipbooks(
                sheetIslands, CutoutIslandFinder.DefaultSizeTolerancePixels);
            sheetGroups = new List<List<int>>();
            foreach (CutoutSheetFlipbookSuggestion suggestion in suggestions)
            {
                sheetGroups.Add(new List<int>(suggestion.islandIndices));
            }

            PushSheetStateIntoOverlay(new List<int>());
            SetStatus("Found " + sheetIslands.Count + " pieces, " + sheetGroups.Count + " suggested flipbooks", ToolkitStatusTone.Neutral);
        }

        private List<int> SelectedIslandIndicesAscending()
        {
            List<int> selected = new List<int>(sheetOverlay.Selection);
            selected.Sort();
            return selected;
        }

        private void RemoveIslandsFromGroups(List<int> islandIndices)
        {
            foreach (List<int> group in sheetGroups)
            {
                group.RemoveAll(memberIndex => islandIndices.Contains(memberIndex));
            }

            sheetGroups.RemoveAll(group => group.Count < 2);
        }

        private void OnGroupAsFlipbookRequested()
        {
            List<int> selected = SelectedIslandIndicesAscending();
            if (selected.Count < 2)
            {
                return;
            }

            RemoveIslandsFromGroups(selected);
            selected.Sort((left, right) => CompareIslandsInReadingOrder(sheetIslands[left], sheetIslands[right]));
            sheetGroups.Add(selected);
            PushSheetStateIntoOverlay(selected);
        }

        private static int CompareIslandsInReadingOrder(CutoutIsland left, CutoutIsland right)
        {
            float rowHeight = Mathf.Max(1f, Mathf.Max(left.boundsPixels.height, right.boundsPixels.height));
            float rowDifference = right.boundsPixels.center.y - left.boundsPixels.center.y;
            if (Mathf.Abs(rowDifference) > rowHeight * 0.5f)
            {
                return rowDifference > 0f ? 1 : -1;
            }

            return left.boundsPixels.center.x.CompareTo(right.boundsPixels.center.x);
        }

        private void OnSplitGroupRequested()
        {
            List<int> selected = SelectedIslandIndicesAscending();
            if (selected.Count == 0)
            {
                return;
            }

            RemoveIslandsFromGroups(selected);
            PushSheetStateIntoOverlay(selected);
        }

        private void OnMergeIslandsRequested()
        {
            List<int> selected = SelectedIslandIndicesAscending();
            if (selected.Count < 2)
            {
                return;
            }

            int keptIndex = selected[0];
            RectInt unionBounds = sheetIslands[keptIndex].boundsPixels;
            int opaqueTotal = 0;
            foreach (int islandIndex in selected)
            {
                RectInt bounds = sheetIslands[islandIndex].boundsPixels;
                int unionMinX = Mathf.Min(unionBounds.xMin, bounds.xMin);
                int unionMinY = Mathf.Min(unionBounds.yMin, bounds.yMin);
                int unionMaxX = Mathf.Max(unionBounds.xMax, bounds.xMax);
                int unionMaxY = Mathf.Max(unionBounds.yMax, bounds.yMax);
                unionBounds = new RectInt(unionMinX, unionMinY, unionMaxX - unionMinX, unionMaxY - unionMinY);
                opaqueTotal += sheetIslands[islandIndex].opaquePixelCount;
            }

            RemoveIslandsFromGroups(selected);

            int[] newIndexPerOldIndex = new int[sheetIslands.Count];
            List<CutoutIsland> mergedIslands = new List<CutoutIsland>(sheetIslands.Count);
            for (int oldIndex = 0; oldIndex < sheetIslands.Count; oldIndex++)
            {
                if (oldIndex == keptIndex)
                {
                    newIndexPerOldIndex[oldIndex] = mergedIslands.Count;
                    mergedIslands.Add(new CutoutIsland { boundsPixels = unionBounds, opaquePixelCount = opaqueTotal });
                }
                else if (selected.Contains(oldIndex))
                {
                    newIndexPerOldIndex[oldIndex] = -1;
                }
                else
                {
                    newIndexPerOldIndex[oldIndex] = mergedIslands.Count;
                    mergedIslands.Add(sheetIslands[oldIndex]);
                }
            }

            foreach (List<int> group in sheetGroups)
            {
                for (int memberSlot = 0; memberSlot < group.Count; memberSlot++)
                {
                    group[memberSlot] = newIndexPerOldIndex[group[memberSlot]];
                }
            }

            sheetIslands = mergedIslands;
            PushSheetStateIntoOverlay(new List<int> { newIndexPerOldIndex[keptIndex] });
        }

        private List<RectInt> BuildCellsForGroup(List<int> group)
        {
            int cellWidth = 0;
            int cellHeight = 0;
            foreach (int islandIndex in group)
            {
                cellWidth = Mathf.Max(cellWidth, sheetIslands[islandIndex].boundsPixels.width);
                cellHeight = Mathf.Max(cellHeight, sheetIslands[islandIndex].boundsPixels.height);
            }

            List<RectInt> cells = new List<RectInt>(group.Count);
            foreach (int islandIndex in group)
            {
                Vector2 centre = sheetIslands[islandIndex].boundsPixels.center;
                cells.Add(new RectInt(
                    Mathf.RoundToInt(centre.x - cellWidth * 0.5f),
                    Mathf.RoundToInt(centre.y - cellHeight * 0.5f),
                    cellWidth,
                    cellHeight));
            }

            return cells;
        }

        private void OnCutPiecesRequested()
        {
            Texture2D sheet = IsSheetSource ? workingCopy.ResolveImage() : null;
            if (sheet == null || sheetIslands.Count == 0)
            {
                return;
            }

            List<int> groupIndexPerIsland = BuildGroupIndexPerIsland();
            List<CutoutIsland> stills = new List<CutoutIsland>();
            for (int islandIndex = 0; islandIndex < sheetIslands.Count; islandIndex++)
            {
                if (groupIndexPerIsland[islandIndex] < 0)
                {
                    stills.Add(sheetIslands[islandIndex]);
                }
            }

            List<CutoutSheetFlipbookSuggestion> groups = new List<CutoutSheetFlipbookSuggestion>();
            foreach (List<int> group in sheetGroups)
            {
                groups.Add(new CutoutSheetFlipbookSuggestion
                {
                    islandIndices = new List<int>(group),
                    cells = BuildCellsForGroup(group)
                });
            }

            CutoutSheetBatchSettings settings = CutoutSheetBatchSettings.Default;
            settings.outputFolder = CutoutMeshWriter.RecallMeshFolder();
            settings.pixelsPerUnit = workingCopy.pixelsPerUnit;
            settings.originPreset = inspector.SheetOriginPreset;
            settings.writePrefabPerStill = inspector.SheetWritePrefabPerStill;

            bool written = CutoutSheetBatchWriter.TryWriteBatch(sheet, stills, groups, settings, out string report);
            if (written)
            {
                cutoutCatalog.RescanProject();
            }

            SetStatus(report, written ? ToolkitStatusTone.Ok : ToolkitStatusTone.Error);
        }

        private void SaveMesh()
        {
            if (workingCopy == null || !workingCopy.HasSource)
            {
                SetStatus("Nothing to save: pick a flipbook or an image first.", ToolkitStatusTone.Warning);
                return;
            }

            if (PolygonTriangulator.IsSelfIntersecting(workingCopy.outlinePixels))
            {
                UpdateStatus();
                return;
            }

            bool saved = CutoutMeshWriter.TrySave(workingCopy, LoadedCutout, out CutoutAsset savedCutout, out string failureReason);
            if (!saved)
            {
                SetStatus("Save failed: " + failureReason, ToolkitStatusTone.Error);
                return;
            }

            string savedMeshPath = workingCopy.outputPath;
            InstallWorkingCopy(UnityEngine.Object.Instantiate(savedCutout), savedCutout, false);
            cutoutCatalog.RescanProject();
            SelectInCutoutCatalog(savedCutout);
            SetStatus("Saved " + savedMeshPath, ToolkitStatusTone.Ok);
        }
    }
}
