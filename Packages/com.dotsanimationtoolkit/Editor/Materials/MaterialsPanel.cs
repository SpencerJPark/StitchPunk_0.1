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
    /// <summary>The Materials tab: every material the shared rig's prefab uses, checked against the shader contract, with Create for a target.</summary>
    public sealed class MaterialsPanel : VisualElement, IDisposable
    {
        private readonly List<RigMaterialUsage> usages = new List<RigMaterialUsage>();
        private readonly List<RigTargetDefinition> dropdownTargets = new List<RigTargetDefinition>();
        private readonly ObjectField rigField;
        private readonly ObjectField clipSetField;
        private readonly DropdownField createTargetDropdown;
        private readonly Label resultLabel;
        private readonly MaterialCatalogColumn catalog;
        private readonly MaterialInspectorColumn inspector;
        private ActiveAssetSelection selection;

        public RigAsset BoundRig { get; private set; }
        public ClipSetAsset BoundClipSet { get; private set; }
        public Material LastCreatedMaterial { get; private set; }
        public Material SelectedMaterial { get; private set; }
        public string LastAssignedDescription { get; private set; }

        public IReadOnlyList<RigMaterialUsage> Usages
        {
            get { return usages; }
        }

        public MaterialsPanel()
        {
            style.flexGrow = 1f;

            VisualElement header = ToolkitChrome.MakeAssetBar("materials-asset-bar");

            header.Add(ToolkitChrome.MakeAssetBarLabel("Rig"));

            rigField = new ObjectField
            {
                objectType = typeof(RigAsset),
                allowSceneObjects = false,
                name = "materials-rig-field"
            };
            rigField.AddToClassList("toolkit-asset-bar__field");
            rigField.RegisterValueChangedCallback(changeEvent =>
            {
                RigAsset newRig = changeEvent.newValue as RigAsset;
                if (selection != null) { selection.SetRig(newRig); } else { SetRig(newRig); }
            });
            header.Add(rigField);

            header.Add(ToolkitChrome.MakeAssetBarLabel("Clip Set"));

            clipSetField = new ObjectField
            {
                objectType = typeof(ClipSetAsset),
                allowSceneObjects = false,
                name = "materials-clip-set-field"
            };
            clipSetField.AddToClassList("toolkit-asset-bar__field");
            clipSetField.RegisterValueChangedCallback(changeEvent =>
            {
                ClipSetAsset newClipSet = changeEvent.newValue as ClipSetAsset;
                if (selection != null) { selection.SetClipSet(newClipSet); } else { SetClipSet(newClipSet); }
            });
            header.Add(clipSetField);

            header.Add(ToolkitChrome.MakeAssetBarSpacer());

            // Its own bar label, not the field's inline one: Unity's label column left "Target" stranded
            // far from its dropdown.
            header.Add(ToolkitChrome.MakeAssetBarLabel("Target"));
            createTargetDropdown = new DropdownField(new List<string>(), 0);
            createTargetDropdown.name = "materials-create-target";
            header.Add(createTargetDropdown);

            Button createButton = ToolkitChrome.MakePrimaryAction(
                OnCreateClicked,
                "d_Toolbar Plus",
                "Create a material for this target from the package's shader, saved beside the rig's prefab, and assigned to the part's renderer in the prefab.",
                "Create and assign");
            createButton.name = "materials-create-button";
            header.Add(createButton);

            VisualElement statusRow = ToolkitChrome.MakeStatusRow(out resultLabel, out _, true);
            resultLabel.name = "materials-result";

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
            Add(split);
            Add(statusRow);
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
            rigField.SetValueWithoutNotify(rig);
            RebuildCreateTargetDropdown();
            Refresh();
        }

        public void SetClipSet(ClipSetAsset clipSet)
        {
            BoundClipSet = clipSet;
            clipSetField.SetValueWithoutNotify(clipSet);
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
                ToolkitChrome.SetStatus(resultLabel, failureMessage, ToolkitStatusTone.Error);
                return false;
            }

            // Capture before Refresh(), which can change SelectedMaterial as the list rebinds.
            Material previouslySelectedMaterial = SelectedMaterial;

            Material createdMaterial;
            bool created = MaterialTemplateUtility.TryCreateForTarget(BoundRig, target, out createdMaterial, out failureMessage);
            if (created)
            {
                LastCreatedMaterial = createdMaterial;

                bool assigned = MaterialTemplateUtility.TryAssignToTargetRenderer(BoundRig, target, createdMaterial, previouslySelectedMaterial, out string assignedDescription, out string assignFailureMessage);
                string createdFileName = Path.GetFileName(AssetDatabase.GetAssetPath(createdMaterial));
                if (assigned)
                {
                    ToolkitChrome.SetStatus(resultLabel, "Created " + createdFileName + " and assigned it to " + assignedDescription + ".", ToolkitStatusTone.Neutral);
                    LastAssignedDescription = assignedDescription;
                }
                else
                {
                    ToolkitChrome.SetStatus(resultLabel, "Created " + createdFileName + "; " + assignFailureMessage, ToolkitStatusTone.Neutral);
                    LastAssignedDescription = string.Empty;
                }

                EditorGUIUtility.PingObject(createdMaterial);
                Refresh();
                return true;
            }

            ToolkitChrome.SetStatus(resultLabel, failureMessage, ToolkitStatusTone.Error);
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
                ToolkitChrome.SetStatus(resultLabel, "Pick a target first.", ToolkitStatusTone.Error);
                return;
            }

            string failureMessage;
            CreateForTarget(dropdownTargets[selectedIndex], out failureMessage);
        }
    }
}
