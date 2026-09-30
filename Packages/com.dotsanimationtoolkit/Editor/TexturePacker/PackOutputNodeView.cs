// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Exactly one of these lives in the graph. Each of its four input ports is a channel of the
    /// texture that will be written to disk: wire a source channel into it, or give it a flat value.
    /// </summary>
    public sealed class PackOutputNodeView : Node
    {
        private const float PreviewSize = 128f;

        private readonly Port[] channelPorts = new Port[PackChannelIndex.Count];
        private readonly Toggle[] invertToggles = new Toggle[PackChannelIndex.Count];
        private readonly Slider[] defaultSliders = new Slider[PackChannelIndex.Count];

        private readonly Vector2IntField resolutionField;
        private readonly EnumField previewChannelField;
        private readonly Image previewImage;
        private readonly Label outputPathLabel;

        private Texture2D previewTexture;

        public event Action SettingsChanged;
        public event Action MatchLargestSourceRequested;

        /// A texture drop landed on one channel row.
        public event Action<int, IReadOnlyList<Texture2D>> SourceDroppedOnChannel;

        public PackOutputNodeView()
        {
            title = "Pack Output";
            titleContainer.AddToClassList("toolkit-card__header");

            Label outputBadge = ToolkitChrome.MakeBadge("PNG", ToolkitStatusTone.Neutral);
            outputBadge.AddToClassList("texture-packer-output-badge");
            outputBadge.tooltip = "The packed texture this node writes to disk.";
            titleContainer.Add(outputBadge);

            // The graph has no meaning without this node, so take deletion off the table.
            capabilities &= ~(Capabilities.Deletable | Capabilities.Copiable);

            for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
            {
                inputContainer.Add(BuildChannelRow(channelIndex));
            }

            resolutionField = new Vector2IntField();
            resolutionField.value = new Vector2Int(1024, 1024);
            resolutionField.AddToClassList("texture-packer-output-value");
            resolutionField.RegisterValueChangedCallback(changeEvent =>
            {
                Vector2Int clamped = new Vector2Int(Mathf.Max(1, changeEvent.newValue.x), Mathf.Max(1, changeEvent.newValue.y));
                if (clamped != changeEvent.newValue)
                {
                    resolutionField.SetValueWithoutNotify(clamped);
                }
                SettingsChanged?.Invoke();
            });

            Button presetsButton = new Button();
            presetsButton.name = "output-presets-button";
            presetsButton.text = "Presets ▾";
            presetsButton.AddToClassList("texture-packer-output-presets");
            presetsButton.clicked += () => OpenPresetsMenu(presetsButton);

            VisualElement sizeRow = MakeBodyRow("Size");
            sizeRow.AddToClassList("texture-packer-output-row--first");
            sizeRow.Add(resolutionField);
            sizeRow.Add(presetsButton);
            extensionContainer.Add(sizeRow);

            previewChannelField = new EnumField(PackPreviewChannel.RGB);
            previewChannelField.AddToClassList("texture-packer-output-value");
            previewChannelField.RegisterValueChangedCallback(changeEvent => SettingsChanged?.Invoke());
            VisualElement viewRow = MakeBodyRow("View");
            viewRow.Add(previewChannelField);
            extensionContainer.Add(viewRow);

            outputPathLabel = new Label();
            outputPathLabel.AddToClassList("texture-packer-output-value");
            outputPathLabel.AddToClassList("texture-packer-output-path");
            VisualElement outputRow = MakeBodyRow("Output");
            outputRow.Add(outputPathLabel);
            extensionContainer.Add(outputRow);
            SetOutputPathLabel(null);

            previewImage = new Image();
            previewImage.scaleMode = ScaleMode.ScaleToFit;
            previewImage.style.width = PreviewSize;
            previewImage.style.height = PreviewSize;
            previewImage.AddToClassList("texture-packer-output-preview");
            SetDisplayed(previewImage, false);
            extensionContainer.Add(previewImage);

            RefreshExpandedState();
            RefreshPorts();
        }

        // One row: [port] [invert toggle] [flat-value slider]. Toggle and slider swap by wired state.
        private VisualElement BuildChannelRow(int channelIndex)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("texture-packer-output-row");

            VisualElement labelColumn = new VisualElement();
            labelColumn.AddToClassList("texture-packer-output-label");
            labelColumn.AddToClassList("texture-packer-output-channel-label");
            row.Add(labelColumn);

            Port channelPort = TexturePackPortBuilder.MakePort(
                this,
                UnityEditor.Experimental.GraphView.Direction.Input,
                Port.Capacity.Single,
                PackChannelIndex.Names[channelIndex],
                PackChannelIndex.PortColors[channelIndex]);
            channelPorts[channelIndex] = channelPort;
            labelColumn.Add(channelPort);

            Toggle invertToggle = new Toggle("inv");
            invertToggle.tooltip = "Invert channel (255 - value).";
            invertToggle.AddToClassList("texture-packer-output-invert");
            invertToggle.RegisterValueChangedCallback(changeEvent => SettingsChanged?.Invoke());
            invertToggles[channelIndex] = invertToggle;
            row.Add(invertToggle);

            Slider defaultSlider = new Slider(0f, 1f);
            defaultSlider.showInputField = true;
            defaultSlider.value = channelIndex == PackChannelIndex.Alpha ? 1f : 0f;
            defaultSlider.tooltip = "Flat value written to this channel while nothing is wired in.";
            defaultSlider.AddToClassList("texture-packer-output-value");
            defaultSlider.RegisterValueChangedCallback(changeEvent => SettingsChanged?.Invoke());
            defaultSliders[channelIndex] = defaultSlider;
            row.Add(defaultSlider);

            row.RegisterCallback<DragUpdatedEvent>(dragEvent => OnChannelRowDragUpdated());
            row.RegisterCallback<DragPerformEvent>(dragEvent => OnChannelRowDragPerform(channelIndex, dragEvent));

            return row;
        }

        private static VisualElement MakeBodyRow(string labelText)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("texture-packer-output-row");
            Label label = new Label(labelText);
            label.AddToClassList("texture-packer-output-label");
            row.Add(label);
            return row;
        }

        private static void OnChannelRowDragUpdated()
        {
            if (DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.OfType<Texture2D>().Any())
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
        }

        private void OnChannelRowDragPerform(int channelIndex, DragPerformEvent dragEvent)
        {
            List<Texture2D> droppedTextures = DragAndDrop.objectReferences == null
                ? new List<Texture2D>()
                : DragAndDrop.objectReferences.OfType<Texture2D>().ToList();
            if (droppedTextures.Count == 0)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            SourceDroppedOnChannel?.Invoke(channelIndex, droppedTextures);
            // Otherwise the GraphView's own handler also adds the node at the drop point.
            dragEvent.StopPropagation();
        }

        private void OpenPresetsMenu(Button anchor)
        {
            GenericDropdownMenu menu = new GenericDropdownMenu();
            menu.AddItem("Match Largest Source", false, () => MatchLargestSourceRequested?.Invoke());
            menu.AddItem("256", false, () => ApplyResolutionPreset(256));
            menu.AddItem("512", false, () => ApplyResolutionPreset(512));
            menu.AddItem("1024", false, () => ApplyResolutionPreset(1024));
            menu.AddItem("2048", false, () => ApplyResolutionPreset(2048));
            menu.AddItem("4096", false, () => ApplyResolutionPreset(4096));
            menu.DropDown(anchor.worldBound, anchor, DropdownMenuSizeMode.Auto);
        }

        private void ApplyResolutionPreset(int size)
        {
            Resolution = new Vector2Int(size, size);
            SettingsChanged?.Invoke();
        }

        // Row state — a wired channel shows its invert toggle; an unwired one shows its slider
        public void RefreshChannelRows()
        {
            for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
            {
                bool isWired = channelPorts[channelIndex].connected;
                SetDisplayed(invertToggles[channelIndex], isWired);
                SetDisplayed(defaultSliders[channelIndex], !isWired);
            }
        }

        private static void SetDisplayed(VisualElement element, bool displayed)
        {
            element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Accessors used by the window when it builds a job description or a recipe
        public Port GetChannelPort(int channelIndex) => channelPorts[channelIndex];

        public bool GetInvert(int channelIndex) => invertToggles[channelIndex].value;

        public void SetInvert(int channelIndex, bool invert) => invertToggles[channelIndex].SetValueWithoutNotify(invert);

        public float GetDefaultValue(int channelIndex) => defaultSliders[channelIndex].value;

        public void SetDefaultValue(int channelIndex, float defaultValue) => defaultSliders[channelIndex].SetValueWithoutNotify(defaultValue);

        public Vector2Int Resolution
        {
            get => resolutionField.value;
            set => resolutionField.SetValueWithoutNotify(value);
        }

        public PackPreviewChannel PreviewChannel => (PackPreviewChannel)previewChannelField.value;

        public void SetOutputPathLabel(string outputAssetPath)
        {
            bool hasPath = !string.IsNullOrEmpty(outputAssetPath);
            outputPathLabel.text = hasPath ? outputAssetPath : "Not chosen";
            outputPathLabel.tooltip = hasPath ? outputAssetPath : string.Empty;
            outputPathLabel.EnableInClassList("texture-packer-output-path--empty", !hasPath);
        }

        // Takes ownership of the preview texture and destroys the one it replaces.
        public void SetPreviewTexture(Texture2D newPreviewTexture)
        {
            if (previewTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(previewTexture);
            }
            previewTexture = newPreviewTexture;
            previewImage.image = previewTexture;
            SetDisplayed(previewImage, previewTexture != null);
        }

        public void DisposePreviewTexture()
        {
            SetPreviewTexture(null);
        }
    }
}
