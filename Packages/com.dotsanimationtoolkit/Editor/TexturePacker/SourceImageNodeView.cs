// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One dragged-in texture. Shows a thumbnail and exposes its four channels as output ports.</summary>
    public sealed class SourceImageNodeView : Node
    {
        private static readonly Color HeaderColor = new Color(0.16f, 0.32f, 0.42f);
        private static readonly Color MissingHeaderColor = new Color(0.52f, 0.14f, 0.14f);
        private const float ThumbnailSize = 96f;

        // Survives moves and renames; the recipe stores this, not a path.
        public string TextureGuid { get; private set; }

        // Null when the GUID no longer resolves to an asset.
        public Texture2D SourceTexture { get; private set; }

        public bool IsMissing => SourceTexture == null;

        // -1 while no chip is lit.
        public int ViewedChannel { get; private set; } = -1;

        public event Action<SourceImageNodeView, int> ChannelViewChanged;

        // A texture was dropped on this node; the graph decides whether the swap is allowed.
        public event Action<SourceImageNodeView, Texture2D> ReplaceRequested;

        private readonly Port[] channelPorts = new Port[PackChannelIndex.Count];
        private readonly ToolbarToggle[] channelChips = new ToolbarToggle[PackChannelIndex.Count];
        private readonly VisualElement bodyContainer = new VisualElement();

        private Image thumbnailImage;
        private Texture2D channelPreviewTexture;
        private bool isApplyingChipSelection;

        private SourceImageNodeView(string textureGuid, Texture2D sourceTexture)
        {
            // Four output ports — one per source channel. Multi capacity, because the same
            // greyscale channel is often wired into more than one output slot. Built once, here,
            // so ReplaceTexture can rebuild the body without ever touching a port or its edges.
            for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
            {
                Port channelPort = TexturePackPortBuilder.MakePort(
                    this,
                    UnityEditor.Experimental.GraphView.Direction.Output,
                    Port.Capacity.Multi,
                    PackChannelIndex.Names[channelIndex],
                    PackChannelIndex.PortColors[channelIndex]);

                channelPorts[channelIndex] = channelPort;
                outputContainer.Add(channelPort);
            }
            RefreshPorts();

            extensionContainer.Add(bodyContainer);

            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);

            ReplaceTexture(textureGuid, sourceTexture);
        }

        public Port GetChannelPort(int channelIndex)
        {
            return channelPorts[channelIndex];
        }

        // Which of this node's channels the given port represents, or -1 if the port is not ours.
        public int FindChannelIndex(Port port)
        {
            for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
            {
                if (channelPorts[channelIndex] == port)
                {
                    return channelIndex;
                }
            }
            return -1;
        }

        // Takes ownership of an isolated-channel thumbnail (null restores the imported texture)
        // and destroys the one it replaces.
        public void SetChannelPreview(Texture2D channelPreview)
        {
            Texture2D previousPreview = channelPreviewTexture;
            channelPreviewTexture = channelPreview;

            if (thumbnailImage != null)
            {
                thumbnailImage.image = channelPreview != null ? channelPreview : SourceTexture;
            }

            if (previousPreview != null)
            {
                UnityEngine.Object.DestroyImmediate(previousPreview);
            }
        }

        public void DisposeChannelPreview()
        {
            SetChannelPreview(null);
        }

        // Swaps the texture behind this node: guid, title, header colour, thumbnail and size label
        // rebuild; ports and their edges are untouched; a lit chip is cleared.
        public void ReplaceTexture(string textureGuid, Texture2D sourceTexture)
        {
            TextureGuid = textureGuid;
            SourceTexture = sourceTexture;

            DisposeChannelPreview();
            ViewedChannel = -1;

            bodyContainer.Clear();
            thumbnailImage = null;
            for (int channelIndex = 0; channelIndex < channelChips.Length; channelIndex++)
            {
                channelChips[channelIndex] = null;
            }

            if (IsMissing)
            {
                title = "Missing source";
                TexturePackPortBuilder.SetHeaderColor(this, MissingHeaderColor);
                BuildMissingBody(textureGuid);
            }
            else
            {
                title = sourceTexture.name;
                TexturePackPortBuilder.SetHeaderColor(this, HeaderColor);
                BuildThumbnailBody(sourceTexture);
                BuildChannelChipRow();
            }

            RefreshExpandedState();
        }

        // Builds a node for an asset GUID, resolving it through the AssetDatabase.
        public static SourceImageNodeView CreateFromGuid(string textureGuid)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(textureGuid);
            Texture2D sourceTexture = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            return new SourceImageNodeView(textureGuid, sourceTexture);
        }

        private void BuildThumbnailBody(Texture2D sourceTexture)
        {
            thumbnailImage = new Image();
            thumbnailImage.image = sourceTexture;
            thumbnailImage.scaleMode = ScaleMode.ScaleToFit;
            thumbnailImage.style.width = ThumbnailSize;
            thumbnailImage.style.height = ThumbnailSize;
            thumbnailImage.style.marginTop = 4f;
            thumbnailImage.style.marginBottom = 2f;
            thumbnailImage.style.alignSelf = Align.Center;
            bodyContainer.Add(thumbnailImage);

            Label sizeLabel = new Label(sourceTexture.width + " x " + sourceTexture.height);
            sizeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            sizeLabel.style.fontSize = 10;
            sizeLabel.style.opacity = 0.7f;
            sizeLabel.style.marginBottom = 4f;
            bodyContainer.Add(sizeLabel);
        }

        private void BuildMissingBody(string textureGuid)
        {
            Label missingLabel = new Label("Asset not found\n" + textureGuid);
            missingLabel.style.whiteSpace = WhiteSpace.Normal;
            missingLabel.style.fontSize = 10;
            missingLabel.style.width = ThumbnailSize + 40f;
            missingLabel.style.marginTop = 4f;
            missingLabel.style.marginBottom = 4f;
            missingLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            bodyContainer.Add(missingLabel);
        }

        private void BuildChannelChipRow()
        {
            VisualElement chipRow = new VisualElement();
            chipRow.style.flexDirection = FlexDirection.Row;
            chipRow.style.justifyContent = Justify.Center;
            chipRow.style.marginBottom = 4f;

            string[] chipNames = { "source-channel-chip-r", "source-channel-chip-g", "source-channel-chip-b", "source-channel-chip-a" };
            for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
            {
                ToolbarToggle chip = new ToolbarToggle();
                chip.name = chipNames[channelIndex];
                chip.text = PackChannelIndex.Names[channelIndex];
                chip.AddToClassList("clip-editor__tab");

                int capturedChannelIndex = channelIndex;
                chip.RegisterValueChangedCallback(changeEvent => OnChannelChipChanged(capturedChannelIndex, changeEvent.newValue));

                channelChips[channelIndex] = chip;
                chipRow.Add(chip);
            }

            bodyContainer.Add(chipRow);
        }

        private void OnChannelChipChanged(int channelIndex, bool isChipOn)
        {
            if (isApplyingChipSelection)
            {
                return;
            }

            if (!isChipOn)
            {
                if (ViewedChannel != channelIndex)
                {
                    return;
                }

                // Lighting the lit chip again turns it off — "no channel" is a real state here.
                ViewedChannel = -1;
                SetChannelPreview(null);
                ChannelViewChanged?.Invoke(this, ViewedChannel);
                return;
            }

            isApplyingChipSelection = true;
            for (int otherChannelIndex = 0; otherChannelIndex < channelChips.Length; otherChannelIndex++)
            {
                if (otherChannelIndex != channelIndex && channelChips[otherChannelIndex] != null)
                {
                    channelChips[otherChannelIndex].SetValueWithoutNotify(false);
                }
            }
            isApplyingChipSelection = false;

            ViewedChannel = channelIndex;
            ChannelViewChanged?.Invoke(this, ViewedChannel);
        }

        private void OnDragUpdated(DragUpdatedEvent dragUpdatedEvent)
        {
            DragAndDrop.visualMode = FindDraggedTexture() != null
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;
        }

        private void OnDragPerform(DragPerformEvent dragPerformEvent)
        {
            Texture2D draggedTexture = FindDraggedTexture();
            if (draggedTexture == null)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            ReplaceRequested?.Invoke(this, draggedTexture);

            // Otherwise the canvas's own drag handler also runs and adds a new node at the drop point.
            dragPerformEvent.StopPropagation();
        }

        private static Texture2D FindDraggedTexture()
        {
            if (DragAndDrop.objectReferences == null)
            {
                return null;
            }

            foreach (UnityEngine.Object draggedObject in DragAndDrop.objectReferences)
            {
                if (draggedObject is Texture2D draggedTexture)
                {
                    return draggedTexture;
                }
            }
            return null;
        }
    }
}
