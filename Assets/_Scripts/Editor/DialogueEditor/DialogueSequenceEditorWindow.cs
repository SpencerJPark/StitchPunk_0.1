#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

// ===========================================================================
//  Dialogue Sequence Editor Window
//  Open via: Window > Stitch Punk > Dialogue Editor
//
//  Layout:
//    LEFT  (200 px) — searchable list of all DialogueSequenceSO assets
//    CENTER (flex)  — node graph canvas (pan with middle-mouse, zoom with scroll)
//    RIGHT  (180 px) — node palette: click to drop a new node onto the canvas
//
//  Connecting nodes:
//    Left-click and drag from any output port (right edge) to any input port (left edge).
//    Release over a compatible port to create the connection.
//    Click a connection line and press Delete to remove it.
//
//  Saving:
//    Changes to node fields are written to the SO immediately (Undo-safe).
//    Click "Save" in the toolbar or use Ctrl+S to flush assets to disk.
// ===========================================================================

public class DialogueSequenceEditorWindow : EditorWindow
{
    // -------------------------------------------------------------------------
    // Static open
    // -------------------------------------------------------------------------

    [MenuItem("Window/Stitch Punk/Dialogue Editor")]
    public static void OpenWindow()
    {
        DialogueSequenceEditorWindow window = GetWindow<DialogueSequenceEditorWindow>("Dialogue Editor");
        window.minSize = new Vector2(900f, 600f);
    }

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private DialogueSequenceSO loadedSequence;
    private DialogueGraphView graphView;

    // Left panel
    private VisualElement leftPanel;
    private ScrollView assetScrollView;
    private TextField searchField;
    private List<DialogueSequenceSO> allSequences   = new List<DialogueSequenceSO>();
    private List<DialogueSequenceSO> shownSequences = new List<DialogueSequenceSO>();

    // -------------------------------------------------------------------------
    // CreateGUI — builds the three-panel layout
    // -------------------------------------------------------------------------

    private void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.style.flexDirection = FlexDirection.Row;
        root.style.flexGrow = 1;

        // LEFT: asset list
        leftPanel = BuildLeftPanel();
        root.Add(leftPanel);

        // Divider
        root.Add(BuildDivider());

        // CENTER: toolbar + graph view stacked vertically
        VisualElement centerColumn = new VisualElement();
        centerColumn.style.flexGrow = 1;
        centerColumn.style.flexDirection = FlexDirection.Column;
        centerColumn.Add(BuildToolbar());

        graphView = new DialogueGraphView(this);
        graphView.style.flexGrow = 1;
        centerColumn.Add(graphView);
        root.Add(centerColumn);

        // Divider
        root.Add(BuildDivider());

        // RIGHT: palette
        root.Add(BuildRightPanel());

        RefreshAssetList();

        // Allow Ctrl+S to save
        root.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.S && evt.ctrlKey)
            {
                SaveCurrentSequence();
                evt.StopPropagation();
            }
        });
    }

    // -------------------------------------------------------------------------
    // Left panel — searchable asset list
    // -------------------------------------------------------------------------

    private VisualElement BuildLeftPanel()
    {
        VisualElement panel = new VisualElement();
        panel.style.width = 200;
        panel.style.minWidth = 200;
        panel.style.maxWidth = 200;
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.backgroundColor = new Color(0.17f, 0.17f, 0.17f);

        // Header
        Label header = new Label("Dialogue Trees");
        header.style.paddingLeft = 8;
        header.style.paddingTop = 7;
        header.style.paddingBottom = 5;
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.color = new Color(0.85f, 0.85f, 0.85f);
        header.tooltip = "All DialogueSequenceSO assets found in the project, sorted by sequenceId.";
        panel.Add(header);

        // Search field
        searchField = new TextField();
        searchField.style.marginLeft = 6;
        searchField.style.marginRight = 6;
        searchField.style.marginBottom = 4;
        searchField.tooltip = "Filter the list by asset name or sequence ID number.";
        searchField.RegisterValueChangedCallback(evt => FilterAndRebuildList(evt.newValue));
        panel.Add(searchField);

        // Thin separator
        panel.Add(BuildDivider());

        // Scrollable list area
        assetScrollView = new ScrollView(ScrollViewMode.Vertical);
        assetScrollView.style.flexGrow = 1;
        panel.Add(assetScrollView);

        // Bottom: Refresh button
        Button refreshBtn = new Button(RefreshAssetList);
        refreshBtn.text = "Refresh List";
        refreshBtn.style.marginLeft = 6;
        refreshBtn.style.marginRight = 6;
        refreshBtn.style.marginTop = 4;
        refreshBtn.style.marginBottom = 6;
        refreshBtn.tooltip = "Re-scan the entire project for DialogueSequenceSO assets and rebuild this list.";
        panel.Add(refreshBtn);

        return panel;
    }

    private void RefreshAssetList()
    {
        allSequences.Clear();
        string[] guids = AssetDatabase.FindAssets("t:DialogueSequenceSO");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            DialogueSequenceSO so = AssetDatabase.LoadAssetAtPath<DialogueSequenceSO>(path);
            if (so != null) allSequences.Add(so);
        }
        allSequences.Sort((seqA, seqB) => seqA.sequenceId.CompareTo(seqB.sequenceId));
        FilterAndRebuildList(searchField?.value ?? string.Empty);
    }

    private void FilterAndRebuildList(string filter)
    {
        shownSequences.Clear();
        string lower = filter.ToLowerInvariant();
        foreach (DialogueSequenceSO so in allSequences)
        {
            if (string.IsNullOrEmpty(filter)
                || so.name.ToLowerInvariant().Contains(lower)
                || so.sequenceId.ToString().Contains(lower))
            {
                shownSequences.Add(so);
            }
        }
        RebuildListUI();
    }

    private void RebuildListUI()
    {
        if (assetScrollView == null) return;
        assetScrollView.Clear();

        foreach (DialogueSequenceSO so in shownSequences)
        {
            DialogueSequenceSO captured = so;

            Button row = new Button(() => LoadSequence(captured));
            row.style.marginLeft = 0;
            row.style.marginRight = 0;
            row.style.marginTop = 0;
            row.style.marginBottom = 1;
            row.style.height = 30;
            row.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 4;
            row.style.borderTopWidth = 0;
            row.style.borderBottomWidth = 0;
            row.style.borderLeftWidth = 0;
            row.style.borderRightWidth = 0;

            row.text = $"[{so.sequenceId}]  {so.name}";
            row.tooltip = $"Sequence ID: {so.sequenceId}\nPath: {AssetDatabase.GetAssetPath(so)}";

            // Highlight currently loaded sequence
            if (so == loadedSequence)
            {
                row.style.backgroundColor = new Color(0.22f, 0.36f, 0.58f);
                row.style.color = Color.white;
            }
            else
            {
                row.style.backgroundColor = new Color(0.20f, 0.20f, 0.20f);
                row.style.color = new Color(0.80f, 0.80f, 0.80f);
            }

            assetScrollView.Add(row);
        }
    }

    // -------------------------------------------------------------------------
    // Center toolbar
    // -------------------------------------------------------------------------

    private VisualElement BuildToolbar()
    {
        VisualElement toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.height = 32;
        toolbar.style.minHeight = 32;
        toolbar.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        toolbar.style.paddingLeft = 10;
        toolbar.style.paddingRight = 8;
        toolbar.style.alignItems = Align.Center;
        toolbar.style.borderBottomColor = new Color(0.08f, 0.08f, 0.08f);
        toolbar.style.borderBottomWidth = 1;

        Label title = new Label("Dialogue Editor");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new Color(0.90f, 0.90f, 0.90f);
        title.style.flexGrow = 1;
        toolbar.Add(title);

        Button saveBtn = new Button(SaveCurrentSequence);
        saveBtn.text = "Save";
        saveBtn.style.width = 64;
        saveBtn.tooltip = "Save all pending changes to the currently loaded dialogue sequence asset. " +
                          "Shortcut: Ctrl+S";
        toolbar.Add(saveBtn);

        return toolbar;
    }

    // -------------------------------------------------------------------------
    // Right panel — node palette
    // -------------------------------------------------------------------------

    private VisualElement BuildRightPanel()
    {
        VisualElement panel = new VisualElement();
        panel.style.width = 180;
        panel.style.minWidth = 180;
        panel.style.maxWidth = 180;
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.backgroundColor = new Color(0.17f, 0.17f, 0.17f);
        panel.style.paddingLeft = 8;
        panel.style.paddingRight = 8;
        panel.style.paddingTop = 10;

        Label header = new Label("Add Node");
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.color = new Color(0.85f, 0.85f, 0.85f);
        header.style.marginBottom = 10;
        header.tooltip = "Click any button to drop a new node onto the center of the canvas. " +
                         "A sequence must be loaded from the left panel first.";
        panel.Add(header);

        panel.Add(BuildPaletteButton(
            "Start",
            new Color(0.12f, 0.46f, 0.12f),
            "Entry point for the dialogue sequence.\n\n" +
            "Every tree needs exactly one Start node. " +
            "Connect its output port to the first Line, Decision, or Event node.",
            () => DropNode(new DialogueStartNodeData())));

        panel.Add(BuildPaletteButton(
            "Line",
            new Color(0.12f, 0.26f, 0.50f),
            "A single line of dialogue.\n\n" +
            "Enter the speaker name (shown above the subtitle) and the subtitle text. " +
            "The player presses Interact to advance to the next node.",
            () => DropNode(new DialogueLineNodeData())));

        panel.Add(BuildPaletteButton(
            "Decision",
            new Color(0.50f, 0.26f, 0.07f),
            "Presents the player with a set of choices.\n\n" +
            "Add branches using the '+ Add Branch' button that appears on the node. " +
            "Each branch becomes a labeled choice button and a separate output port. " +
            "Connect each port to the next step for that choice.",
            () => DropNode(new DialogueDecisionNodeData())));

        panel.Add(BuildPaletteButton(
            "Event",
            new Color(0.34f, 0.12f, 0.50f),
            "Fires a game event by ID and continues automatically.\n\n" +
            "The player does not see this node — it is invisible to them. " +
            "Use it to trigger camera changes, unlock systems, or update game data mid-dialogue. " +
            "The Event ID must match a constant in DialogueIds.Events.",
            () => DropNode(new DialogueEventNodeData())));

        panel.Add(BuildPaletteButton(
            "End",
            new Color(0.50f, 0.12f, 0.12f),
            "Exit point for the dialogue sequence.\n\n" +
            "Connect any terminal node's output here to close the conversation. " +
            "A tree can have multiple End nodes — one per path that should end the dialogue.",
            () => DropNode(new DialogueEndNodeData())));

        return panel;
    }

    private VisualElement BuildPaletteButton(string label, Color headerColor, string tooltip, Action onClick)
    {
        Button btn = new Button(onClick);
        btn.text = label;
        btn.tooltip = tooltip;
        btn.style.marginBottom = 8;
        btn.style.height = 40;
        btn.style.backgroundColor = headerColor;
        btn.style.color = Color.white;
        btn.style.unityFontStyleAndWeight = FontStyle.Bold;
        btn.style.fontSize = 12;
        btn.style.borderTopLeftRadius = 4;
        btn.style.borderTopRightRadius = 4;
        btn.style.borderBottomLeftRadius = 4;
        btn.style.borderBottomRightRadius = 4;
        btn.style.borderTopWidth = 0;
        btn.style.borderBottomWidth = 0;
        btn.style.borderLeftWidth = 0;
        btn.style.borderRightWidth = 0;
        return btn;
    }

    // -------------------------------------------------------------------------
    // Load / save / drop
    // -------------------------------------------------------------------------

    public void LoadSequence(DialogueSequenceSO sequence)
    {
        loadedSequence = sequence;
        graphView?.LoadSequence(sequence);
        RebuildListUI(); // Refresh highlight
    }

    private void SaveCurrentSequence()
    {
        if (loadedSequence == null) return;
        EditorUtility.SetDirty(loadedSequence);
        AssetDatabase.SaveAssets();
    }

    private void DropNode(DialogueNodeData nodeData)
    {
        if (loadedSequence == null)
        {
            Debug.LogWarning("Dialogue Editor: Select a sequence from the left panel before adding nodes.");
            return;
        }

        // Place the new node at the current center of the visible canvas.
        nodeData.editorPosition = graphView?.GetVisibleCenter() ?? Vector2.zero;

        Undo.RecordObject(loadedSequence, "Add Dialogue Node");
        loadedSequence.nodes.Add(nodeData);
        EditorUtility.SetDirty(loadedSequence);

        graphView?.AddNodeView(nodeData);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static VisualElement BuildDivider()
    {
        VisualElement div = new VisualElement();
        div.style.width = 1;
        div.style.backgroundColor = new Color(0.09f, 0.09f, 0.09f);
        return div;
    }
}

// ===========================================================================
//  DialogueGraphView
//  The center panel — handles node creation, connection dragging, and deletion.
// ===========================================================================

public class DialogueGraphView : GraphView
{
    private DialogueSequenceSO loadedSequence;
    private readonly DialogueSequenceEditorWindow ownerWindow;

    public DialogueGraphView(DialogueSequenceEditorWindow window)
    {
        ownerWindow = window;

        // Enable pan (middle-mouse or Alt+drag), zoom (scroll), box-select, drag-select.
        SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        // Grid background (dark dots, matches Shader Graph aesthetic)
        GridBackground grid = new GridBackground();
        grid.StretchToParentSize();
        Insert(0, grid);

        // Slightly darker background behind the grid
        style.backgroundColor = new Color(0.13f, 0.13f, 0.13f);

        this.StretchToParentSize();

        // Respond to edge creation, edge deletion, node deletion, and node movement.
        graphViewChanged = OnGraphViewChanged;
    }

    // -------------------------------------------------------------------------
    // Port compatibility — only output→input connections are allowed
    // -------------------------------------------------------------------------

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
    {
        List<Port> compatible = new List<Port>();
        ports.ForEach(port =>
        {
            if (port.node == startPort.node) return;      // same node
            if (port.direction == startPort.direction) return; // same direction
            compatible.Add(port);
        });
        return compatible;
    }

    // -------------------------------------------------------------------------
    // Visible center in graph-space (used to place freshly dropped nodes)
    // -------------------------------------------------------------------------

    public Vector2 GetVisibleCenter()
    {
        Rect viewportRect = this.layout;
        Vector2 screenCenter = new Vector2(viewportRect.width * 0.5f, viewportRect.height * 0.5f);
        return contentViewContainer.WorldToLocal(this.LocalToWorld(screenCenter));
    }

    // -------------------------------------------------------------------------
    // Load sequence — rebuild all nodes and edges from SO data
    // -------------------------------------------------------------------------

    public void LoadSequence(DialogueSequenceSO sequence)
    {
        loadedSequence = sequence;
        DeleteElements(graphElements.ToList());

        if (sequence == null) return;

        // Create node views
        Dictionary<string, DialogueNodeViewBase> nodeViewMap = new Dictionary<string, DialogueNodeViewBase>();
        foreach (DialogueNodeData nodeData in sequence.nodes)
        {
            DialogueNodeViewBase view = CreateNodeView(nodeData, sequence);
            if (view == null) continue;
            AddElement(view);
            nodeViewMap[nodeData.nodeId] = view;
        }

        // Create edges from connection data
        foreach (DialogueConnectionData conn in sequence.connections)
        {
            if (!nodeViewMap.TryGetValue(conn.fromNodeId, out DialogueNodeViewBase fromView)) continue;
            if (!nodeViewMap.TryGetValue(conn.toNodeId,   out DialogueNodeViewBase toView))   continue;

            Port outputPort = fromView.FindOutputPort(conn.fromPortId);
            Port inputPort  = toView.FindInputPort(conn.toPortId);
            if (outputPort == null || inputPort == null) continue;

            Edge edge = outputPort.ConnectTo(inputPort);
            AddElement(edge);
        }
    }

    // -------------------------------------------------------------------------
    // Add a single node view (called when palette button drops a new node)
    // -------------------------------------------------------------------------

    public void AddNodeView(DialogueNodeData nodeData)
    {
        DialogueNodeViewBase view = CreateNodeView(nodeData, loadedSequence);
        if (view != null) AddElement(view);
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    private DialogueNodeViewBase CreateNodeView(DialogueNodeData nodeData, DialogueSequenceSO sequence)
    {
        DialogueNodeViewBase view = nodeData switch
        {
            DialogueStartNodeData    d => new DialogueStartNodeView(d, sequence, this),
            DialogueEndNodeData      d => new DialogueEndNodeView(d, sequence, this),
            DialogueLineNodeData     d => new DialogueLineNodeView(d, sequence, this),
            DialogueDecisionNodeData d => new DialogueDecisionNodeView(d, sequence, this),
            DialogueEventNodeData    d => new DialogueEventNodeView(d, sequence, this),
            _                          => null
        };

        if (view != null)
            view.SetPosition(new Rect(nodeData.editorPosition, Vector2.zero));

        return view;
    }

    // -------------------------------------------------------------------------
    // GraphViewChanged — sync SO with all edge and node changes
    // -------------------------------------------------------------------------

    private GraphViewChange OnGraphViewChanged(GraphViewChange change)
    {
        if (loadedSequence == null) return change;

        bool dirty = false;

        // Removed elements (edges and nodes)
        if (change.elementsToRemove != null)
        {
            Undo.RecordObject(loadedSequence, "Remove Dialogue Element");
            foreach (GraphElement element in change.elementsToRemove)
            {
                if (element is Edge edge)
                {
                    RemoveConnectionFromSO(edge);
                    dirty = true;
                }
                else if (element is DialogueNodeViewBase nodeView)
                {
                    loadedSequence.nodes.Remove(nodeView.NodeData);
                    loadedSequence.connections.RemoveAll(conn =>
                        conn.fromNodeId == nodeView.NodeData.nodeId ||
                        conn.toNodeId   == nodeView.NodeData.nodeId);
                    dirty = true;
                }
            }
        }

        // New edges created by drag-connect
        if (change.edgesToCreate != null)
        {
            Undo.RecordObject(loadedSequence, "Connect Dialogue Nodes");
            foreach (Edge edge in change.edgesToCreate)
            {
                AddConnectionToSO(edge);
                dirty = true;
            }
        }

        // Nodes moved — save new positions
        if (change.movedElements != null)
        {
            Undo.RecordObject(loadedSequence, "Move Dialogue Node");
            foreach (GraphElement element in change.movedElements)
            {
                if (element is DialogueNodeViewBase nodeView)
                {
                    nodeView.NodeData.editorPosition = element.GetPosition().position;
                    dirty = true;
                }
            }
        }

        if (dirty) EditorUtility.SetDirty(loadedSequence);
        return change;
    }

    // -------------------------------------------------------------------------
    // SO connection helpers
    // -------------------------------------------------------------------------

    private void AddConnectionToSO(Edge edge)
    {
        if (edge.output?.node is not DialogueNodeViewBase fromView) return;
        if (edge.input?.node  is not DialogueNodeViewBase toView)   return;

        loadedSequence.connections.Add(new DialogueConnectionData
        {
            fromNodeId = fromView.NodeData.nodeId,
            fromPortId = edge.output.portName,
            toNodeId   = toView.NodeData.nodeId,
            toPortId   = edge.input.portName
        });
    }

    private void RemoveConnectionFromSO(Edge edge)
    {
        if (edge.output?.node is not DialogueNodeViewBase fromView) return;
        if (edge.input?.node  is not DialogueNodeViewBase toView)   return;

        string fromPort = edge.output.portName;
        string toPort   = edge.input.portName;

        loadedSequence.connections.RemoveAll(conn =>
            conn.fromNodeId == fromView.NodeData.nodeId &&
            conn.fromPortId == fromPort &&
            conn.toNodeId   == toView.NodeData.nodeId &&
            conn.toPortId   == toPort);
    }

    // Exposed so Decision node can remove edges for a deleted branch.
    public void DeleteEdgesForPort(Port port)
    {
        List<Edge> edgesToRemove = port.connections.ToList();
        DeleteElements(edgesToRemove);
    }
}

// ===========================================================================
//  Base node view — shared setup for all five node types
// ===========================================================================

public abstract partial class DialogueNodeViewBase : Node
{
    public DialogueNodeData NodeData { get; protected set; }
    protected DialogueSequenceSO Sequence;
    protected DialogueGraphView GraphViewRef;

    // Every node has at most one "in" input port and/or one "out" output port.
    // Decision nodes override FindOutputPort for their multi-branch ports.
    protected Port StandardInputPort;
    protected Port StandardOutputPort;

    public virtual Port FindOutputPort(string portId) =>
        StandardOutputPort?.portName == portId ? StandardOutputPort : null;

    public virtual Port FindInputPort(string portId) =>
        StandardInputPort?.portName == portId ? StandardInputPort : null;

    // Convenience factory for a typed port.
    protected Port MakePort(UnityEditor.Experimental.GraphView.Direction direction, Port.Capacity capacity, string portName, Color portColor)
    {
        Port port = Port.Create<Edge>(Orientation.Horizontal, direction, capacity, typeof(bool));
        port.portName  = portName;
        port.portColor = portColor;
        return port;
    }

    // Applies a background color to the title bar that Unity generates for each Node.
    protected void SetHeaderColor(Color color)
    {
        VisualElement titleBar = this.Q("title");
        if (titleBar != null) titleBar.style.backgroundColor = color;
    }

    protected void MarkDirty()
    {
        if (Sequence != null) EditorUtility.SetDirty(Sequence);
    }
}

// ===========================================================================
//  Start node  (green)
// ===========================================================================

public class DialogueStartNodeView : DialogueNodeViewBase
{
    private static readonly Color HEADER = new Color(0.12f, 0.46f, 0.12f);

    public DialogueStartNodeView(DialogueStartNodeData data,
                                 DialogueSequenceSO sequence,
                                 DialogueGraphView graphView)
    {
        NodeData      = data;
        Sequence      = sequence;
        GraphViewRef  = graphView;

        title   = "Start";
        tooltip = "Entry point for the dialogue sequence.\n\n" +
                  "Every tree must have exactly one Start node. " +
                  "Connect its output port to the first Line, Decision, or Event node.";

        SetHeaderColor(HEADER);

        // Output port only — no input port.
        StandardOutputPort = MakePort(UnityEditor.Experimental.GraphView.Direction.Output, Port.Capacity.Single, "out",
            new Color(0.40f, 0.90f, 0.40f));
        outputContainer.Add(StandardOutputPort);

        // Hide the empty input container so the node doesn't have a blank left side.
        inputContainer.RemoveFromHierarchy();

        capabilities &= ~Capabilities.Deletable; // Prevent accidental delete of Start nodes

        RefreshExpandedState();
        RefreshPorts();
    }
}

// ===========================================================================
//  End node  (red)
// ===========================================================================

public class DialogueEndNodeView : DialogueNodeViewBase
{
    private static readonly Color HEADER = new Color(0.52f, 0.12f, 0.12f);

    public DialogueEndNodeView(DialogueEndNodeData data,
                               DialogueSequenceSO sequence,
                               DialogueGraphView graphView)
    {
        NodeData     = data;
        Sequence     = sequence;
        GraphViewRef = graphView;

        title   = "End";
        tooltip = "Exit point for the dialogue sequence.\n\n" +
                  "Connect any terminal node's output port to an End node to close the conversation. " +
                  "A tree can have multiple End nodes — one per path that ends the dialogue.";

        SetHeaderColor(HEADER);

        // Input port only — no output port.
        StandardInputPort = MakePort(UnityEditor.Experimental.GraphView.Direction.Input, Port.Capacity.Multi, "in",
            new Color(0.65f, 0.65f, 1.00f));
        inputContainer.Add(StandardInputPort);

        outputContainer.RemoveFromHierarchy();

        RefreshExpandedState();
        RefreshPorts();
    }

    public override Port FindInputPort(string portId) =>
        portId == "in" ? StandardInputPort : null;
}

// ===========================================================================
//  Line node  (blue)
// ===========================================================================

public class DialogueLineNodeView : DialogueNodeViewBase
{
    private static readonly Color HEADER = new Color(0.12f, 0.25f, 0.50f);

    private readonly DialogueLineNodeData lineData;

    public DialogueLineNodeView(DialogueLineNodeData data,
                                DialogueSequenceSO sequence,
                                DialogueGraphView graphView)
    {
        lineData     = data;
        NodeData     = data;
        Sequence     = sequence;
        GraphViewRef = graphView;

        title   = "Line";
        tooltip = "Displays a subtitle line to the player.\n\n" +
                  "The player presses Interact to advance to the next connected node. " +
                  "Set Speaker Name to show a label above the text. Leave it empty to hide the label.";

        SetHeaderColor(HEADER);

        // Ports
        StandardInputPort = MakePort(UnityEditor.Experimental.GraphView.Direction.Input, Port.Capacity.Multi, "in",
            new Color(0.65f, 0.65f, 1.00f));
        inputContainer.Add(StandardInputPort);

        StandardOutputPort = MakePort(UnityEditor.Experimental.GraphView.Direction.Output, Port.Capacity.Single, "out",
            new Color(0.55f, 0.85f, 1.00f));
        outputContainer.Add(StandardOutputPort);

        // Body
        VisualElement body = BuildBody();
        extensionContainer.Add(body);

        expanded = true;
        RefreshExpandedState();
        RefreshPorts();
    }

    private VisualElement BuildBody()
    {
        VisualElement body = new VisualElement();
        body.style.paddingLeft   = 8;
        body.style.paddingRight  = 8;
        body.style.paddingTop    = 6;
        body.style.paddingBottom = 8;
        body.style.minWidth = 240;

        // Speaker Name
        body.Add(MakeFieldLabel("Speaker Name",
            "The character's name shown above the subtitle text. Leave empty to hide the speaker label."));

        TextField speakerField = new TextField();
        speakerField.value   = lineData.speakerName;
        speakerField.tooltip = "Name displayed above the subtitle. Leave empty to hide the speaker label entirely.";
        speakerField.style.marginBottom = 6;
        speakerField.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(Sequence, "Edit Speaker Name");
            lineData.speakerName = evt.newValue;
            MarkDirty();
        });
        body.Add(speakerField);

        // Dialogue Text
        body.Add(MakeFieldLabel("Dialogue Text",
            "The subtitle text displayed to the player when this line plays."));

        TextField textField = new TextField();
        textField.multiline  = true;
        textField.value      = lineData.dialogueText;
        textField.tooltip    = "The subtitle text shown to the player. The player presses Interact to advance.";
        textField.style.height      = 72;
        textField.style.whiteSpace  = WhiteSpace.Normal;
        textField.style.marginBottom = 2;
        textField.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(Sequence, "Edit Dialogue Text");
            lineData.dialogueText = evt.newValue;
            MarkDirty();
        });
        body.Add(textField);

        return body;
    }

    public override Port FindOutputPort(string portId) => portId == "out" ? StandardOutputPort : null;
    public override Port FindInputPort(string portId)  => portId == "in"  ? StandardInputPort  : null;
}

// ===========================================================================
//  Decision node  (orange)
// ===========================================================================

public class DialogueDecisionNodeView : DialogueNodeViewBase
{
    private static readonly Color HEADER = new Color(0.50f, 0.25f, 0.06f);

    private readonly DialogueDecisionNodeData decisionData;

    // Maps branch portId → the Port element for that branch output.
    private readonly Dictionary<string, Port> branchPortMap = new Dictionary<string, Port>();

    // The VisualElement that holds branch rows (inserted before the Add button).
    private VisualElement branchListContainer;

    public DialogueDecisionNodeView(DialogueDecisionNodeData data,
                                    DialogueSequenceSO sequence,
                                    DialogueGraphView graphView)
    {
        decisionData = data;
        NodeData     = data;
        Sequence     = sequence;
        GraphViewRef = graphView;

        title   = "Decision";
        tooltip = "Presents the player with a set of choice buttons.\n\n" +
                  "Each branch is one choice. Click '+ Add Branch' to add an option. " +
                  "Each branch has its own output port — connect it to the node that plays when the player picks that choice. " +
                  "Click '×' next to a branch to remove it and its connection.";

        SetHeaderColor(HEADER);

        // Single shared input port
        StandardInputPort = MakePort(UnityEditor.Experimental.GraphView.Direction.Input, Port.Capacity.Multi, "in",
            new Color(0.65f, 0.65f, 1.00f));
        inputContainer.Add(StandardInputPort);

        // Body
        VisualElement body = BuildBody();
        extensionContainer.Add(body);

        expanded = true;
        RefreshExpandedState();
        RefreshPorts();
    }

    private VisualElement BuildBody()
    {
        VisualElement body = new VisualElement();
        body.style.paddingLeft   = 8;
        body.style.paddingRight  = 8;
        body.style.paddingTop    = 6;
        body.style.paddingBottom = 8;
        body.style.minWidth = 280;

        body.Add(MakeFieldLabel("Branches  (each = one choice button + output port)",
            "Add a branch for each option the player can pick. " +
            "Each branch has its own output port on the right — connect it to the next node for that choice."));

        // Container for branch rows
        branchListContainer = new VisualElement();
        body.Add(branchListContainer);

        // Add existing branches from SO data
        foreach (DialogueDecisionBranch branch in decisionData.branches)
            BuildBranchRow(branch);

        // Add Branch button
        Button addBtn = new Button(() =>
        {
            Undo.RecordObject(Sequence, "Add Decision Branch");
            DialogueDecisionBranch newBranch = new DialogueDecisionBranch
            {
                choiceText = "New Choice",
                portId     = Guid.NewGuid().ToString()
            };
            decisionData.branches.Add(newBranch);
            BuildBranchRow(newBranch);
            MarkDirty();
            RefreshPorts();
        });
        addBtn.text    = "+ Add Branch";
        addBtn.tooltip = "Add a new choice branch. A new output port will appear on the right side of this node.";
        addBtn.style.marginTop    = 6;
        addBtn.style.marginBottom = 2;
        body.Add(addBtn);

        return body;
    }

    private void BuildBranchRow(DialogueDecisionBranch branch)
    {
        VisualElement row = new VisualElement();
        row.name                   = "branch-row-" + branch.portId;
        row.style.flexDirection    = FlexDirection.Row;
        row.style.alignItems       = Align.Center;
        row.style.marginBottom     = 4;

        // Text field for the choice label
        TextField choiceField = new TextField();
        choiceField.value   = branch.choiceText;
        choiceField.tooltip = "Text shown on the player's choice button for this option. Keep it short — typically a sentence or less.";
        choiceField.style.flexGrow     = 1;
        choiceField.style.marginRight  = 4;
        choiceField.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(Sequence, "Edit Choice Text");
            branch.choiceText = evt.newValue;
            MarkDirty();
        });
        row.Add(choiceField);

        // Output port for this branch — portName = branch.portId for connection tracking
        Port branchPort = MakePort(UnityEditor.Experimental.GraphView.Direction.Output, Port.Capacity.Single, branch.portId,
            new Color(1.00f, 0.68f, 0.25f));

        // Hide the auto-generated portName label (it would show the GUID)
        Label portLabel = branchPort.Q<Label>("type");
        if (portLabel != null) portLabel.style.display = DisplayStyle.None;

        branchPortMap[branch.portId] = branchPort;
        outputContainer.Add(branchPort); // Ports must be in port containers for proper edge routing
        row.Add(new VisualElement()); // spacer

        // Remove button
        Button removeBtn = new Button(() =>
        {
            Undo.RecordObject(Sequence, "Remove Decision Branch");

            // Remove all edges connected to this port
            if (branchPortMap.TryGetValue(branch.portId, out Port portToRemove))
            {
                GraphViewRef?.DeleteEdgesForPort(portToRemove);
                outputContainer.Remove(portToRemove);
                branchPortMap.Remove(branch.portId);
            }

            // Remove from data
            decisionData.branches.Remove(branch);
            branchListContainer.Remove(row);

            MarkDirty();
            RefreshPorts();
        });
        removeBtn.text    = "×";
        removeBtn.tooltip = "Remove this choice branch. The output port and any connection from it will also be removed.";
        removeBtn.style.width     = 22;
        removeBtn.style.height    = 22;
        removeBtn.style.fontSize  = 14;
        removeBtn.style.paddingLeft  = 0;
        removeBtn.style.paddingRight = 0;
        row.Add(removeBtn);

        branchListContainer.Add(row);
    }

    public override Port FindOutputPort(string portId)
    {
        branchPortMap.TryGetValue(portId, out Port port);
        return port;
    }

    public override Port FindInputPort(string portId) => portId == "in" ? StandardInputPort : null;
}

// ===========================================================================
//  Event node  (purple)
// ===========================================================================

public class DialogueEventNodeView : DialogueNodeViewBase
{
    private static readonly Color HEADER = new Color(0.34f, 0.11f, 0.50f);

    private readonly DialogueEventNodeData eventData;

    public DialogueEventNodeView(DialogueEventNodeData data,
                                 DialogueSequenceSO sequence,
                                 DialogueGraphView graphView)
    {
        eventData    = data;
        NodeData     = data;
        Sequence     = sequence;
        GraphViewRef = graphView;

        title   = "Event";
        tooltip = "Fires a game event by ID and continues automatically.\n\n" +
                  "The player does not see this node. Execution passes through instantly. " +
                  "The Event ID must match a constant in DialogueIds.Events (e.g., DialogueIds.Events.UnlockSomething). " +
                  "Use -1 to pass through silently with no event.";

        SetHeaderColor(HEADER);

        // Ports
        StandardInputPort = MakePort(UnityEditor.Experimental.GraphView.Direction.Input, Port.Capacity.Multi, "in",
            new Color(0.65f, 0.65f, 1.00f));
        inputContainer.Add(StandardInputPort);

        StandardOutputPort = MakePort(UnityEditor.Experimental.GraphView.Direction.Output, Port.Capacity.Single, "out",
            new Color(0.82f, 0.50f, 1.00f));
        outputContainer.Add(StandardOutputPort);

        // Body
        VisualElement body = BuildBody();
        extensionContainer.Add(body);

        expanded = true;
        RefreshExpandedState();
        RefreshPorts();
    }

    private VisualElement BuildBody()
    {
        VisualElement body = new VisualElement();
        body.style.paddingLeft   = 8;
        body.style.paddingRight  = 8;
        body.style.paddingTop    = 6;
        body.style.paddingBottom = 8;
        body.style.minWidth = 240;

        // Event ID
        body.Add(MakeFieldLabel("Event ID",
            "Must match a constant in DialogueIds.Events. Use -1 for no event (node passes through silently)."));

        IntegerField eventIdField = new IntegerField();
        eventIdField.value   = eventData.eventId;
        eventIdField.tooltip = "The event ID fired when dialogue passes through this node. " +
                               "Must match a constant in DialogueIds.Events. " +
                               "Use -1 to pass through without triggering anything.";
        eventIdField.style.marginBottom = 6;
        eventIdField.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(Sequence, "Edit Event ID");
            eventData.eventId = evt.newValue;
            MarkDirty();
        });
        body.Add(eventIdField);

        // Description (editor note only)
        body.Add(MakeFieldLabel("Description  (editor note — not used at runtime)",
            "Optional note about what this event does. Not used at runtime."));

        TextField descField = new TextField();
        descField.multiline  = true;
        descField.value      = eventData.eventDescription;
        descField.tooltip    = "Optional editor-only reminder of what this event does in the game. " +
                               "Not read at runtime — just for your own reference.";
        descField.style.height      = 40;
        descField.style.whiteSpace  = WhiteSpace.Normal;
        descField.style.marginBottom = 2;
        descField.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(Sequence, "Edit Event Description");
            eventData.eventDescription = evt.newValue;
            MarkDirty();
        });
        body.Add(descField);

        return body;
    }

    public override Port FindOutputPort(string portId) => portId == "out" ? StandardOutputPort : null;
    public override Port FindInputPort(string portId)  => portId == "in"  ? StandardInputPort  : null;
}

// ===========================================================================
//  Shared UI helpers  (accessible to all node view classes)
// ===========================================================================

public static class DialogueNodeUIHelpers
{
    /// <summary>Small, dimmed field label used above each text/int field inside a node body.</summary>
    public static Label MakeFieldLabel(string text, string tooltip)
    {
        Label label = new Label(text);
        label.style.color     = new Color(0.65f, 0.65f, 0.65f);
        label.style.fontSize  = 10;
        label.style.marginBottom = 2;
        label.tooltip = tooltip;
        return label;
    }
}

// ---------------------------------------------------------------------------
// Extension method so all DialogueNodeViewBase subclasses can call MakeFieldLabel
// without a static class prefix.
// ---------------------------------------------------------------------------
public abstract partial class DialogueNodeViewBase
{
    protected static Label MakeFieldLabel(string text, string tooltip)
        => DialogueNodeUIHelpers.MakeFieldLabel(text, tooltip);
}

#endif
