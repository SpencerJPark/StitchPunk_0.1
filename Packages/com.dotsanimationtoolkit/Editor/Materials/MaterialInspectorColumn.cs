// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Materials tab's detail column: one material's shader, users, contract properties, instancing and flipbook findings.</summary>
    public sealed class MaterialInspectorColumn : VisualElement
    {
        public RigMaterialUsage BoundUsage { get; private set; }
        public ClipSetAsset BoundClipSet { get; private set; }

        private readonly Label hintLabel;
        private readonly Button selectButton;
        private readonly ScrollView bodyScrollView;

        public MaterialInspectorColumn()
        {
            style.flexGrow = 1f;
            AddToClassList("toolkit-column");

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            Label titleLabel = new Label("Material") { name = "material-inspector-title" };
            titleLabel.AddToClassList("toolkit-pane-title");
            header.Add(titleLabel);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("toolkit-pane-actions");
            selectButton = ToolkitIcons.MakeIconTextButton(
                SelectBoundMaterial,
                "d_UnityEditor.InspectorWindow",
                "Select this material in the Inspector",
                "Inspector");
            selectButton.name = "material-inspector-select";
            actions.Add(selectButton);
            header.Add(actions);
            Add(header);

            hintLabel = new Label("Pick a material on the left.") { name = "material-inspector-hint" };
            hintLabel.AddToClassList("toolkit-hint");
            Add(hintLabel);

            bodyScrollView = new ScrollView();
            bodyScrollView.style.flexGrow = 1f;
            Add(bodyScrollView);
        }

        public void Bind(RigMaterialUsage usage, ClipSetAsset clipSet)
        {
            BoundUsage = usage;
            BoundClipSet = clipSet;
            RebuildBody();
        }

        private void SelectBoundMaterial()
        {
            if (BoundUsage == null || BoundUsage.Material == null)
            {
                return;
            }

            Selection.activeObject = BoundUsage.Material;
            EditorGUIUtility.PingObject(BoundUsage.Material);
        }

        private void RebuildBody()
        {
            bodyScrollView.Clear();

            Material material = BoundUsage != null ? BoundUsage.Material : null;
            if (BoundUsage == null || material == null)
            {
                hintLabel.style.display = DisplayStyle.Flex;
                bodyScrollView.style.display = DisplayStyle.None;
                selectButton.SetEnabled(false);
                return;
            }

            hintLabel.style.display = DisplayStyle.None;
            bodyScrollView.style.display = DisplayStyle.Flex;
            selectButton.SetEnabled(true);

            Label shaderRow = new Label("Shader  " + (material.shader != null ? material.shader.name : "no shader")) { name = "material-inspector-shader" };
            bodyScrollView.Add(shaderRow);

            List<RigTargetDefinition> targets = BoundUsage.Targets;
            string usedByText = targets != null && targets.Count > 0
                ? string.Join(", ", targets.Select(target => target.displayName))
                : "no rig target";
            Label usedByRow = new Label("Used by  " + usedByText) { name = "material-inspector-used-by" };
            bodyScrollView.Add(usedByRow);

            if (targets == null || targets.Count == 0)
            {
                if (BoundUsage.UnmappedNodePaths != null && BoundUsage.UnmappedNodePaths.Count > 0)
                {
                    Label unmappedHint = new Label("Unmapped: " + string.Join(", ", BoundUsage.UnmappedNodePaths));
                    unmappedHint.AddToClassList("toolkit-hint");
                    bodyScrollView.Add(unmappedHint);
                }

                Label noTargetHint = new Label("No rig target uses this material, so no contract applies.");
                noTargetHint.AddToClassList("toolkit-hint");
                bodyScrollView.Add(noTargetHint);
            }
            else
            {
                IEnumerable<TargetKind> distinctKindsInEnumOrder = targets
                    .Select(target => target.kind)
                    .Distinct()
                    .OrderBy(kind => (int)kind);

                foreach (TargetKind kind in distinctKindsInEnumOrder)
                {
                    Label sectionLabel = new Label(DisplayNameForTargetKind(kind));
                    bodyScrollView.Add(sectionLabel);

                    List<ContractPropertyStatus> propertyStatuses = new List<ContractPropertyStatus>();
                    MaterialContractValidation.EvaluateProperties(material, kind, propertyStatuses);
                    foreach (ContractPropertyStatus propertyStatus in propertyStatuses)
                    {
                        AddPropertyRow(propertyStatus, kind);
                    }
                }
            }

            bool isInstancingEnabled = material.enableInstancing;
            Label instancingRow = new Label(isInstancingEnabled
                ? "✓ GPU instancing"
                : "✗ GPU instancing off (Entities Graphics needs it on)") { name = "material-inspector-instancing" };
            if (!isInstancingEnabled)
            {
                instancingRow.AddToClassList("toolkit-text--error");
            }

            bodyScrollView.Add(instancingRow);

            List<ValidationMessage> flipbookWarnings = new List<ValidationMessage>();
            RigMaterialResolver.CollectFlipbookBindingWarnings(BoundUsage, BoundClipSet, flipbookWarnings);
            foreach (ValidationMessage warning in flipbookWarnings)
            {
                Label warningRow = new Label("● " + warning.text) { name = "material-inspector-flipbook-warning" };
                warningRow.AddToClassList("toolkit-text--warning");
                bodyScrollView.Add(warningRow);
            }
        }

        private void AddPropertyRow(ContractPropertyStatus propertyStatus, TargetKind kind)
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
            bodyScrollView.Add(propertyRow);
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
