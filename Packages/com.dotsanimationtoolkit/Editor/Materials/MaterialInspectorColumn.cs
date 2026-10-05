// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Materials tab's detail column: which inputs a part's animations need and which its shader lacks.</summary>
    public sealed class MaterialInspectorColumn : VisualElement
    {
        private const string MissingInputsAdvice =
            "Add the missing inputs to this material's shader. The toolkit's own shaders already have them — "
            + "ToolkitSpriteUnlitArray for flipbook frames, ToolkitSpriteUnlit for atlas frames, ToolkitVatCrowdUnlit for VAT — "
            + "or build your own from the Custom Function nodes in Packages/com.dotsanimationtoolkit/Shaders/Nodes (see shader-contract.md).";

        public PartInputReport BoundReport { get; private set; }

        private RigAsset boundRig;
        private ClipSetAsset boundClipSet;

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
            titleLabel = new Label("Part") { name = "material-inspector-title" };
            titleLabel.AddToClassList("toolkit-pane-title");
            header.Add(titleLabel);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("toolkit-pane-actions");
            selectButton = ToolkitChrome.MakeGhostAction(
                SelectFirstMaterial,
                ToolkitIcons.Frame,
                "Select this part's material in the Project and Inspector",
                "Select");
            selectButton.name = "material-inspector-select";
            actions.Add(selectButton);
            header.Add(actions);
            Add(header);

            emptyState = ToolkitChrome.MakeEmptyState(
                "material-inspector-hint",
                "No part selected",
                "Pick a part on the left to see what its animations need from its material.",
                null,
                null);
            Add(emptyState);

            bodyScrollView = new ScrollView();
            bodyScrollView.style.flexGrow = 1f;
            Add(bodyScrollView);
        }

        public void Bind(PartInputReport report, RigAsset rig, ClipSetAsset clipSet)
        {
            BoundReport = report;
            boundRig = rig;
            boundClipSet = clipSet;
            RebuildBody();
        }

        private void SelectFirstMaterial()
        {
            if (BoundReport == null || BoundReport.Materials.Count == 0)
            {
                return;
            }
            SelectMaterial(BoundReport.Materials[0]);
        }

        private static void SelectMaterial(Material material)
        {
            Selection.activeObject = material;
            EditorGUIUtility.PingObject(material);
        }

        private void RebuildBody()
        {
            bodyScrollView.Clear();

            if (BoundReport == null)
            {
                titleLabel.text = "Part";
                emptyState.style.display = DisplayStyle.Flex;
                bodyScrollView.style.display = DisplayStyle.None;
                selectButton.SetEnabled(false);
                return;
            }

            emptyState.style.display = DisplayStyle.None;
            bodyScrollView.style.display = DisplayStyle.Flex;
            selectButton.SetEnabled(BoundReport.Materials.Count > 0);
            titleLabel.text = string.IsNullOrEmpty(BoundReport.DisplayName) ? "Part" : BoundReport.DisplayName;

            AddInputsCard();
            AddMaterialCard();
            AddWarnings();

            if (BoundReport.HasMissingInputs)
            {
                Label advice = ToolkitChrome.MakeHint(MissingInputsAdvice);
                advice.name = "material-inspector-advice";
                bodyScrollView.Add(advice);
            }
        }

        private void AddInputsCard()
        {
            VisualElement cardBody;
            VisualElement cardHeaderActions;
            VisualElement card = ToolkitChrome.MakeCard("material-inspector-inputs-card", "Inputs", out cardBody, out cardHeaderActions);
            card.style.flexShrink = 0f;
            bodyScrollView.Add(card);

            if (BoundReport.State == PartInputState.NotChecked)
            {
                cardBody.Add(ToolkitChrome.MakeHint("Pick a clip set: the check reads what each part's animations drive."));
                return;
            }

            if (BoundReport.State == PartInputState.NothingNeeded)
            {
                Label nothingNeededBadge = ToolkitChrome.MakeBadge("Nothing needed", ToolkitStatusTone.Ok);
                nothingNeededBadge.name = "material-inspector-nothing-needed";
                cardBody.Add(nothingNeededBadge);
                cardBody.Add(ToolkitChrome.MakeHint("Its animations only move it; no material input is driven."));
                return;
            }

            for (int needIndex = 0; needIndex < BoundReport.Needs.Count; needIndex++)
            {
                PartInputNeed need = BoundReport.Needs[needIndex];
                bool isMissing = BoundReport.MissingPropertyNames.Contains(need.PropertyName);
                Label needLabel = new Label((isMissing ? "✗ " : "✓ ") + need.PropertyName);
                if (isMissing)
                {
                    needLabel.AddToClassList("toolkit-text--error");
                }
                cardBody.Add(needLabel);
                cardBody.Add(ToolkitChrome.MakeHint(need.TrackKindLabel + " in " + string.Join(", ", need.ClipNames)));
            }
        }

        private void AddMaterialCard()
        {
            VisualElement cardBody;
            VisualElement cardHeaderActions;
            VisualElement card = ToolkitChrome.MakeCard("material-inspector-material-card", "Material", out cardBody, out cardHeaderActions);
            card.style.flexShrink = 0f;
            bodyScrollView.Add(card);

            if (BoundReport.Materials.Count == 0)
            {
                Label noMaterialLabel = new Label("No material on this part's renderer.") { name = "material-inspector-no-material" };
                noMaterialLabel.AddToClassList("toolkit-text--error");
                cardBody.Add(noMaterialLabel);
                return;
            }

            bool hasSeveralMaterials = BoundReport.Materials.Count > 1;
            for (int materialIndex = 0; materialIndex < BoundReport.Materials.Count; materialIndex++)
            {
                Material material = BoundReport.Materials[materialIndex];

                VisualElement materialField = new VisualElement();
                materialField.style.flexDirection = FlexDirection.Row;
                materialField.Add(new Label(material.name));
                if (hasSeveralMaterials)
                {
                    materialField.Add(ToolkitChrome.MakeGhostAction(
                        () => SelectMaterial(material),
                        ToolkitIcons.Frame,
                        "Select this material in the Project and Inspector",
                        "Select"));
                }
                cardBody.Add(ToolkitChrome.MakePropertyRow("Material", materialField, null));

                Label shaderValueLabel = new Label(material.shader != null ? material.shader.name : "no shader");
                shaderValueLabel.name = "material-inspector-shader";
                cardBody.Add(ToolkitChrome.MakePropertyRow("Shader", shaderValueLabel, null));

                bool isInstancingEnabled = material.enableInstancing;
                Label instancingBadge = ToolkitChrome.MakeBadge(
                    "GPU instancing",
                    isInstancingEnabled ? ToolkitStatusTone.Ok : ToolkitStatusTone.Error);
                instancingBadge.name = "material-inspector-instancing";
                if (!isInstancingEnabled)
                {
                    instancingBadge.tooltip = "Entities Graphics needs it on";
                }
                cardBody.Add(ToolkitChrome.MakePropertyRow("Instancing", instancingBadge, "Whether GPU instancing is on for this material."));
            }
        }

        private void AddWarnings()
        {
            for (int warningIndex = 0; warningIndex < BoundReport.Warnings.Count; warningIndex++)
            {
                Label warningLabel = new Label("● " + BoundReport.Warnings[warningIndex]);
                warningLabel.AddToClassList("toolkit-text--warning");
                bodyScrollView.Add(warningLabel);
            }
        }
    }
}
