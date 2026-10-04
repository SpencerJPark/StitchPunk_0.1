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

        private readonly Label cutoutNameLabel;
        private readonly VisualElement badgeRow;
        private readonly Label vertexCountBadge;
        private readonly Label areaBadge;
        private readonly Button saveMeshButton;
        private readonly CatalogSidebarElement sidebar;
        private readonly CutoutCatalogColumn cutoutCatalog;
        private readonly FlipbookCatalogColumn flipbookCatalog;
        private readonly ViewportFrameElement viewportFrame;
        private readonly CutoutCanvasElement canvas;
        private readonly CutoutShapeManipulator manipulator;
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

            sidebar = new CatalogSidebarElement { name = "cutouts-sidebar" };
            sidebar.AddMode(CutoutsModeName, "Cutouts", cutoutCatalog, cutoutCatalog.HeaderActions);
            sidebar.AddMode(FlipbooksModeName, "Flipbooks", flipbookCatalog, null);
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
            manipulator = new CutoutShapeManipulator(canvas);
            canvas.AddManipulator(manipulator);
            canvas.ShapeChanged += OnCanvasShapeChanged;
            manipulator.EditStarting += OnManipulatorEditStarting;
            manipulator.EditFinished += OnManipulatorEditFinished;
            manipulator.EdgeRejected += OnEdgeRejected;
            canvas.ModeChanged += OnCanvasModeChanged;
            BuildRail();
            viewportFrame.SetEmptyState("cutouts-empty-state", "No flipbook picked", "Pick a cutout, or a flipbook to start one, on the left.");
            centreColumn.Add(viewportFrame);

            VisualElement statusRow = ToolkitChrome.MakeStatusRow(out statusLabel, out VisualElement _, true);
            statusRow.name = "cutouts-status";
            centreColumn.Add(statusRow);

            inspector = new CutoutInspectorColumn();
            inspector.PixelsPerUnitChanged += OnPixelsPerUnitChanged;
            inspector.FrameStepRequested += OnFrameStepRequested;
            inspector.AllFramesGhostToggled += OnAllFramesGhostToggled;
            inspector.FitToArtRequested += OnFitToArtRequested;
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
                new CoverPaneSplitView("Cutouts.Sidebar", 0, 260f, TwoPaneSplitViewOrientation.Horizontal);
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
                SetStatus("That flipbook has no texture array.", ToolkitStatusTone.Warning);
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

            return source is Texture2DArray;
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

            Texture2DArray array = workingCopy.ResolveArray();
            if (array == null)
            {
                Unload();
                SetStatus("That flipbook has no texture array.", ToolkitStatusTone.Warning);
                return;
            }

            layerMasks = CutoutAlphaTracer.ReadLayerMasks(array, CutoutAlphaTracer.DefaultAlphaThreshold);
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
                workingCopy.pixelsPerUnit, workingCopy.FrameSize);
            canvas.SetFrameTexture(thumbnailCache.GetLayerThumbnail(workingCopy.ResolveArray(), frameIndex));
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
            layerMasks = null;
            unionMask = null;
            overhangs = new List<FrameOverhang>();
        }

        private string FrameName(int layerIndex)
        {
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
                workingCopy.ResolveArray().depth,
                FrameName(frameIndex),
                canvas.IsAllFramesGhostVisible);
        }

        private void SetStatus(string text, ToolkitStatusTone tone)
        {
            ToolkitChrome.SetStatus(statusLabel, text, tone);
        }

        private void UpdateStatus()
        {
            if (workingCopy == null)
            {
                SetStatus("Pick a flipbook on the left.", ToolkitStatusTone.Neutral);
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
            workingCopy.artPositionWorld = canvas.ArtPositionWorld;
            workingCopy.pixelsPerUnit = canvas.PixelsPerUnit;
            workingCopy.referenceRectWorld = canvas.ReferenceRectWorld;
            MarkEdited();
            canvas.IsOutlineInvalid = PolygonTriangulator.IsSelfIntersecting(workingCopy.outlinePixels);
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

            int frameCount = workingCopy.ResolveArray().depth;
            frameIndex = ((frameIndex + step) % frameCount + frameCount) % frameCount;
            canvas.SetFrameTexture(thumbnailCache.GetLayerThumbnail(workingCopy.ResolveArray(), frameIndex));
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

            List<Vector2> fittedOutline = CutoutAlphaTracer.FitOutline(
                unionMask, workingCopy.FrameSize, workingCopy.fitVertexBudget, workingCopy.fitPaddingPixels);
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

        private void SaveMesh()
        {
            if (workingCopy == null || workingCopy.ResolveArray() == null)
            {
                SetStatus("Nothing to save: pick a flipbook first.", ToolkitStatusTone.Warning);
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
