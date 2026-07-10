#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

// ===========================================================================
//  Texture Packer Graph View
//  The canvas. Textures dragged from the Project window become source nodes;
//  their channel ports wire into the single Pack Output node.
// ===========================================================================

public class TexturePackerGraphView : GraphView
{
    /// <summary>The one output node. Created with the view and never deleted.</summary>
    public PackOutputNodeView OutputNode { get; }

    /// <summary>Raised whenever an edge or node changes, so the window can refresh the preview.</summary>
    public event Action GraphChanged;

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
        AddElement(OutputNode);
        OutputNode.RefreshChannelRows();
    }

    // -------------------------------------------------------------------------
    // Port compatibility — source channel out, output channel in, never same node
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Graph mutation
    // -------------------------------------------------------------------------

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

    /// <summary>Adds a source node for the given asset GUID, or returns the existing one.</summary>
    public SourceImageNodeView AddSourceNode(string textureGuid, Vector2 graphPosition)
    {
        SourceImageNodeView existingNode = FindSourceNode(textureGuid);
        if (existingNode != null)
        {
            return existingNode;
        }

        SourceImageNodeView sourceNode = SourceImageNodeView.CreateFromGuid(textureGuid);
        sourceNode.SetPosition(new Rect(graphPosition, Vector2.zero));
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

    /// <summary>Clears every source node and edge, leaving the output node in place.</summary>
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
            RemoveElement(sourceNode);
        }

        OutputNode.RefreshChannelRows();
    }

    /// <summary>Creates the edge for a saved wire. Both ports must already exist.</summary>
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

    // -------------------------------------------------------------------------
    // Drag textures in from the Project window
    // -------------------------------------------------------------------------

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
            string assetPath = AssetDatabase.GetAssetPath(draggedTexture);
            if (string.IsNullOrEmpty(assetPath))
            {
                continue;
            }

            string textureGuid = AssetDatabase.AssetPathToGUID(assetPath);
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
}
#endif
