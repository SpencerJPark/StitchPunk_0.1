// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>What the Materials tab's inspector shows: a material, or the cutout or mesh that still needs one.</summary>
    public sealed class MaterialInspectorSubject
    {
        public Material Material;
        public CutoutAsset Cutout;
        public Mesh Mesh;
        public RigMaterialUsage RigUsage;
    }

    /// <summary>The Materials tab's detail column: motion, shader, rig usage, contract properties and flipbook findings.</summary>
    public sealed class MaterialInspectorColumn : VisualElement
    {
        public MaterialInspectorSubject BoundSubject { get; private set; }
        public RigAsset BoundRig { get; private set; }
        public ClipSetAsset BoundClipSet { get; private set; }

        public event Action<Material> MaterialCreated;
        public event Action<Material> MaterialChanged;
        public event Action<string, ToolkitStatusTone> StatusReported;

        private RigMaterialUsage BoundUsage
        {
            get { return BoundSubject != null ? BoundSubject.RigUsage : null; }
        }

        private readonly Label titleLabel;
        private readonly VisualElement emptyState;
        private readonly Button selectButton;
        private readonly ScrollView bodyScrollView;

        public MaterialInspectorColumn()
        {
            style.flexGrow = 1f;
            AddToClassList("toolkit-column");

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            titleLabel = new Label("Material") { name = "material-inspector-title" };
            titleLabel.AddToClassList("toolkit-pane-title");
            header.Add(titleLabel);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("toolkit-pane-actions");
            selectButton = ToolkitChrome.MakeGhostAction(
                SelectBoundMaterial,
                ToolkitIcons.Frame,
                "Select this material in the Project and Inspector",
                "Select");
            selectButton.name = "material-inspector-select";
            actions.Add(selectButton);
            header.Add(actions);
            Add(header);

            emptyState = ToolkitChrome.MakeEmptyState(
                "material-inspector-hint",
                "No material selected",
                "Pick a material on the left to see its shader, users and contract.",
                null,
                null);
            Add(emptyState);

            bodyScrollView = new ScrollView();
            bodyScrollView.style.flexGrow = 1f;
            Add(bodyScrollView);
        }

        public void Bind(MaterialInspectorSubject subject, RigAsset rig, ClipSetAsset clipSet)
        {
            BoundSubject = subject;
            BoundRig = rig;
            BoundClipSet = clipSet;
            RebuildBody();
        }

        private void SelectBoundMaterial()
        {
            if (BoundSubject == null || BoundSubject.Material == null)
            {
                return;
            }

            Selection.activeObject = BoundSubject.Material;
            EditorGUIUtility.PingObject(BoundSubject.Material);
        }

        private void ReportStatus(string message, ToolkitStatusTone tone)
        {
            StatusReported?.Invoke(message, tone);
        }

        private void RebuildBody()
        {
            bodyScrollView.Clear();

            Material material = BoundSubject != null ? BoundSubject.Material : null;
            bool hasCutoutOrMesh = BoundSubject != null && (BoundSubject.Cutout != null || BoundSubject.Mesh != null);

            if (material == null && !hasCutoutOrMesh)
            {
                titleLabel.text = "Material";
                emptyState.style.display = DisplayStyle.Flex;
                bodyScrollView.style.display = DisplayStyle.None;
                selectButton.SetEnabled(false);
                return;
            }

            emptyState.style.display = DisplayStyle.None;
            bodyScrollView.style.display = DisplayStyle.Flex;
            selectButton.SetEnabled(material != null);

            if (material == null)
            {
                titleLabel.text = BoundSubject.Cutout != null ? BoundSubject.Cutout.name : BoundSubject.Mesh.name;
                AddNoMaterialYetCard();
                return;
            }

            titleLabel.text = material.name;
            AddMotionCard(material);

            VisualElement shaderCardBody;
            VisualElement shaderCardHeaderActions;
            VisualElement shaderCard = ToolkitChrome.MakeCard(
                "material-inspector-shader-card",
                "Shader",
                out shaderCardBody,
                out shaderCardHeaderActions);
            shaderCard.style.flexShrink = 0f;

            Label shaderValueLabel = new Label(material.shader != null ? material.shader.name : "no shader") { name = "material-inspector-shader" };
            shaderCardBody.Add(ToolkitChrome.MakePropertyRow("Shader", shaderValueLabel, null));

            bool isInstancingEnabled = material.enableInstancing;
            Label instancingBadge = ToolkitChrome.MakeBadge(
                "GPU instancing",
                isInstancingEnabled ? ToolkitStatusTone.Ok : ToolkitStatusTone.Error);
            instancingBadge.name = "material-inspector-instancing";
            if (!isInstancingEnabled)
            {
                instancingBadge.tooltip = "Entities Graphics needs it on";
            }

            shaderCardBody.Add(ToolkitChrome.MakePropertyRow("Instancing", instancingBadge, "Whether GPU instancing is on for this material."));

            bool isToolkitShader = MaterialFeatureResolver.IsToolkitShader(material.shader);
            Label shaderKindBadge = ToolkitChrome.MakeBadge(
                isToolkitShader ? "Toolkit" : "Custom",
                isToolkitShader ? ToolkitStatusTone.Ok : ToolkitStatusTone.Neutral);
            shaderKindBadge.name = "material-inspector-shader-kind";
            shaderCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Kind",
                shaderKindBadge,
                "Toolkit shaders are the ones this package ships; anything else is a custom shader."));

            if (!isToolkitShader)
            {
                AddCustomShaderFindings(shaderCardBody, material);
            }

            bodyScrollView.Add(shaderCard);

            AddPrefabCard(material);
            AddUseInRigAction(material);

            if (BoundUsage == null)
            {
                return;
            }

            VisualElement usageCardBody;
            VisualElement usageCardHeaderActions;
            VisualElement usageCard = ToolkitChrome.MakeCard(
                "material-inspector-usage-card",
                "Usage",
                out usageCardBody,
                out usageCardHeaderActions);
            usageCard.style.flexShrink = 0f;

            List<RigTargetDefinition> targets = BoundUsage.Targets;
            string usedByText = targets != null && targets.Count > 0
                ? string.Join(", ", targets.Select(target => target.displayName))
                : "no rig target";
            Label usedByValueLabel = new Label(usedByText) { name = "material-inspector-used-by" };
            usageCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Used by",
                usedByValueLabel,
                "Rig targets whose material this is. With none, no material contract applies."));

            List<TargetKind> distinctKindsInEnumOrder = targets != null && targets.Count > 0
                ? targets.Select(target => target.kind).Distinct().OrderBy(kind => (int)kind).ToList()
                : new List<TargetKind>();

            string kindText = distinctKindsInEnumOrder.Count > 0
                ? string.Join(", ", distinctKindsInEnumOrder.Select(DisplayNameForTargetKind))
                : "—";
            Label kindValueLabel = new Label(kindText) { name = "material-inspector-kind" };
            usageCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Kind",
                kindValueLabel,
                distinctKindsInEnumOrder.Count == 0 ? "No rig target uses this material." : null));

            if (distinctKindsInEnumOrder.Count == 0 && BoundUsage.UnmappedNodePaths != null && BoundUsage.UnmappedNodePaths.Count > 0)
            {
                Label unmappedValueLabel = new Label(string.Join(", ", BoundUsage.UnmappedNodePaths));
                usageCardBody.Add(ToolkitChrome.MakePropertyRow("Unmapped", unmappedValueLabel, "Nodes using this material that no rig target maps."));
            }

            bodyScrollView.Add(usageCard);

            if (distinctKindsInEnumOrder.Count > 0)
            {
                VisualElement contractCardBody;
                VisualElement contractCardHeaderActions;
                VisualElement contractCard = ToolkitChrome.MakeCard(
                    "material-inspector-contract-card",
                    "Contract",
                    out contractCardBody,
                    out contractCardHeaderActions);
                contractCard.style.flexShrink = 0f;

                bool hasSeveralKinds = distinctKindsInEnumOrder.Count > 1;
                int listedPropertyCount = 0;
                foreach (TargetKind kind in distinctKindsInEnumOrder)
                {
                    // With one kind the card's Kind row already names it, and a header here would
                    // just repeat the word. With several, each property group needs to say which
                    // kind it belongs to, because the rows below never name it themselves.
                    if (hasSeveralKinds)
                    {
                        Label kindSectionLabel = new Label(DisplayNameForTargetKind(kind));
                        kindSectionLabel.AddToClassList("toolkit-heading");
                        contractCardBody.Add(kindSectionLabel);
                    }

                    List<ContractPropertyStatus> propertyStatuses = new List<ContractPropertyStatus>();
                    MaterialContractValidation.EvaluateProperties(material, kind, propertyStatuses);
                    foreach (ContractPropertyStatus propertyStatus in propertyStatuses)
                    {
                        if (propertyStatus.state != ContractPropertyState.NotNeeded)
                        {
                            listedPropertyCount++;
                        }

                        AddPropertyRow(contractCardBody, propertyStatus, kind);
                    }
                }

                if (listedPropertyCount == 0)
                {
                    VisualElement noPropertiesRow = ToolkitChrome.MakeBadgeRow("material-inspector-contract-ok");
                    noPropertiesRow.Add(ToolkitChrome.MakeBadge("No per-frame properties", ToolkitStatusTone.Ok));
                    Label needsHint = ToolkitChrome.MakeHint("Only Flipbook (_ImageIndex) and VAT (_VatFrameA/B, _VatBlend) parts need any.");
                    noPropertiesRow.Add(needsHint);
                    contractCardBody.Add(noPropertiesRow);
                }

                bodyScrollView.Add(contractCard);
            }

            List<ValidationMessage> flipbookWarnings = new List<ValidationMessage>();
            RigMaterialResolver.CollectFlipbookBindingWarnings(BoundUsage, BoundClipSet, flipbookWarnings);
            foreach (ValidationMessage warning in flipbookWarnings)
            {
                Label warningRow = new Label("● " + warning.text) { name = "material-inspector-flipbook-warning" };
                warningRow.AddToClassList("toolkit-text--warning");
                bodyScrollView.Add(warningRow);
            }
        }

        private void AddNoMaterialYetCard()
        {
            string subjectNoun = BoundSubject.Cutout != null ? "cutout" : "mesh";
            VisualElement createCard = ToolkitChrome.MakeEmptyState(
                "material-inspector-create",
                "No material yet",
                "Make the look this " + subjectNoun + " is shown with.",
                "Create material",
                CreateMaterialForSubject);
            createCard.style.flexShrink = 0f;
            bodyScrollView.Add(createCard);
        }

        private void CreateMaterialForSubject()
        {
            CutoutAsset cutout = BoundSubject.Cutout;
            string materialPath;
            MaterialFeature features;
            Texture sourceTexture;

            if (cutout != null)
            {
                string outputMeshPath = cutout.outputMesh != null ? AssetDatabase.GetAssetPath(cutout.outputMesh) : string.Empty;
                if (!string.IsNullOrEmpty(outputMeshPath))
                {
                    materialPath = MaterialFeatureResolver.ComputeDefaultMaterialPath(cutout.outputMesh);
                }
                else
                {
                    string cutoutFolder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(cutout)).Replace('\\', '/');
                    materialPath = cutoutFolder + "/M_" + cutout.name + ".mat";
                }

                features = MaterialFeatureResolver.FeaturesForCutout(cutout);
                sourceTexture = cutout.ResolveSourceTexture();
            }
            else
            {
                materialPath = MaterialFeatureResolver.ComputeDefaultMaterialPath(BoundSubject.Mesh);
                features = MaterialFeature.None;
                sourceTexture = null;
            }

            if (string.IsNullOrEmpty(materialPath))
            {
                ReportStatus("Save the mesh as an asset first.", ToolkitStatusTone.Error);
                return;
            }

            Material createdMaterial;
            string failureMessage;
            if (!MaterialAuthoringUtility.TryCreateMaterial(materialPath, features, sourceTexture, out createdMaterial, out failureMessage))
            {
                ReportStatus(failureMessage, ToolkitStatusTone.Error);
                return;
            }

            if (cutout != null)
            {
                // A cutout stores its material; a bare mesh is matched by the M_<Mesh>.mat name.
                cutout.material = createdMaterial;
                EditorUtility.SetDirty(cutout);
                AssetDatabase.SaveAssetIfDirty(cutout);
            }

            ReportStatus("Created " + System.IO.Path.GetFileName(materialPath) + ".", ToolkitStatusTone.Neutral);
            MaterialCreated?.Invoke(createdMaterial);
        }

        private void AddMotionCard(Material material)
        {
            VisualElement motionCardBody;
            VisualElement motionCardHeaderActions;
            VisualElement motionCard = ToolkitChrome.MakeCard(
                "material-inspector-motion-card",
                "Motion",
                out motionCardBody,
                out motionCardHeaderActions);
            motionCard.style.flexShrink = 0f;

            MaterialFeature currentFeatures = MaterialFeatureResolver.ReadFeatures(material);
            Toggle flipbookToggle = new Toggle { name = "material-inspector-flipbook-toggle" };
            flipbookToggle.SetValueWithoutNotify((currentFeatures & MaterialFeature.Flipbook) != 0);
            flipbookToggle.SetEnabled(MaterialFeatureResolver.IsToolkitShader(material.shader));
            flipbookToggle.RegisterValueChangedCallback(changeEvent => OnFlipbookToggled(material, changeEvent.newValue));
            motionCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Flipbook",
                flipbookToggle,
                "Swap the material between the static and flipbook toolkit shaders. Only toolkit shaders can be switched."));

            string vatText;
            bool isVat = (currentFeatures & MaterialFeature.Vat) != 0;
            Texture boneTexture = isVat ? material.GetTexture(MaterialFeatureResolver.VatBoneTexturePropertyName) : null;
            if (!isVat)
            {
                vatText = "Not baked: VAT Bake turns this on";
            }
            else if (boneTexture == null)
            {
                vatText = "VAT, not baked yet: run VAT Bake";
            }
            else
            {
                UnityEngine.Object bakeAsset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(boneTexture));
                vatText = "Baked by " + (bakeAsset != null ? bakeAsset.name : boneTexture.name);
            }

            Label vatStatusLabel = new Label(vatText) { name = "material-inspector-vat-status" };
            if (isVat && boneTexture == null)
            {
                vatStatusLabel.AddToClassList("toolkit-text--warning");
            }

            motionCardBody.Add(ToolkitChrome.MakePropertyRow("VAT", vatStatusLabel, "VAT is set by the VAT Bake tab, never switched by hand."));
            bodyScrollView.Add(motionCard);
        }

        private void OnFlipbookToggled(Material material, bool isFlipbookOn)
        {
            MaterialFeature newFeatures = MaterialFeatureResolver.ReadFeatures(material) & MaterialFeature.Vat;
            if (isFlipbookOn)
            {
                newFeatures |= MaterialFeature.Flipbook;
            }

            string resultMessage;
            bool succeeded = MaterialAuthoringUtility.TrySetToolkitFeatures(material, newFeatures, out resultMessage);
            ReportStatus(resultMessage, succeeded ? ToolkitStatusTone.Neutral : ToolkitStatusTone.Error);
            if (succeeded)
            {
                MaterialChanged?.Invoke(material);
            }

            RebuildBody();
        }

        private void AddCustomShaderFindings(VisualElement shaderCardBody, Material material)
        {
            MaterialFeature features = MaterialFeatureResolver.ReadFeatures(material);
            List<string> providedNames = new List<string>();
            if ((features & MaterialFeature.Flipbook) != 0)
            {
                providedNames.Add("Flipbook");
            }

            if ((features & MaterialFeature.Vat) != 0)
            {
                providedNames.Add("VAT");
            }

            Label providesLabel = new Label(providedNames.Count > 0 ? string.Join(", ", providedNames) : "Static")
            {
                name = "material-inspector-provides"
            };
            shaderCardBody.Add(ToolkitChrome.MakePropertyRow("Provides", providesLabel, "Features read from this shader's properties."));

            List<TargetKind> contractKinds = new List<TargetKind>();
            if ((features & MaterialFeature.Flipbook) != 0)
            {
                contractKinds.Add(TargetKind.FlipbookPlane);
            }

            if ((features & MaterialFeature.Vat) != 0)
            {
                contractKinds.Add(TargetKind.VatMesh);
            }

            bool hasMissingProperty = false;
            foreach (TargetKind kind in contractKinds)
            {
                List<ContractPropertyStatus> propertyStatuses = new List<ContractPropertyStatus>();
                MaterialContractValidation.EvaluateProperties(material, kind, propertyStatuses);
                foreach (ContractPropertyStatus propertyStatus in propertyStatuses)
                {
                    if (propertyStatus.state != ContractPropertyState.Missing)
                    {
                        continue;
                    }

                    hasMissingProperty = true;
                    Label missingLabel = new Label("✗ " + propertyStatus.property.name + " (missing: a " + DisplayNameForTargetKind(kind) + " part needs it)");
                    missingLabel.AddToClassList("toolkit-text--error");
                    shaderCardBody.Add(missingLabel);
                }
            }

            if (hasMissingProperty)
            {
                shaderCardBody.Add(ToolkitChrome.MakeHint(
                    "Add them with a Custom Function node from Packages/com.dotsanimationtoolkit/Shaders/Nodes/*.hlsl, or see shader-contract.md."));
            }
        }

        private void AddPrefabCard(Material material)
        {
            if (BoundSubject.Mesh == null || material == null)
            {
                return;
            }

            Mesh mesh = BoundSubject.Mesh;
            VisualElement prefabCardBody;
            VisualElement prefabCardHeaderActions;
            VisualElement prefabCard = ToolkitChrome.MakeCard(
                "material-inspector-prefab-card",
                "Prefab",
                out prefabCardBody,
                out prefabCardHeaderActions);

            GameObject existingPrefab = MaterialPrefabSaving.FindPrefabForMesh(mesh);
            string saveButtonText;
            if (existingPrefab != null)
            {
                Label prefabNameLabel = new Label(existingPrefab.name) { name = "material-inspector-prefab-name" };
                prefabCardBody.Add(ToolkitChrome.MakePropertyRow("Prefab", prefabNameLabel, "The prefab saved beside this mesh."));

                Button selectPrefabButton = ToolkitChrome.MakeGhostAction(
                    () => SelectAndPingPrefab(existingPrefab),
                    ToolkitIcons.Frame,
                    "Select this prefab in the Project and Inspector",
                    "Select");
                selectPrefabButton.name = "material-inspector-prefab-select";
                prefabCardHeaderActions.Add(selectPrefabButton);
                saveButtonText = "Update prefab";
            }
            else
            {
                prefabCardBody.Add(ToolkitChrome.MakeHint(
                    "Saves " + mesh.name + ".prefab beside the mesh, with this material on it."));
                saveButtonText = "Save as prefab";
            }

            Button savePrefabButton = ToolkitChrome.MakePrimaryAction(
                () => SaveMeshWithMaterialAsPrefab(mesh, material),
                ToolkitIcons.Plus,
                "Put this material on " + mesh.name + " and keep it as a prefab beside the mesh",
                saveButtonText);
            savePrefabButton.name = "material-inspector-prefab-save";
            savePrefabButton.style.flexShrink = 0f;
            prefabCardBody.Add(savePrefabButton);

            bodyScrollView.Add(prefabCard);
        }

        private static void SelectAndPingPrefab(GameObject prefab)
        {
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }

        private void SaveMeshWithMaterialAsPrefab(Mesh mesh, Material material)
        {
            GameObject savedPrefab;
            string resultMessage;
            if (!MaterialPrefabSaving.TrySavePrefab(mesh, material, out savedPrefab, out resultMessage))
            {
                ReportStatus(resultMessage, ToolkitStatusTone.Error);
                return;
            }

            ReportStatus(resultMessage, ToolkitStatusTone.Neutral);
            EditorGUIUtility.PingObject(savedPrefab);
            RebuildBody();
        }

        private void AddUseInRigAction(Material material)
        {
            if (BoundRig == null || BoundRig.sourcePrefab == null || BoundSubject.Mesh == null)
            {
                return;
            }

            Mesh mesh = BoundSubject.Mesh;
            int partCount = MaterialMeshAssignment.CountRendererSlotsUsingMesh(BoundRig.sourcePrefab, mesh);
            Button useInRigButton = ToolkitChrome.MakePrimaryAction(
                () => UseMaterialInRig(material),
                ToolkitIcons.Plus,
                partCount > 0
                    ? "Put this material on every part of " + BoundRig.name + "'s prefab that uses " + mesh.name
                    : "No part of the rig uses this mesh",
                "Use in rig: " + partCount + " parts");
            useInRigButton.name = "material-inspector-use-in-rig";
            useInRigButton.SetEnabled(partCount > 0);
            useInRigButton.style.flexShrink = 0f;
            bodyScrollView.Add(useInRigButton);
        }

        private void UseMaterialInRig(Material material)
        {
            int assignedSlotCount;
            string failureMessage;
            if (!MaterialMeshAssignment.TryAssignToRenderersUsingMesh(
                    BoundRig.sourcePrefab,
                    BoundSubject.Mesh,
                    material,
                    out assignedSlotCount,
                    out failureMessage))
            {
                ReportStatus(failureMessage, ToolkitStatusTone.Error);
                return;
            }

            ReportStatus("Put " + material.name + " on " + assignedSlotCount + " parts.", ToolkitStatusTone.Neutral);
            MaterialChanged?.Invoke(material);
            RebuildBody();
        }

        private void AddPropertyRow(VisualElement targetContainer, ContractPropertyStatus propertyStatus, TargetKind kind)
        {
            string propertyName = propertyStatus.property.name;
            Label propertyRow;

            switch (propertyStatus.state)
            {
                case ContractPropertyState.Present:
                    propertyRow = new Label("✓ " + propertyName);
                    break;
                case ContractPropertyState.Missing:
                    propertyRow = new Label("✗ " + propertyName + " (missing: a " + DisplayNameForTargetKind(kind) + " part needs it)");
                    propertyRow.AddToClassList("toolkit-text--error");
                    break;
                case ContractPropertyState.CoveredByAlternative:
                    propertyRow = new Label("– " + propertyName + " (not in this shader; the other frame property covers it)");
                    break;
                case ContractPropertyState.PresentButNotNeeded:
                    propertyRow = new Label("– " + propertyName + " (not needed for " + DisplayNameForTargetKind(kind) + ")");
                    break;
                default:
                    return;
            }

            propertyRow.name = "material-inspector-property-" + propertyName;
            targetContainer.Add(propertyRow);
        }

        private static string DisplayNameForTargetKind(TargetKind kind)
        {
            switch (kind)
            {
                case TargetKind.Quad:
                    return "Quad";
                case TargetKind.VatMesh:
                    return "VAT Mesh";
                case TargetKind.FlipbookPlane:
                    return "Flipbook Plane";
                default:
                    return kind.ToString();
            }
        }
    }
}
