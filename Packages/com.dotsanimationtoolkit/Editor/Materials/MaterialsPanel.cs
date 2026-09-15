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
    /// <summary>The Materials tab: every material the shared rig's prefab uses, checked against the shader contract, with Create for a target.</summary>
    public sealed class MaterialsPanel : VisualElement, IDisposable
    {
        private readonly List<RigMaterialUsage> usages = new List<RigMaterialUsage>();
        private readonly List<RigTargetDefinition> dropdownTargets = new List<RigTargetDefinition>();
        private readonly Label rigNameLabel;
        private readonly DropdownField createTargetDropdown;
        private readonly Label resultLabel;
        private readonly MaterialCatalogColumn catalog;
        private readonly MaterialInspectorColumn inspector;
        private ActiveAssetSelection selection;

        public RigAsset BoundRig { get; private set; }
        public ClipSetAsset BoundClipSet { get; private set; }
        public Material LastCreatedMaterial { get; private set; }
        public Material SelectedMaterial { get; private set; }

        public IReadOnlyList<RigMaterialUsage> Usages
        {
            get { return usages; }
        }

        public MaterialsPanel()
        {
            style.flexGrow = 1f;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.flexWrap = Wrap.Wrap;

            Label rigLabel = new Label("Rig");
            header.Add(rigLabel);

            rigNameLabel = new Label("No rig selected. Pick one in the Rigs tab.");
            rigNameLabel.name = "materials-rig-name";
            header.Add(rigNameLabel);

            createTargetDropdown = new DropdownField("Target", new List<string>(), 0);
            createTargetDropdown.name = "materials-create-target";
            header.Add(createTargetDropdown);

            Button createButton = ToolkitIcons.MakeIconTextButton(
                OnCreateClicked,
                "d_Toolbar Plus",
                "Create a material for this target from the package's shader, saved beside the rig's prefab. It is not assigned to the renderer.",
                "Create");
            createButton.name = "materials-create-button";
            header.Add(createButton);

            resultLabel = new Label(string.Empty);
            resultLabel.name = "materials-result";
            resultLabel.AddToClassList("clip-editor__hint");

            catalog = new MaterialCatalogColumn();
            catalog.MaterialSelected += SelectMaterial;
            catalog.RefreshRequested += Refresh;
            catalog.NewRequested += OnCreateClicked;

            inspector = new MaterialInspectorColumn();

            CoverPaneSplitView split = new CoverPaneSplitView("Materials.Catalog", 0, 280f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            split.Add(catalog);
            split.Add(inspector);

            Add(header);
            Add(resultLabel);
            Add(split);
        }

        public void Bind(ActiveAssetSelection sharedSelection)
        {
            if (selection != null)
            {
                selection.RigChanged -= OnSharedRigChanged;
                selection.ClipSetChanged -= OnSharedClipSetChanged;
            }

            selection = sharedSelection;
            selection.RigChanged += OnSharedRigChanged;
            selection.ClipSetChanged += OnSharedClipSetChanged;
            SetClipSet(selection.ClipSet);
            SetRig(selection.Rig);
        }

        public void SetRig(RigAsset rig)
        {
            BoundRig = rig;
            RebuildCreateTargetDropdown();
            Refresh();
        }

        public void SetClipSet(ClipSetAsset clipSet)
        {
            BoundClipSet = clipSet;
            RebindInspectorForSelection();
        }

        public void Refresh()
        {
            usages.Clear();
            if (BoundRig != null)
            {
                usages.AddRange(RigMaterialResolver.Resolve(BoundRig));
            }

            catalog.SetUsages(usages);
            rigNameLabel.text = BoundRig != null ? BoundRig.name : "No rig selected. Pick one in the Rigs tab.";

            Material nextSelected = null;
            if (SelectedMaterial != null && IsMaterialInUsages(SelectedMaterial))
            {
                nextSelected = SelectedMaterial;
            }
            else if (usages.Count > 0)
            {
                nextSelected = usages[0].Material;
            }

            SelectedMaterial = nextSelected;
            catalog.SetSelectedMaterial(SelectedMaterial);
            RebindInspectorForSelection();
        }

        public void SelectMaterial(Material material)
        {
            SelectedMaterial = material;
            catalog.SetSelectedMaterial(material);
            RebindInspectorForSelection();
        }

        public bool CreateForTarget(RigTargetDefinition target, out string failureMessage)
        {
            if (BoundRig == null)
            {
                failureMessage = "Pick a rig first.";
                resultLabel.text = failureMessage;
                return false;
            }

            Material createdMaterial;
            bool created = MaterialTemplateUtility.TryCreateForTarget(BoundRig, target, out createdMaterial, out failureMessage);
            if (created)
            {
                LastCreatedMaterial = createdMaterial;
                resultLabel.text = "Created " + AssetDatabase.GetAssetPath(createdMaterial) + ". Assign it to the part's renderer in the Inspector.";
                EditorGUIUtility.PingObject(createdMaterial);
                Refresh();
                return true;
            }

            resultLabel.text = failureMessage;
            return false;
        }

        public void Dispose()
        {
            if (selection != null)
            {
                selection.RigChanged -= OnSharedRigChanged;
                selection.ClipSetChanged -= OnSharedClipSetChanged;
            }
        }

        private void OnSharedRigChanged(RigAsset rig)
        {
            SetRig(rig);
        }

        private void OnSharedClipSetChanged(ClipSetAsset clipSet)
        {
            SetClipSet(clipSet);
        }

        private void RebuildCreateTargetDropdown()
        {
            dropdownTargets.Clear();
            List<string> choices = new List<string>();
            if (BoundRig != null && BoundRig.targets != null)
            {
                Dictionary<string, int> displayNameCounts = new Dictionary<string, int>();
                foreach (RigTargetDefinition target in BoundRig.targets)
                {
                    if (target == null)
                    {
                        continue;
                    }

                    displayNameCounts.TryGetValue(target.displayName ?? string.Empty, out int existingCount);
                    displayNameCounts[target.displayName ?? string.Empty] = existingCount + 1;
                }

                foreach (RigTargetDefinition target in BoundRig.targets)
                {
                    if (target == null)
                    {
                        continue;
                    }

                    string choiceName = target.displayName ?? string.Empty;
                    if (displayNameCounts[choiceName] > 1)
                    {
                        choiceName = choiceName + " (" + target.sourceNodePath + ")";
                    }

                    dropdownTargets.Add(target);
                    choices.Add(choiceName);
                }
            }

            createTargetDropdown.choices = choices;
            createTargetDropdown.index = choices.Count > 0 ? 0 : -1;
        }

        private void RebindInspectorForSelection()
        {
            RigMaterialUsage matchedUsage = FindUsageForMaterial(SelectedMaterial);
            inspector.Bind(matchedUsage, BoundClipSet);
        }

        private RigMaterialUsage FindUsageForMaterial(Material material)
        {
            if (material == null)
            {
                return null;
            }

            foreach (RigMaterialUsage usage in usages)
            {
                if (usage.Material == material)
                {
                    return usage;
                }
            }

            return null;
        }

        private bool IsMaterialInUsages(Material material)
        {
            return FindUsageForMaterial(material) != null;
        }

        private void OnCreateClicked()
        {
            int selectedIndex = createTargetDropdown.index;
            if (selectedIndex < 0 || selectedIndex >= dropdownTargets.Count)
            {
                resultLabel.text = "Pick a target first.";
                return;
            }

            string failureMessage;
            CreateForTarget(dropdownTargets[selectedIndex], out failureMessage);
        }
    }
}
