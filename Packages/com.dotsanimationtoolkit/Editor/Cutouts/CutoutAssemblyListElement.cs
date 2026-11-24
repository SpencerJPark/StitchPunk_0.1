// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    // The Assemblies tab's list of assembly assets: New, click to open, double-click or right-click to rename or delete.
    public sealed class CutoutAssemblyListElement : VisualElement
    {
        private readonly VisualElement assemblyRowContainer;
        private CutoutAssemblyAsset selectedAssembly;

        public VisualElement HeaderActions { get; }

        public event Action<CutoutAssemblyAsset> AssemblyActivated;
        public event Action<CutoutAssemblyAsset> AssemblyCreated;
        public event Action<CutoutAssemblyAsset, string> AssemblyRenamed;
        public event Action<CutoutAssemblyAsset> AssemblyDeleted;

        public CutoutAssemblyListElement()
        {
            name = "assemblies-list";
            style.flexGrow = 1f;

            // The sidebar owns the header, so the + button is handed over rather than drawn here.
            HeaderActions = ToolkitChrome.MakeIconSquareRun("assemblies-actions");
            HeaderActions.AddToClassList("toolkit-pane-actions");
            Button newAssemblyButton = ToolkitChrome.MakeIconSquare(
                CreateNewAssembly, "d_Toolbar Plus", "New assembly: a character built from several cutouts");
            newAssemblyButton.name = "assemblies-new-button";
            HeaderActions.Add(newAssemblyButton);

            ScrollView assemblyScroll = new ScrollView();
            assemblyScroll.name = "assemblies-list-rows";
            assemblyScroll.AddToClassList("toolkit-list-surface");
            assemblyScroll.style.flexGrow = 1f;
            assemblyRowContainer = assemblyScroll.contentContainer;
            Add(assemblyScroll);

            RebuildAssemblyRows();
        }

        public void Rescan()
        {
            RebuildAssemblyRows();
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
            VisualElement slot = ToolkitChrome.MakeListRowSlot("assemblies-row-box", out VisualElement row);
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
            assembly.outputFolder = folder;
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
    }
}
