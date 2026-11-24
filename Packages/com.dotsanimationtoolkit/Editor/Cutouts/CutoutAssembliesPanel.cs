// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    // The Assemblies tab: assemblies and cutouts on the left, the assembly canvas in the middle, its inspector on the right.
    public sealed class CutoutAssembliesPanel : VisualElement, IDisposable
    {
        private readonly CutoutAssemblyListElement assemblyList;
        private readonly CutoutCatalogColumn cutoutCatalog;
        private readonly ViewportFrameElement viewportFrame;
        private readonly CutoutAssemblyCanvasElement assemblyCanvas;
        private readonly CutoutAssemblyInspector assemblyInspector;
        private readonly Label assemblyNameLabel;
        private readonly Label statusLabel;
        private CutoutAssemblyAsset activeAssembly;

        public CutoutAssembliesPanel()
        {
            name = "assemblies-panel";
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            VisualElement assetBar = ToolkitChrome.MakeAssetBar("assemblies-asset-bar");
            Add(assetBar);
            assetBar.Add(ToolkitChrome.MakeAssetBarLabel("Assembly"));
            assemblyNameLabel = new Label("None") { name = "assemblies-asset-name" };
            assetBar.Add(assemblyNameLabel);
            assetBar.Add(ToolkitChrome.MakeAssetBarSpacer());

            assemblyList = new CutoutAssemblyListElement();
            assemblyList.AssemblyActivated += OnAssemblyActivated;
            assemblyList.AssemblyCreated += assembly => SetStatus("Created " + assembly.name, ToolkitStatusTone.Ok);
            assemblyList.AssemblyRenamed += OnAssemblyRenamed;
            assemblyList.AssemblyDeleted += OnAssemblyDeleted;

            cutoutCatalog = new CutoutCatalogColumn();
            cutoutCatalog.style.flexGrow = 1f;
            // Cutouts are made in the Cutouts tab; here the list is only something to drag from.
            VisualElement newCutoutButton = cutoutCatalog.HeaderActions.Q<VisualElement>("cutouts-new-button");
            if (newCutoutButton != null)
            {
                newCutoutButton.style.display = DisplayStyle.None;
            }

            VisualElement sidebar = ToolkitChrome.MakeColumn("assemblies-sidebar");
            sidebar.Add(assemblyList);
            VisualElement cutoutHeader = ToolkitChrome.MakePaneHeader("Cutouts", out Label _, out VisualElement cutoutHeaderActions);
            cutoutHeader.tooltip = "Drag a cutout onto the canvas to add it to the open assembly.";
            cutoutHeaderActions.Add(cutoutCatalog.HeaderActions);
            sidebar.Add(cutoutHeader);
            sidebar.Add(cutoutCatalog);

            VisualElement centreColumn = ToolkitChrome.MakeColumn("assemblies-centre-column");
            centreColumn.AddToClassList("toolkit-column--flush");
            centreColumn.style.flexGrow = 1f;
            centreColumn.Add(ToolkitChrome.MakePaneHeader("Canvas", out Label _, out VisualElement _));

            viewportFrame = new ViewportFrameElement();
            viewportFrame.style.flexGrow = 1f;
            assemblyCanvas = new CutoutAssemblyCanvasElement();
            assemblyCanvas.style.position = Position.Absolute;
            assemblyCanvas.style.left = 0f;
            assemblyCanvas.style.top = 0f;
            assemblyCanvas.style.right = 0f;
            assemblyCanvas.style.bottom = 0f;
            assemblyCanvas.RegisterCallback<DragUpdatedEvent>(OnCanvasDragUpdated);
            assemblyCanvas.RegisterCallback<DragPerformEvent>(OnCanvasDragPerformed);
            viewportFrame.ViewportImage.Add(assemblyCanvas);
            viewportFrame.AddRailButton(
                "d_ViewToolZoom",
                "Frame every piece (F). Wheel zooms at the cursor; middle-drag or Alt+drag pans.",
                "Frame",
                () => assemblyCanvas.FrameAll());
            viewportFrame.SetEmptyState(
                "assemblies-empty-state",
                "No assembly open",
                "Pick an assembly on the left, or press + to start one, then drag cutouts onto the canvas.");
            centreColumn.Add(viewportFrame);

            VisualElement statusRow = ToolkitChrome.MakeStatusRow(out statusLabel, out VisualElement _, true);
            statusRow.name = "assemblies-status";
            centreColumn.Add(statusRow);

            assemblyInspector = new CutoutAssemblyInspector();
            assemblyInspector.StatusReported += report => SetStatus(report, ToolkitStatusTone.Neutral);
            assemblyCanvas.PieceSelected += assemblyInspector.SetSelectedPiece;
            assemblyInspector.PieceSelected += assemblyCanvas.SetSelectedPiece;
            assemblyCanvas.AssemblyEdited += assemblyInspector.Refresh;
            assemblyInspector.AssemblyEdited += assemblyCanvas.MarkDirtyRepaint;

            CoverPaneSplitView inspectorSplit =
                new CoverPaneSplitView("Assemblies.Inspector", 1, 320f, TwoPaneSplitViewOrientation.Horizontal);
            inspectorSplit.style.flexGrow = 1f;
            inspectorSplit.Add(centreColumn);
            inspectorSplit.Add(assemblyInspector);

            CoverPaneSplitView sidebarSplit =
                new CoverPaneSplitView("Assemblies.Sidebar", 0, 280f, TwoPaneSplitViewOrientation.Horizontal);
            sidebarSplit.style.flexGrow = 1f;
            sidebarSplit.Add(sidebar);
            sidebarSplit.Add(inspectorSplit);
            Add(sidebarSplit);

            ShowAssembly(null);
        }

        public void RescanProject()
        {
            assemblyList.Rescan();
            cutoutCatalog.RescanProject();
        }

        public void Dispose()
        {
            assemblyCanvas.SetAssembly(null);
            assemblyInspector.SetAssembly(null);
        }

        private void ShowAssembly(CutoutAssemblyAsset assembly)
        {
            activeAssembly = assembly;
            assemblyCanvas.SetAssembly(assembly);
            assemblyInspector.SetAssembly(assembly);
            assemblyNameLabel.text = assembly != null ? assembly.name : "None";
            viewportFrame.ShowEmptyState(assembly == null);
            if (assembly != null)
            {
                assemblyCanvas.FrameAll();
            }
        }

        private void OnAssemblyActivated(CutoutAssemblyAsset assembly)
        {
            ShowAssembly(assembly);
            if (assembly != null && assembly.pieces.Count == 0)
            {
                SetStatus("Drag cutouts from the list onto the canvas", ToolkitStatusTone.Neutral);
            }
        }

        private void OnAssemblyRenamed(CutoutAssemblyAsset assembly, string newName)
        {
            if (assembly == activeAssembly)
            {
                assemblyNameLabel.text = newName;
            }

            SetStatus("Renamed to " + newName, ToolkitStatusTone.Ok);
        }

        private void OnAssemblyDeleted(CutoutAssemblyAsset deletedAssembly)
        {
            if (activeAssembly == deletedAssembly)
            {
                ShowAssembly(null);
            }

            SetStatus("Deleted assembly", ToolkitStatusTone.Ok);
        }

        private static CutoutAsset FindDraggedCutout()
        {
            UnityEngine.Object[] references = DragAndDrop.objectReferences;
            for (int referenceIndex = 0; referenceIndex < references.Length; referenceIndex++)
            {
                CutoutAsset cutout = references[referenceIndex] as CutoutAsset;
                if (cutout != null)
                {
                    return cutout;
                }
            }

            return null;
        }

        private void OnCanvasDragUpdated(DragUpdatedEvent dragEvent)
        {
            if (FindDraggedCutout() == null)
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            dragEvent.StopPropagation();
        }

        private void OnCanvasDragPerformed(DragPerformEvent dragEvent)
        {
            CutoutAsset draggedCutout = FindDraggedCutout();
            if (draggedCutout == null)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            dragEvent.StopPropagation();
            if (activeAssembly == null)
            {
                SetStatus("Pick or create an assembly first", ToolkitStatusTone.Warning);
                return;
            }

            Vector2 localPosition = assemblyCanvas.WorldToLocal(dragEvent.mousePosition);
            assemblyCanvas.AddPiece(draggedCutout, assemblyCanvas.ElementToWorld(localPosition));
        }

        private void SetStatus(string message, ToolkitStatusTone tone)
        {
            ToolkitChrome.SetStatus(statusLabel, message, tone);
        }
    }
}
