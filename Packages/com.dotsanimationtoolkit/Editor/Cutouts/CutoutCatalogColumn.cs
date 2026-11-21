// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed class CutoutCatalogColumn : ToolkitCatalogColumn<CutoutAsset>
    {
        private const float DragStartDistance = 4f;

        private readonly VisualElement assemblyRowContainer;
        private CutoutAssemblyAsset selectedAssembly;
        private CutoutAsset pendingDragCutout;
        private Vector2 pendingDragOrigin;

        public event Action<CutoutAssemblyAsset> AssemblyActivated;
        public event Action<CutoutAssemblyAsset> AssemblyCreated;
        public event Action<CutoutAssemblyAsset, string> AssemblyRenamed;
        public event Action<CutoutAssemblyAsset> AssemblyDeleted;

        public CutoutCatalogColumn() : base(BuildOptions())
        {
            VisualElement assemblyHeader = new VisualElement();
            assemblyHeader.AddToClassList("toolkit-pane-header");
            Label assemblyTitle = new Label("Assemblies");
            assemblyTitle.AddToClassList("toolkit-pane-title");
            assemblyHeader.Add(assemblyTitle);
            VisualElement assemblyActions = ToolkitChrome.MakeIconSquareRun("cutouts-assembly-actions");
            assemblyActions.AddToClassList("toolkit-pane-actions");
            Button newAssemblyButton = ToolkitChrome.MakeIconSquare(
                CreateNewAssembly, "d_Toolbar Plus", "New assembly: a character built from several cutouts");
            newAssemblyButton.name = "cutouts-assembly-new-button";
            assemblyActions.Add(newAssemblyButton);
            assemblyHeader.Add(assemblyActions);
            Add(assemblyHeader);

            ScrollView assemblyScroll = new ScrollView();
            assemblyScroll.name = "cutouts-assembly-list";
            assemblyScroll.AddToClassList("toolkit-list-surface");
            assemblyScroll.style.maxHeight = 160f;
            assemblyScroll.style.flexShrink = 0f;
            assemblyScroll.style.marginTop = 4f;
            assemblyRowContainer = assemblyScroll.contentContainer;
            Add(assemblyScroll);

            RefreshRequested += RebuildAssemblyRows;
            RegisterCallback<PointerDownEvent>(OnCutoutRowPointerDown, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(OnCutoutRowPointerMove, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(OnCutoutRowPointerUp, TrickleDown.TrickleDown);
            RebuildAssemblyRows();
        }

        public void RescanProject()
        {
            Rescan();
        }

        public void SetSelectedAssembly(CutoutAssemblyAsset assembly)
        {
            selectedAssembly = assembly;
            RebuildAssemblyRows();
        }

        private static List<CutoutAssemblyAsset> ScanProjectAssemblies()
        {
            List<CutoutAssemblyAsset> assemblies = new List<CutoutAssemblyAsset>();
            string[] guids = AssetDatabase.FindAssets("t:CutoutAssemblyAsset");
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                CutoutAssemblyAsset assembly = AssetDatabase.LoadAssetAtPath<CutoutAssemblyAsset>(
                    AssetDatabase.GUIDToAssetPath(guids[guidIndex]));
                if (assembly != null)
                {
                    assemblies.Add(assembly);
                }
            }

            assemblies.Sort((CutoutAssemblyAsset left, CutoutAssemblyAsset right) =>
                string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
            return assemblies;
        }

        private void RebuildAssemblyRows()
        {
            assemblyRowContainer.Clear();
            List<CutoutAssemblyAsset> assemblies = ScanProjectAssemblies();
            if (assemblies.Count == 0)
            {
                Label emptyLabel = new Label("No assemblies yet");
                emptyLabel.AddToClassList("toolkit-list-row__meta");
                assemblyRowContainer.Add(emptyLabel);
                return;
            }

            for (int assemblyIndex = 0; assemblyIndex < assemblies.Count; assemblyIndex++)
            {
                assemblyRowContainer.Add(MakeAssemblyRow(assemblies[assemblyIndex]));
            }
        }

        private VisualElement MakeAssemblyRow(CutoutAssemblyAsset assembly)
        {
            VisualElement slot = ToolkitChrome.MakeListRowSlot("cutouts-assembly-row-box", out VisualElement row);
            slot.style.height = 22f;
            Label titleLabel = new Label(assembly.name);
            titleLabel.AddToClassList("toolkit-list-row__title");
            row.Add(titleLabel);
            row.tooltip = AssetDatabase.GetAssetPath(assembly);
            row.EnableInClassList("toolkit-list-row--selected", assembly == selectedAssembly);
            row.RegisterCallback<ClickEvent>((ClickEvent clickEvent) =>
            {
                if (clickEvent.clickCount == 2)
                {
                    InlineRenameEditing.Begin(
                        titleLabel,
                        assembly.name,
                        (string committedName) => RenameAssembly(assembly, committedName));
                    return;
                }

                selectedAssembly = assembly;
                RebuildAssemblyRows();
                AssemblyActivated?.Invoke(assembly);
            });
            row.AddManipulator(new ContextualMenuManipulator((ContextualMenuPopulateEvent populateEvent) =>
            {
                populateEvent.menu.AppendAction(
                    "Rename",
                    (DropdownMenuAction action) => InlineRenameEditing.Begin(
                        titleLabel,
                        assembly.name,
                        (string committedName) => RenameAssembly(assembly, committedName)),
                    DropdownMenuAction.AlwaysEnabled);
                populateEvent.menu.AppendAction(
                    "Delete",
                    (DropdownMenuAction action) => DeleteAssembly(assembly),
                    DropdownMenuAction.AlwaysEnabled);
            }));
            return slot;
        }

        private void CreateNewAssembly()
        {
            string folder = CutoutMeshWriter.RecallMeshFolder();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                folder = "Assets";
            }

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/NewCutoutAssembly.asset");
            CutoutAssemblyAsset assembly = ScriptableObject.CreateInstance<CutoutAssemblyAsset>();
            AssetDatabase.CreateAsset(assembly, assetPath);
            AssetDatabase.SaveAssets();
            selectedAssembly = assembly;
            RebuildAssemblyRows();
            AssemblyCreated?.Invoke(assembly);
            AssemblyActivated?.Invoke(assembly);
        }

        private void RenameAssembly(CutoutAssemblyAsset assembly, string committedName)
        {
            if (assembly == null || string.IsNullOrWhiteSpace(committedName) || committedName == assembly.name)
            {
                RebuildAssemblyRows();
                return;
            }

            string renameError = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(assembly), committedName);
            RebuildAssemblyRows();
            if (string.IsNullOrEmpty(renameError))
            {
                AssemblyRenamed?.Invoke(assembly, committedName);
            }
        }

        private void DeleteAssembly(CutoutAssemblyAsset assembly)
        {
            if (!EditorUtility.DisplayDialog(
                    "Delete assembly",
                    "Delete '" + assembly.name + "'? The cutouts it uses are not deleted.",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(assembly));
            if (selectedAssembly == assembly)
            {
                selectedAssembly = null;
            }

            RebuildAssemblyRows();
            AssemblyDeleted?.Invoke(assembly);
        }

        private static CutoutAsset FindCutoutUnderPointer(VisualElement target)
        {
            VisualElement current = target;
            while (current != null)
            {
                CutoutAsset cutout = current.userData as CutoutAsset;
                if (cutout != null)
                {
                    return cutout;
                }

                current = current.parent;
            }

            return null;
        }

        private void OnCutoutRowPointerDown(PointerDownEvent pointerDownEvent)
        {
            pendingDragCutout = pointerDownEvent.button == 0
                ? FindCutoutUnderPointer(pointerDownEvent.target as VisualElement)
                : null;
            pendingDragOrigin = pointerDownEvent.position;
        }

        private void OnCutoutRowPointerMove(PointerMoveEvent pointerMoveEvent)
        {
            if (pendingDragCutout == null || (pointerMoveEvent.pressedButtons & 1) == 0)
            {
                return;
            }

            if (Vector2.Distance(pendingDragOrigin, pointerMoveEvent.position) < DragStartDistance)
            {
                return;
            }

            CutoutAsset draggedCutout = pendingDragCutout;
            pendingDragCutout = null;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new UnityEngine.Object[] { draggedCutout };
            DragAndDrop.StartDrag(draggedCutout.name);
        }

        private void OnCutoutRowPointerUp(PointerUpEvent pointerUpEvent)
        {
            pendingDragCutout = null;
        }

        public void SetSelectedCutout(CutoutAsset cutout)
        {
            Select(cutout);
        }

        private static CatalogColumnOptions<CutoutAsset> BuildOptions()
        {
            return new CatalogColumnOptions<CutoutAsset>
            {
                elementName = "cutout-catalog-column",
                namePrefix = "cutouts",
                // No title: the Cutouts sidebar owns the header.
                title = string.Empty,
                newButtonIconName = "d_Toolbar Plus",
                newButtonTooltip = "Start a cutout: pick the flipbook or image to draw it over",
                refreshButtonIconName = "d_Refresh",
                refreshButtonTooltip = "Rescan the project for cutouts",
                emptyProjectTitle = "No cutouts yet",
                emptyProjectMessage = "A cutout is a flat mesh drawn tight around a flipbook's or an image's art.",
                emptyProjectActionText = "New Cutout",
                emptySearchMessage = "No cutouts match your search.",
                scan = ScanProjectCutouts,
                secondLine = DescribeCutout,
                tooltip = DescribeCutoutTooltip,
                allowRename = true,
                allowDelete = true,
            };
        }

        private static IReadOnlyList<CutoutAsset> ScanProjectCutouts()
        {
            List<CutoutAsset> cutouts = new List<CutoutAsset>();
            string[] guids = AssetDatabase.FindAssets("t:CutoutAsset");
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
                CutoutAsset cutout = AssetDatabase.LoadAssetAtPath<CutoutAsset>(assetPath);
                if (cutout != null)
                {
                    cutouts.Add(cutout);
                }
            }

            cutouts.Sort((CutoutAsset left, CutoutAsset right) =>
                string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
            return cutouts;
        }

        private static string DescribeCutout(CutoutAsset cutout)
        {
            string sourceName = cutout.flipbook != null ? cutout.flipbook.name : "no source";
            return cutout.outlinePixels.Count + " verts · " + sourceName;
        }

        private static string DescribeCutoutTooltip(CutoutAsset cutout)
        {
            string tooltipText = AssetDatabase.GetAssetPath(cutout);
            if (!string.IsNullOrEmpty(cutout.outputPath))
            {
                tooltipText += "\n" + cutout.outputPath;
            }

            return tooltipText;
        }
    }
}
