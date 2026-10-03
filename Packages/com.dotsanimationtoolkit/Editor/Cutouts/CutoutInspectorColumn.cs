// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed class CutoutInspectorColumn : VisualElement
    {
        public event Action<float> PixelsPerUnitChanged;
        public event Action<int> FrameStepRequested;
        public event Action<bool> AllFramesGhostToggled;
        public event Action FitToArtRequested;
        public event Action<int, float> FitSettingsChanged;
        public event Action<Vector2> OriginPixelsChanged;
        public event Action<Vector2> OriginPresetRequested;
        public event Action<CutoutFacing> FacingChanged;
        public event Action<CutoutNormalMode> NormalModeChanged;
        public event Action<float> RoundnessChanged;
        public event Action<Texture2D> ReferenceImageChanged;
        public event Action<Rect> ReferenceRectChanged;
        public event Action<float> ReferenceOpacityChanged;
        public event Action OutputBrowseRequested;

        private const float OriginPresetToleranceInPixels = 0.5f;
        private const float MinimumReferenceSizeInWorldUnits = 0.01f;

        private static readonly string[] OriginPresetNames =
        {
            "Custom", "Centre", "Bottom centre", "Bottom left", "Bottom right", "Top left", "Top right",
        };

        // Index i + 1 in OriginPresetNames; index 0 ("Custom") has no point.
        private static readonly Vector2[] OriginPresetPoints =
        {
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0f, 0f),
            new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
        };

        private readonly VisualElement scrollContent;
        private readonly VisualElement noCutoutHint;

        private readonly FloatField pixelsPerUnitField;
        private readonly Label frameLabel;
        private readonly Toggle allFramesToggle;
        private readonly Label vertexCountLabel;
        private readonly IntegerField vertexBudgetField;
        private readonly FloatField paddingField;
        private readonly Vector2Field originField;
        private readonly DropdownField originPresetField;
        private readonly VisualElement facingControl;
        private readonly VisualElement normalModeControl;
        private readonly VisualElement roundnessRow;
        private readonly Slider roundnessSlider;
        private readonly ObjectField referenceImageField;
        private readonly Slider referenceOpacitySlider;
        private readonly Vector2Field referencePositionField;
        private readonly Vector2Field referenceSizeField;
        private readonly PathPickerRowElement outputPathRow;

        private CutoutAsset shownCutout;

        public CutoutInspectorColumn()
        {
            name = "cutouts-inspector-column";
            AddToClassList("toolkit-column");
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            VisualElement headerRow = ToolkitChrome.MakePaneHeader("Inspector", out _, out _);
            headerRow.name = "cutouts-inspector-header";
            headerRow.style.flexShrink = 0f;
            Add(headerRow);

            ScrollView scrollView = new ScrollView { name = "cutouts-inspector-scroll" };
            scrollView.style.flexGrow = 1f;
            Add(scrollView);

            noCutoutHint = ToolkitChrome.MakeHint("Pick a flipbook on the left to edit its cutout.");
            noCutoutHint.name = "cutouts-inspector-hint";
            scrollView.Add(noCutoutHint);

            scrollContent = new VisualElement { name = "cutouts-inspector-content" };
            scrollView.Add(scrollContent);

            // Flipbook
            VisualElement flipbookBody;
            scrollContent.Add(ToolkitChrome.MakeCard("cutouts-card-flipbook", "Flipbook", out flipbookBody, out _));

            pixelsPerUnitField = new FloatField { name = "cutouts-pixels-per-unit" };
            pixelsPerUnitField.RegisterValueChangedCallback((ChangeEvent<float> changeEvent) =>
            {
                if (changeEvent.newValue < 1f)
                {
                    pixelsPerUnitField.SetValueWithoutNotify(1f);
                }

                PixelsPerUnitChanged?.Invoke(Mathf.Max(1f, changeEvent.newValue));
            });
            flipbookBody.Add(ToolkitChrome.MakePropertyRow("Pixels / unit", pixelsPerUnitField,
                "Pixels of art per world unit — Unity's sprite meaning. Changing it rescales the art and the shape together; the reference keeps its size."));

            VisualElement frameRow = new VisualElement { name = "cutouts-frame-row" };
            frameRow.style.flexDirection = FlexDirection.Row;
            frameRow.style.alignItems = Align.Center;
            frameRow.style.flexGrow = 1f;
            frameRow.Add(ToolkitChrome.MakeIconSquare(() => FrameStepRequested?.Invoke(-1), "d_Animation.PrevKey", "Previous frame"));
            frameLabel = new Label { name = "cutouts-frame-label" };
            frameLabel.AddToClassList("cutouts-frame-label");
            frameLabel.style.flexGrow = 1f;
            frameLabel.style.overflow = Overflow.Hidden;
            frameLabel.style.textOverflow = TextOverflow.Ellipsis;
            frameRow.Add(frameLabel);
            frameRow.Add(ToolkitChrome.MakeIconSquare(() => FrameStepRequested?.Invoke(1), "d_Animation.NextKey", "Next frame"));
            flipbookBody.Add(ToolkitChrome.MakePropertyRow("Frame", frameRow, "Step through the flipbook's frames."));

            allFramesToggle = new Toggle { name = "cutouts-all-frames" };
            allFramesToggle.RegisterValueChangedCallback((ChangeEvent<bool> changeEvent) => AllFramesGhostToggled?.Invoke(changeEvent.newValue));
            flipbookBody.Add(ToolkitChrome.MakePropertyRow("All frames", allFramesToggle,
                "Draw every frame's art as a ghost, so you can see the shape must cover all of them"));

            // Shape
            VisualElement shapeBody;
            VisualElement shapeHeaderActions;
            scrollContent.Add(ToolkitChrome.MakeCard("cutouts-card-shape", "Shape", out shapeBody, out shapeHeaderActions));
            shapeHeaderActions.Add(ToolkitChrome.MakeSecondaryAction(() => FitToArtRequested?.Invoke(), "d_Grid.FillTool",
                "Trace every frame's art, wrap it in a convex outline grown by the padding, and reduce it to the vertex budget. Edit by hand from there.",
                "Fit to art"));

            vertexCountLabel = new Label { name = "cutouts-vertex-count" };
            shapeBody.Add(ToolkitChrome.MakePropertyRow("Vertices", vertexCountLabel, "How many corners the outline has."));

            vertexBudgetField = new IntegerField { name = "cutouts-vertex-budget" };
            vertexBudgetField.RegisterValueChangedCallback((ChangeEvent<int> changeEvent) =>
            {
                int clampedBudget = Mathf.Clamp(changeEvent.newValue, 4, 32);
                vertexBudgetField.SetValueWithoutNotify(clampedBudget);
                FitSettingsChanged?.Invoke(clampedBudget, Mathf.Clamp(paddingField.value, 0f, 64f));
            });
            shapeBody.Add(ToolkitChrome.MakePropertyRow("Vertex budget", vertexBudgetField,
                "The most corners Fit to art may use (4 to 32)."));

            paddingField = new FloatField { name = "cutouts-padding" };
            paddingField.RegisterValueChangedCallback((ChangeEvent<float> changeEvent) =>
            {
                float clampedPadding = Mathf.Clamp(changeEvent.newValue, 0f, 64f);
                paddingField.SetValueWithoutNotify(clampedPadding);
                FitSettingsChanged?.Invoke(Mathf.Clamp(vertexBudgetField.value, 4, 32), clampedPadding);
            });
            shapeBody.Add(ToolkitChrome.MakePropertyRow("Padding (px)", paddingField,
                "How far Fit to art grows the outline past the art, in pixels (0 to 64)."));

            // Origin
            VisualElement originBody;
            scrollContent.Add(ToolkitChrome.MakeCard("cutouts-card-origin", "Origin", out originBody, out _));

            originField = new Vector2Field { name = "cutouts-origin" };
            originField.RegisterValueChangedCallback((ChangeEvent<Vector2> changeEvent) => OriginPixelsChanged?.Invoke(changeEvent.newValue));
            originBody.Add(ToolkitChrome.MakePropertyRow("Position (px)", originField,
                "The mesh's (0,0,0): the part rotates about it. Put it on the joint."));

            originPresetField = new DropdownField(new System.Collections.Generic.List<string>(OriginPresetNames), 0) { name = "cutouts-origin-preset" };
            originPresetField.RegisterValueChangedCallback((ChangeEvent<string> changeEvent) =>
            {
                int presetIndex = Array.IndexOf(OriginPresetNames, changeEvent.newValue);
                if (presetIndex > 0)
                {
                    OriginPresetRequested?.Invoke(OriginPresetPoints[presetIndex - 1]);
                }
            });
            originBody.Add(ToolkitChrome.MakePropertyRow("Preset", originPresetField,
                "The mesh's (0,0,0): the part rotates about it. Put it on the joint."));

            // Facing
            VisualElement facingBody;
            scrollContent.Add(ToolkitChrome.MakeCard("cutouts-card-facing", "Facing", out facingBody, out _));

            facingControl = ToolkitChrome.MakeSegmentedControl("cutouts-facing",
                new[] { "−Z (toward camera)", "+Z" }, 0,
                (int selectedIndex) => FacingChanged?.Invoke(selectedIndex == 0 ? CutoutFacing.NegativeZ : CutoutFacing.PositiveZ));
            facingBody.Add(ToolkitChrome.MakePropertyRow("Faces", facingControl, "Which way the flat mesh's front side points."));

            normalModeControl = ToolkitChrome.MakeSegmentedControl("cutouts-normal-mode",
                new[] { "Flat", "Rounded" }, 0,
                (int selectedIndex) =>
                {
                    CutoutNormalMode selectedMode = selectedIndex == 0 ? CutoutNormalMode.Flat : CutoutNormalMode.Rounded;
                    roundnessRow.style.display = selectedMode == CutoutNormalMode.Rounded ? DisplayStyle.Flex : DisplayStyle.None;
                    NormalModeChanged?.Invoke(selectedMode);
                });
            facingBody.Add(ToolkitChrome.MakePropertyRow("Normals", normalModeControl, "Flat lights the part like a card; Rounded bends the normals as if it were puffy."));

            roundnessSlider = new Slider(0f, 1f) { name = "cutouts-roundness", showInputField = true };
            roundnessSlider.RegisterValueChangedCallback((ChangeEvent<float> changeEvent) => RoundnessChanged?.Invoke(changeEvent.newValue));
            roundnessRow = ToolkitChrome.MakePropertyRow("Roundness", roundnessSlider, "How strongly the normals bend toward the rim.");
            roundnessRow.name = "cutouts-roundness-row";
            facingBody.Add(roundnessRow);

            // Reference
            VisualElement referenceBody;
            scrollContent.Add(ToolkitChrome.MakeCard("cutouts-card-reference", "Reference", out referenceBody, out _));
            const string referenceTooltip =
                "A sizing aid drawn under the art in world units; never baked into the mesh. Match the art to it by changing Pixels / unit.";

            referenceImageField = new ObjectField { name = "cutouts-reference-image", objectType = typeof(Texture2D), allowSceneObjects = false };
            referenceImageField.RegisterValueChangedCallback((ChangeEvent<UnityEngine.Object> changeEvent) =>
                ReferenceImageChanged?.Invoke(changeEvent.newValue as Texture2D));
            referenceBody.Add(ToolkitChrome.MakePropertyRow("Image", referenceImageField, referenceTooltip));

            referenceOpacitySlider = new Slider(0f, 1f) { name = "cutouts-reference-opacity", showInputField = true };
            referenceOpacitySlider.RegisterValueChangedCallback((ChangeEvent<float> changeEvent) => ReferenceOpacityChanged?.Invoke(changeEvent.newValue));
            referenceBody.Add(ToolkitChrome.MakePropertyRow("Opacity", referenceOpacitySlider, referenceTooltip));

            referencePositionField = new Vector2Field { name = "cutouts-reference-position" };
            referencePositionField.RegisterValueChangedCallback((ChangeEvent<Vector2> changeEvent) => RaiseReferenceRectChanged());
            referenceBody.Add(ToolkitChrome.MakePropertyRow("Position", referencePositionField, referenceTooltip));

            referenceSizeField = new Vector2Field { name = "cutouts-reference-size" };
            referenceSizeField.RegisterValueChangedCallback((ChangeEvent<Vector2> changeEvent) => RaiseReferenceRectChanged());
            referenceBody.Add(ToolkitChrome.MakePropertyRow("Size", referenceSizeField, referenceTooltip));

            // Output
            VisualElement outputBody;
            scrollContent.Add(ToolkitChrome.MakeCard("cutouts-card-output", "Output", out outputBody, out _));

            outputPathRow = new PathPickerRowElement(null, "Choose where the mesh asset is written") { name = "cutouts-output-path" };
            outputPathRow.BrowseRequested += () => OutputBrowseRequested?.Invoke();
            outputBody.Add(ToolkitChrome.MakePropertyRow("Mesh", outputPathRow, "Where the baked mesh asset is written."));

            ShowCutout(null, 0, 0, string.Empty, true);
        }

        public void ShowCutout(CutoutAsset cutout, int frameIndex, int frameCount, string frameName, bool isAllFramesGhostVisible)
        {
            shownCutout = cutout;
            bool hasCutout = cutout != null;
            scrollContent.SetEnabled(hasCutout);
            noCutoutHint.style.display = hasCutout ? DisplayStyle.None : DisplayStyle.Flex;

            allFramesToggle.SetValueWithoutNotify(isAllFramesGhostVisible);
            frameLabel.text = (frameIndex + 1) + " / " + frameCount + " · " + frameName;
            frameLabel.tooltip = frameLabel.text;

            if (!hasCutout)
            {
                return;
            }

            pixelsPerUnitField.SetValueWithoutNotify(cutout.pixelsPerUnit);
            vertexCountLabel.text = cutout.outlinePixels.Count.ToString();
            vertexBudgetField.SetValueWithoutNotify(Mathf.Clamp(cutout.fitVertexBudget, 4, 32));
            paddingField.SetValueWithoutNotify(Mathf.Clamp(cutout.fitPaddingPixels, 0f, 64f));
            originField.SetValueWithoutNotify(cutout.originPixels);
            originPresetField.SetValueWithoutNotify(OriginPresetNames[FindMatchingOriginPresetIndex(cutout)]);

            ToolkitChrome.SetSegmentedSelection(facingControl, cutout.facing == CutoutFacing.NegativeZ ? 0 : 1);
            ToolkitChrome.SetSegmentedSelection(normalModeControl, cutout.normalMode == CutoutNormalMode.Flat ? 0 : 1);
            roundnessSlider.SetValueWithoutNotify(cutout.roundness);
            roundnessRow.style.display = cutout.normalMode == CutoutNormalMode.Rounded ? DisplayStyle.Flex : DisplayStyle.None;

            referenceImageField.SetValueWithoutNotify(cutout.referenceImage);
            referenceOpacitySlider.SetValueWithoutNotify(cutout.referenceOpacity);
            referencePositionField.SetValueWithoutNotify(cutout.referenceRectWorld.position);
            referenceSizeField.SetValueWithoutNotify(cutout.referenceRectWorld.size);

            outputPathRow.Path = cutout.outputPath;
        }

        private int FindMatchingOriginPresetIndex(CutoutAsset cutout)
        {
            Vector2 frameSize = new Vector2(cutout.FrameSize.x, cutout.FrameSize.y);
            for (int presetIndex = 0; presetIndex < OriginPresetPoints.Length; presetIndex++)
            {
                Vector2 presetPixels = Vector2.Scale(OriginPresetPoints[presetIndex], frameSize);
                if (Vector2.Distance(presetPixels, cutout.originPixels) <= OriginPresetToleranceInPixels)
                {
                    return presetIndex + 1;
                }
            }

            return 0;
        }

        private void RaiseReferenceRectChanged()
        {
            Vector2 size = referenceSizeField.value;
            size.x = Mathf.Max(MinimumReferenceSizeInWorldUnits, size.x);
            size.y = Mathf.Max(MinimumReferenceSizeInWorldUnits, size.y);
            referenceSizeField.SetValueWithoutNotify(size);
            ReferenceRectChanged?.Invoke(new Rect(referencePositionField.value, size));
        }
    }
}
