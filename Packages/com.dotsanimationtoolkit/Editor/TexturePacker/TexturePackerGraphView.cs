// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The pack graph canvas: source image nodes wire their channels into the single output
    /// node, and this view also translates the canvas to and from a recipe asset.
    /// </summary>
    public sealed class TexturePackerGraphView : GraphView
    {
        public PackOutputNodeView OutputNode { get; }

        public event Action GraphChanged;

        // Re-raised from every source node so the panel subscribes once.
        public event Action<SourceImageNodeView, int> SourceChannelViewChanged;

        public TexturePackerGraphView()
        {
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            GridBackground grid = new GridBackground();
            grid.StretchToParentSize();
            Insert(0, grid);

            style.backgroundColor = new Color(0.13f, 0.13f, 0.13f);
            this.StretchToParentSize();

            graphViewChanged = OnGraphViewChanged;

            RegisterCallback<DragUpdatedEvent>(OnTextureDragUpdated);
            RegisterCallback<DragPerformEvent>(OnTextureDragPerform);

            OutputNode = new PackOutputNodeView();
            OutputNode.SetPosition(new Rect(new Vector2(620f, 180f), Vector2.zero));
            OutputNode.SourceDroppedOnChannel += (channelIndex, droppedTextures) => AddSourcesWiredIntoChannel(channelIndex, droppedTextures);
            AddElement(OutputNode);
            OutputNode.RefreshChannelRows();
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            List<Port> compatiblePorts = new List<Port>();
            ports.ForEach(candidatePort =>
            {
                if (candidatePort.node == startPort.node)
                {
                    return;
                }
                if (candidatePort.direction == startPort.direction)
                {
                    return;
                }
                compatiblePorts.Add(candidatePort);
            });
            return compatiblePorts;
        }

        // AddSourceNode is the one place nodes are made, so a recipe load and a drop behave alike.
        public SourceImageNodeView AddSourceNode(string textureGuid, Vector2 graphPosition)
        {
            SourceImageNodeView existingNode = FindSourceNode(textureGuid);
            if (existingNode != null)
            {
                return existingNode;
            }

            SourceImageNodeView sourceNode = SourceImageNodeView.CreateFromGuid(textureGuid);
            sourceNode.SetPosition(new Rect(graphPosition, Vector2.zero));
            sourceNode.ReplaceRequested += (node, replacementTexture) => ReplaceSourceTexture(node, replacementTexture);
            sourceNode.ChannelViewChanged += (node, channelIndex) => SourceChannelViewChanged?.Invoke(node, channelIndex);
            AddElement(sourceNode);
            return sourceNode;
        }

        public SourceImageNodeView FindSourceNode(string textureGuid)
        {
            return EnumerateSourceNodes().FirstOrDefault(sourceNode => sourceNode.TextureGuid == textureGuid);
        }

        public IEnumerable<SourceImageNodeView> EnumerateSourceNodes()
        {
            return nodes.ToList().OfType<SourceImageNodeView>();
        }

        public void ClearSources()
        {
            // The output node outlives this call, so its ports must be told the edges are
            // gone — RemoveElement alone leaves them reporting themselves as connected.
            foreach (Edge edge in edges.ToList())
            {
                edge.output?.Disconnect(edge);
                edge.input?.Disconnect(edge);
                RemoveElement(edge);
            }

            foreach (SourceImageNodeView sourceNode in EnumerateSourceNodes().ToList())
            {
                sourceNode.DisposeChannelPreview();
                RemoveElement(sourceNode);
            }

            OutputNode.RefreshChannelRows();
        }

        public void ConnectPorts(Port outputPort, Port inputPort)
        {
            if (outputPort == null || inputPort == null)
            {
                return;
            }
            Edge edge = outputPort.ConnectTo(inputPort);
            AddElement(edge);
        }

        public Vector2 GetVisibleCenter()
        {
            Rect viewportRect = layout;
            Vector2 screenCenter = new Vector2(viewportRect.width * 0.5f, viewportRect.height * 0.5f);
            return contentViewContainer.WorldToLocal(this.LocalToWorld(screenCenter));
        }

        public void AddSourcesAtVisibleCenter(IReadOnlyList<Texture2D> textures)
        {
            if (textures == null || textures.Count == 0)
            {
                return;
            }

            List<string> existingGuids = CollectSourceGuids();
            Vector2 spawnPosition = GetVisibleCenter();
            bool anyAdded = false;

            foreach (Texture2D texture in textures)
            {
                string textureGuid = GuidForTexture(texture);
                if (string.IsNullOrEmpty(textureGuid) || existingGuids.Contains(textureGuid))
                {
                    continue;
                }

                AddSourceNode(textureGuid, spawnPosition);
                existingGuids.Add(textureGuid);
                spawnPosition += new Vector2(30f, 30f);
                anyAdded = true;
            }

            if (anyAdded)
            {
                GraphChanged?.Invoke();
            }
        }

        public void AddSourcesWiredIntoChannel(int outputChannelIndex, IReadOnlyList<Texture2D> textures)
        {
            if (textures == null || textures.Count == 0)
            {
                return;
            }

            List<string> existingGuids = CollectSourceGuids();
            Vector2 spawnPosition = OutputNode.GetPosition().position - new Vector2(320f, 0f);
            bool anyAdded = false;
            bool firstAddedSourceWired = false;

            foreach (Texture2D texture in textures)
            {
                string textureGuid = GuidForTexture(texture);
                if (string.IsNullOrEmpty(textureGuid) || existingGuids.Contains(textureGuid))
                {
                    continue;
                }

                SourceImageNodeView sourceNode = AddSourceNode(textureGuid, spawnPosition);
                existingGuids.Add(textureGuid);
                anyAdded = true;

                if (!firstAddedSourceWired)
                {
                    // ConnectPorts bypasses graphViewChanged, so the single-capacity replacement
                    // that runs for a user-drawn edge has to be done by hand here.
                    DisconnectExistingEdges(OutputNode.GetChannelPort(outputChannelIndex), null);
                    ConnectPorts(sourceNode.GetChannelPort(PackChannelIndex.Red), OutputNode.GetChannelPort(outputChannelIndex));
                    firstAddedSourceWired = true;
                }

                spawnPosition += new Vector2(0f, 30f);
            }

            if (anyAdded)
            {
                GraphChanged?.Invoke();
                // port.connected is stale until the next frame.
                schedule.Execute(() => OutputNode.RefreshChannelRows()).ExecuteLater(0);
            }
        }

        public List<string> CollectSourceGuids()
        {
            return EnumerateSourceNodes().Select(sourceNode => sourceNode.TextureGuid).ToList();
        }

        public bool ReplaceSourceTexture(SourceImageNodeView node, Texture2D replacement)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(replacement, out string replacementGuid, out long _))
            {
                Debug.LogWarning("[DOTS Animation Toolkit] Texture Packer: dropped texture has no asset GUID.");
                return false;
            }

            SourceImageNodeView nodeAlreadyHoldingGuid = FindSourceNode(replacementGuid);
            if (nodeAlreadyHoldingGuid != null && nodeAlreadyHoldingGuid != node)
            {
                Debug.LogWarning("[DOTS Animation Toolkit] Texture Packer: that texture is already on another node.");
                return false;
            }

            node.ReplaceTexture(replacementGuid, replacement);
            GraphChanged?.Invoke();
            return true;
        }

        public void LoadFromRecipe(TexturePackRecipeAsset recipe)
        {
            ClearSources();

            if (recipe.channels == null || recipe.channels.Length != PackChannelIndex.Count)
            {
                recipe.channels = TexturePackRecipeAsset.CreateDefaultChannels();
            }

            // Layout entries place their nodes; a wired source with no saved layout still gets one.
            Dictionary<string, Vector2> layoutByGuid = new Dictionary<string, Vector2>();
            foreach (SourceNodeLayout layout in recipe.sourceLayouts)
            {
                if (!string.IsNullOrEmpty(layout.sourceTextureGuid))
                {
                    layoutByGuid[layout.sourceTextureGuid] = layout.position;
                }
            }

            Vector2 fallbackPosition = new Vector2(120f, 120f);
            foreach (KeyValuePair<string, Vector2> layoutEntry in layoutByGuid)
            {
                AddSourceNode(layoutEntry.Key, layoutEntry.Value);
            }

            foreach (PackChannel channel in recipe.channels)
            {
                if (!channel.IsWired || layoutByGuid.ContainsKey(channel.sourceTextureGuid))
                {
                    continue;
                }
                AddSourceNode(channel.sourceTextureGuid, fallbackPosition);
                fallbackPosition += new Vector2(30f, 30f);
            }

            OutputNode.SetPosition(new Rect(recipe.outputNodePosition, Vector2.zero));
            OutputNode.Resolution = recipe.resolution;

            for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
            {
                PackChannel channel = recipe.channels[channelIndex];
                OutputNode.SetInvert(channelIndex, channel.invert);
                OutputNode.SetDefaultValue(channelIndex, channel.defaultValue);

                if (!channel.IsWired || channel.sourceChannel >= PackChannelIndex.Count)
                {
                    continue;
                }

                SourceImageNodeView sourceNode = FindSourceNode(channel.sourceTextureGuid);
                if (sourceNode == null)
                {
                    continue;
                }

                ConnectPorts(
                    sourceNode.GetChannelPort(channel.sourceChannel),
                    OutputNode.GetChannelPort(channelIndex));
            }

            OutputNode.SetOutputPathLabel(recipe.outputAssetPath);
            OutputNode.RefreshChannelRows();
            GraphChanged?.Invoke();
        }

        public void WriteToRecipe(TexturePackRecipeAsset recipe)
        {
            recipe.channels = new PackChannel[PackChannelIndex.Count];
            for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
            {
                PackChannelBinding channelBinding = BuildChannelBinding(channelIndex);
                recipe.channels[channelIndex] = new PackChannel
                {
                    sourceTextureGuid = channelBinding.IsWired ? AssetDatabase.AssetPathToGUID(channelBinding.sourceAssetPath) : string.Empty,
                    sourceChannel = channelBinding.IsWired ? channelBinding.sourceChannel : -1,
                    invert = channelBinding.invert,
                    defaultValue = channelBinding.defaultValue
                };
            }

            recipe.resolution = OutputNode.Resolution;
            recipe.outputNodePosition = OutputNode.GetPosition().position;

            recipe.sourceLayouts = new List<SourceNodeLayout>();
            foreach (SourceImageNodeView sourceNode in EnumerateSourceNodes())
            {
                recipe.sourceLayouts.Add(new SourceNodeLayout
                {
                    sourceTextureGuid = sourceNode.TextureGuid,
                    position = sourceNode.GetPosition().position
                });
            }
        }

        public PackRequest BuildPackRequest(string outputAssetPath)
        {
            PackRequest request = new PackRequest
            {
                channels = new PackChannelBinding[PackChannelIndex.Count],
                resolution = OutputNode.Resolution,
                outputAssetPath = outputAssetPath
            };

            for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
            {
                request.channels[channelIndex] = BuildChannelBinding(channelIndex);
            }

            return request;
        }

        private PackChannelBinding BuildChannelBinding(int channelIndex)
        {
            PackChannelBinding channelBinding = new PackChannelBinding
            {
                sourceAssetPath = string.Empty,
                sourceChannel = -1,
                invert = OutputNode.GetInvert(channelIndex),
                defaultValue = OutputNode.GetDefaultValue(channelIndex)
            };

            Edge wire = OutputNode.GetChannelPort(channelIndex).connections.FirstOrDefault();
            if (wire?.output?.node is not SourceImageNodeView sourceNode || sourceNode.IsMissing)
            {
                return channelBinding;
            }

            int sourceChannel = sourceNode.FindChannelIndex(wire.output);
            string sourceAssetPath = AssetDatabase.GUIDToAssetPath(sourceNode.TextureGuid);
            if (sourceChannel < 0 || string.IsNullOrEmpty(sourceAssetPath))
            {
                return channelBinding;
            }

            channelBinding.sourceAssetPath = sourceAssetPath;
            channelBinding.sourceChannel = sourceChannel;
            return channelBinding;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            // A single-capacity input silently accepts a second edge unless the old one
            // is torn down first, which would leave two edges claiming the same channel.
            if (change.edgesToCreate != null)
            {
                foreach (Edge createdEdge in change.edgesToCreate)
                {
                    DisconnectExistingEdges(createdEdge.input, createdEdge);
                }
            }

            if (change.elementsToRemove != null)
            {
                foreach (SourceImageNodeView removedSourceNode in change.elementsToRemove.OfType<SourceImageNodeView>())
                {
                    removedSourceNode.DisposeChannelPreview();
                }
            }

            bool graphMutated = (change.edgesToCreate != null && change.edgesToCreate.Count > 0) ||
                                (change.elementsToRemove != null && change.elementsToRemove.Count > 0);

            if (graphMutated)
            {
                // The rows read port.connected, which is only correct once the removals land.
                schedule.Execute(() =>
                {
                    OutputNode.RefreshChannelRows();
                    GraphChanged?.Invoke();
                }).ExecuteLater(0);
            }

            return change;
        }

        private void DisconnectExistingEdges(Port inputPort, Edge edgeBeingCreated)
        {
            if (inputPort == null || inputPort.capacity != Port.Capacity.Single)
            {
                return;
            }

            List<Edge> staleEdges = inputPort.connections.Where(existingEdge => existingEdge != edgeBeingCreated).ToList();
            foreach (Edge staleEdge in staleEdges)
            {
                staleEdge.output?.Disconnect(staleEdge);
                staleEdge.input?.Disconnect(staleEdge);
                RemoveElement(staleEdge);
            }
        }

        private void OnTextureDragUpdated(DragUpdatedEvent dragEvent)
        {
            if (DraggedTextures().Any())
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
        }

        private void OnTextureDragPerform(DragPerformEvent dragEvent)
        {
            List<Texture2D> draggedTextures = DraggedTextures().ToList();
            if (draggedTextures.Count == 0)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            Vector2 dropPosition = contentViewContainer.WorldToLocal(this.LocalToWorld(dragEvent.localMousePosition));

            foreach (Texture2D draggedTexture in draggedTextures)
            {
                string textureGuid = GuidForTexture(draggedTexture);
                if (string.IsNullOrEmpty(textureGuid))
                {
                    continue;
                }

                AddSourceNode(textureGuid, dropPosition);
                dropPosition += new Vector2(30f, 30f);
            }

            GraphChanged?.Invoke();
        }

        private static IEnumerable<Texture2D> DraggedTextures()
        {
            if (DragAndDrop.objectReferences == null)
            {
                return Enumerable.Empty<Texture2D>();
            }
            return DragAndDrop.objectReferences.OfType<Texture2D>();
        }

        private static string GuidForTexture(Texture2D texture)
        {
            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }
            return AssetDatabase.AssetPathToGUID(assetPath);
        }
    }
}
