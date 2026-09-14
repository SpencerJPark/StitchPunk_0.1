using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorktreeToolkit.Editor
{
    // Main editor window: polls `list` on a timer, lays out the worktree graph with
    // WorktreeTreeLayout, and dispatches card actions through StageSwapGuard / WorktreeCliClient.
    public sealed class WorktreeGraphWindow : EditorWindow
    {
        private const double PollIntervalSeconds = 3.0;
        private const double SpinnerUpdateIntervalSeconds = 0.1;
        private const float CanvasMargin = 32f;
        private const float ColumnStepPixels = 228f;
        private const float RowStepPixels = 172f;
        private const string HasSelectedTileEditorPrefKey = "WorktreeToolkit.HasSelectedTile";

        [MenuItem("Window/Worktree Toolkit")]
        public static void Open()
        {
            WorktreeGraphWindow window = GetWindow<WorktreeGraphWindow>();
            window.titleContent = new GUIContent("Worktree Toolkit");
        }

        private readonly Dictionary<string, WorktreeNodeElement> nodeElementsById =
            new Dictionary<string, WorktreeNodeElement>(StringComparer.Ordinal);

        private VisualElement canvasElement;
        private WorktreeEdgeLayer edgeLayer;
        private VisualElement createRow;
        private TextField createIdField;
        private Label stageChipLabel;
        private Label stageChipBadgeLabel;
        private VisualElement brokerButtonHolder;
        private Button menuButtonElement;
        private Image statusSpinnerImage;
        private Label statusLineTextLabel;
        private GateQueueStrip gateQueueStrip;
        private WorktreeInspectorPane inspectorPane;
        private WorktreeLegend legendElement;
        private Label emptyHintLabel;

        // EditorWindow fields survive a domain reload; per-session bookkeeping must not.
        [System.NonSerialized] private GateRequestStore requestStore;
        [System.NonSerialized] private WorktreeGraphDto currentGraph;
        [System.NonSerialized] private string selectedNodeId;
        // Polled from EditorApplication.update: queuing a main-thread callback from a thread-pool continuation silently lost it.
        private Task<WorktreeCliResult> pendingGraphTask;
        private Task<WorktreeCliResult> pendingWhereTask;
        [System.NonSerialized] private bool hasRequestedStateDirectory;
        [System.NonSerialized] private double lastPollTimeSeconds;
        [System.NonSerialized] private double lastSpinnerUpdateTimeSeconds;

        public void CreateGUI()
        {
            VisualElement root = this.rootVisualElement;
            root.AddToClassList("worktree-window");
            root.style.flexGrow = 1f;
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(this.HandleRootKeyDown);

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.worktreetoolkit/Editor/Window/WorktreeToolkit.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            this.BuildHeader(root);
            this.BuildStatusLine(root);
            this.BuildSplitView(root);

            root.Add(new WorktreeFooterBar());

            this.ResolveStateDirectoryOnce();
            this.RequestGraphRefresh();
        }

        private void BuildHeader(VisualElement root)
        {
            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("worktree-header");
            root.Add(headerRow);

            headerRow.Add(WorktreeIcons.MakeIcon(WorktreeIcons.Branch, 16f));

            Label leftOrnamentLabel = new Label("~");
            leftOrnamentLabel.AddToClassList("worktree-header__ornament");
            headerRow.Add(leftOrnamentLabel);

            Label titleLabel = new Label("Worktrees");
            titleLabel.AddToClassList("worktree-header__title");
            headerRow.Add(titleLabel);

            Label rightOrnamentLabel = new Label("~");
            rightOrnamentLabel.AddToClassList("worktree-header__ornament");
            headerRow.Add(rightOrnamentLabel);

            Button stageChipButton = new Button(this.HandleStageChipClicked);
            stageChipButton.AddToClassList("worktree-header__stage-chip");
            this.stageChipLabel = new Label("Open in Editor: unknown");
            this.stageChipBadgeLabel = new Label();
            this.stageChipBadgeLabel.AddToClassList("worktree-header__badge");
            this.stageChipBadgeLabel.style.display = DisplayStyle.None;
            stageChipButton.Add(this.stageChipLabel);
            stageChipButton.Add(this.stageChipBadgeLabel);
            headerRow.Add(stageChipButton);

            VisualElement headerSpacer = new VisualElement();
            headerSpacer.style.flexGrow = 1f;
            headerRow.Add(headerSpacer);

            headerRow.Add(WorktreeIcons.MakeIconTextButton(this.RequestGraphRefresh, WorktreeIcons.Refresh, "Reload the worktree list", "Refresh"));
            headerRow.Add(WorktreeIcons.MakeIconTextButton(this.HandleAddWorktreeButtonClicked, WorktreeIcons.Create, "Create a new worktree", "New worktree"));

            this.brokerButtonHolder = new VisualElement();
            this.brokerButtonHolder.style.flexDirection = FlexDirection.Row;
            headerRow.Add(this.brokerButtonHolder);
            this.RebuildBrokerButton();

            this.menuButtonElement = WorktreeIcons.MakeIconTextButton(this.HandleMenuButtonClicked, WorktreeIcons.Menu, "More actions", "More");
            headerRow.Add(this.menuButtonElement);

            this.createIdField = new TextField();
            Button createSubmitButton = new Button(this.HandleCreateButtonClicked) { text = "Create" };
            this.createRow = new VisualElement();
            this.createRow.AddToClassList("worktree-create-row");
            this.createRow.style.display = DisplayStyle.None;
            this.createRow.Add(this.createIdField);
            this.createRow.Add(createSubmitButton);
            root.Add(this.createRow);
        }

        private void BuildStatusLine(VisualElement root)
        {
            VisualElement statusLineRow = new VisualElement();
            statusLineRow.AddToClassList("worktree-status-line");
            root.Add(statusLineRow);

            this.statusSpinnerImage = new Image { scaleMode = ScaleMode.ScaleToFit };
            this.statusSpinnerImage.AddToClassList("worktree-icon");
            this.statusSpinnerImage.style.width = 14f;
            this.statusSpinnerImage.style.height = 14f;
            this.statusSpinnerImage.style.display = DisplayStyle.None;
            statusLineRow.Add(this.statusSpinnerImage);

            this.statusLineTextLabel = new Label();
            this.statusLineTextLabel.AddToClassList("worktree-status-line__text");
            statusLineRow.Add(this.statusLineTextLabel);
        }

        private void BuildSplitView(VisualElement root)
        {
            TwoPaneSplitView splitView = new TwoPaneSplitView(1, 300f, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1f;
            root.Add(splitView);

            VisualElement canvasColumn = new VisualElement();
            canvasColumn.style.flexGrow = 1f;
            canvasColumn.style.flexDirection = FlexDirection.Column;
            splitView.Add(canvasColumn);

            ScrollView scrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scrollView.style.flexGrow = 1f;
            scrollView.AddToClassList("worktree-canvas");
            canvasColumn.Add(scrollView);

            // Sibling after the ScrollView, absolutely positioned, so it stays pinned top-right while the canvas scrolls.
            this.legendElement = new WorktreeLegend();
            this.legendElement.style.position = Position.Absolute;
            this.legendElement.style.top = 8f;
            this.legendElement.style.right = 8f;
            this.legendElement.SetFirstUseHintVisible(!EditorPrefs.GetBool(HasSelectedTileEditorPrefKey, false));
            canvasColumn.Add(this.legendElement);

            this.canvasElement = new VisualElement();
            this.canvasElement.style.position = Position.Relative;
            this.canvasElement.RegisterCallback<PointerDownEvent>(this.HandleCanvasBackgroundPointerDown);
            scrollView.Add(this.canvasElement);

            this.edgeLayer = new WorktreeEdgeLayer();
            this.canvasElement.Add(this.edgeLayer);

            VisualElement dividerElement = new VisualElement();
            dividerElement.AddToClassList("worktree-divider");
            dividerElement.style.height = 2f;
            canvasColumn.Add(dividerElement);

            this.gateQueueStrip = new GateQueueStrip();
            canvasColumn.Add(this.gateQueueStrip);

            this.inspectorPane = new WorktreeInspectorPane();
            this.inspectorPane.ActionRequested += this.HandleNodeActionRequested;
            splitView.Add(this.inspectorPane);
        }

        private void OnEnable()
        {
            this.titleContent = new GUIContent("Worktree Toolkit");
            EditorApplication.update += this.HandleEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= this.HandleEditorUpdate;
        }

        private void OnFocus()
        {
            this.RequestGraphRefresh();
        }

        private void HandleEditorUpdate()
        {
            this.DrainCompletedCliTasks();

            if (this.canvasElement == null)
            {
                return;
            }

            double currentTimeSeconds = EditorApplication.timeSinceStartup;

            foreach (WorktreeNodeElement nodeElement in this.nodeElementsById.Values)
            {
                nodeElement.Tick(currentTimeSeconds);
            }

            if (currentTimeSeconds - this.lastSpinnerUpdateTimeSeconds >= SpinnerUpdateIntervalSeconds)
            {
                this.lastSpinnerUpdateTimeSeconds = currentTimeSeconds;
                this.UpdateStatusSpinner(currentTimeSeconds);
            }

            if (!this.hasFocus)
            {
                return;
            }

            if (currentTimeSeconds - this.lastPollTimeSeconds < PollIntervalSeconds)
            {
                return;
            }

            this.lastPollTimeSeconds = currentTimeSeconds;
            this.RequestGraphRefresh();
        }

        private void HandleRootKeyDown(KeyDownEvent keyDownEvent)
        {
            if (keyDownEvent.keyCode == KeyCode.F5)
            {
                this.RequestGraphRefresh();
            }
        }

        private void RebuildBrokerButton()
        {
            this.brokerButtonHolder.Clear();

            bool brokerIsEnabled = GateBroker.IsEnabled;
            string brokerIconName = brokerIsEnabled ? WorktreeIcons.BrokerOn : WorktreeIcons.BrokerOff;
            string brokerButtonText = brokerIsEnabled ? "Broker: on" : "Broker: off";
            Button brokerButton = WorktreeIcons.MakeIconTextButton(this.HandleBrokerButtonClicked, brokerIconName, "Toggle the gate broker", brokerButtonText);

            VisualElement brokerDot = new VisualElement();
            brokerDot.AddToClassList("worktree-header__broker-dot");
            if (brokerIsEnabled)
            {
                brokerDot.AddToClassList("worktree-header__broker-dot--alive");
            }
            brokerButton.Add(brokerDot);

            this.brokerButtonHolder.Add(brokerButton);
        }

        private void HandleBrokerButtonClicked()
        {
            GateBroker.IsEnabled = !GateBroker.IsEnabled;
            this.RebuildBrokerButton();
        }

        private void HandleStageChipClicked()
        {
            this.HandleNodeSelectionRequested(WorktreeTreeLayout.TrunkNodeId);
        }

        private void HandleMenuButtonClicked()
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Adopt existing worktrees"), false, this.HandleAdoptMenuItemClicked);
            menu.AddItem(new GUIContent("Edit noise globs…"), false, this.HandleEditNoiseGlobsMenuItemClicked);
            menu.AddItem(new GUIContent("Open documentation"), false, HandleOpenDocumentationMenuItemClicked);
            menu.AddItem(new GUIContent("Gate broker enabled"), GateBroker.IsEnabled, this.HandleToggleBrokerMenuItemClicked);
            menu.DropDown(this.menuButtonElement.worldBound);
        }

        private void HandleAdoptMenuItemClicked()
        {
            WorktreeCliResult cliResult = WorktreeCliClient.Run("adopt");
            if (!cliResult.Succeeded)
            {
                this.ShowError(ResolveCliFailureMessage(cliResult));
            }
            else
            {
                this.ClearError();
            }

            this.RequestGraphRefresh();
        }

        private void HandleEditNoiseGlobsMenuItemClicked()
        {
            WorktreeCliResult cliResult = WorktreeCliClient.Run("config");
            string dialogMessage = cliResult.Succeeded ? cliResult.standardOutput : ResolveCliFailureMessage(cliResult);
            EditorUtility.DisplayDialog("Noise globs", dialogMessage, "OK");
        }

        private static void HandleOpenDocumentationMenuItemClicked()
        {
            EditorUtility.RevealInFinder(System.IO.Path.GetFullPath("Packages/com.worktreetoolkit/Documentation~/worktree-toolkit.md"));
        }

        private void HandleToggleBrokerMenuItemClicked()
        {
            GateBroker.IsEnabled = !GateBroker.IsEnabled;
            this.RebuildBrokerButton();
        }

        private void HandleCanvasBackgroundPointerDown(PointerDownEvent pointerDownEvent)
        {
            if (pointerDownEvent.target != this.canvasElement)
            {
                return;
            }

            this.ClearSelection();
        }

        private void HandleNodeSelectionRequested(string nodeId)
        {
            this.selectedNodeId = nodeId;
            this.ApplySelectionToNodes();
            this.UpdateInspectorForSelection();

            if (!EditorPrefs.GetBool(HasSelectedTileEditorPrefKey, false))
            {
                EditorPrefs.SetBool(HasSelectedTileEditorPrefKey, true);
                this.legendElement.SetFirstUseHintVisible(false);
            }
        }

        private void ClearSelection()
        {
            this.selectedNodeId = null;
            this.ApplySelectionToNodes();
            this.inspectorPane.ShowNothingSelected();
        }

        private void ApplySelectionToNodes()
        {
            foreach (KeyValuePair<string, WorktreeNodeElement> nodeEntry in this.nodeElementsById)
            {
                nodeEntry.Value.SetSelected(string.Equals(nodeEntry.Key, this.selectedNodeId, StringComparison.Ordinal));
            }
        }

        private void UpdateInspectorForSelection()
        {
            if (string.IsNullOrEmpty(this.selectedNodeId) || this.currentGraph == null)
            {
                this.inspectorPane.ShowNothingSelected();
                return;
            }

            this.inspectorPane.SetTrunkBranch(this.currentGraph.trunk);

            if (string.Equals(this.selectedNodeId, WorktreeTreeLayout.TrunkNodeId, StringComparison.Ordinal))
            {
                bool trunkIsOnStage = this.currentGraph.stage != null
                    && string.Equals(this.currentGraph.stage.branch, this.currentGraph.trunk, StringComparison.Ordinal);
                this.inspectorPane.ShowTrunk(this.currentGraph.stage, this.currentGraph.trunk, trunkIsOnStage);
                return;
            }

            WorktreeNodeDto selectedWorktreeNode = this.FindWorktreeNode(this.selectedNodeId);
            if (selectedWorktreeNode == null)
            {
                this.selectedNodeId = null;
                this.inspectorPane.ShowNothingSelected();
                return;
            }

            bool worktreeIsOnStage = this.currentGraph.stage != null
                && string.Equals(selectedWorktreeNode.branch, this.currentGraph.stage.branch, StringComparison.Ordinal);
            bool stageIsBusy = this.currentGraph.stage != null && this.currentGraph.stage.busyWith != null && this.currentGraph.stage.busyWith.IsHeld;
            this.inspectorPane.ShowWorktree(selectedWorktreeNode, worktreeIsOnStage, stageIsBusy);
        }

        private void HandleAddWorktreeButtonClicked()
        {
            bool isRowVisible = this.createRow.style.display == DisplayStyle.Flex;
            this.createRow.style.display = isRowVisible ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void HandleCreateButtonClicked()
        {
            string worktreeId = this.createIdField.value != null ? this.createIdField.value.Trim() : string.Empty;
            if (string.IsNullOrEmpty(worktreeId))
            {
                return;
            }

            WorktreeCliResult cliResult = WorktreeCliClient.Run("create", worktreeId);
            if (!cliResult.Succeeded)
            {
                this.ShowError(ResolveCliFailureMessage(cliResult));
            }
            else
            {
                this.ClearError();
                this.createIdField.value = string.Empty;
                this.createRow.style.display = DisplayStyle.None;
            }

            this.RequestGraphRefresh();
        }

        // Once per window lifetime: resolve the shared gate-protocol state directory so the
        // queue strip and status line have somewhere to read from.
        private void ResolveStateDirectoryOnce()
        {
            if (this.hasRequestedStateDirectory)
            {
                return;
            }

            this.hasRequestedStateDirectory = true;

            this.pendingWhereTask = WorktreeCliClient.RunAsync("where");
        }

        private void RequestGraphRefresh()
        {
            if (this.pendingGraphTask != null)
            {
                return;
            }

            this.pendingGraphTask = WorktreeCliClient.RunAsync("list");
        }

        private void DrainCompletedCliTasks()
        {
            // Results wait until CreateGUI has built the elements they update.
            if (this.canvasElement == null)
            {
                return;
            }

            if (this.pendingWhereTask != null && this.pendingWhereTask.IsCompleted)
            {
                WorktreeCliResult whereResult = ResultOrFault(this.pendingWhereTask);
                this.pendingWhereTask = null;
                if (WorktreeCliJson.TryParse(whereResult, out WherePathsDto wherePaths, out string whereErrorMessage))
                {
                    this.requestStore = new GateRequestStore(wherePaths.stateDirectory);
                    this.gateQueueStrip.Refresh(this.requestStore);
                }
                else
                {
                    this.hasRequestedStateDirectory = false;
                    this.ShowError(whereErrorMessage);
                }
            }

            if (this.pendingGraphTask != null && this.pendingGraphTask.IsCompleted)
            {
                WorktreeCliResult graphResult = ResultOrFault(this.pendingGraphTask);
                this.pendingGraphTask = null;
                this.HandleGraphResult(graphResult);
            }
        }

        // A faulted task must still reach the main thread, or the in-progress flag never clears.
        private static WorktreeCliResult ResultOrFault(System.Threading.Tasks.Task<WorktreeCliResult> task)
        {
            if (!task.IsFaulted)
            {
                return task.Result;
            }

            return new WorktreeCliResult
            {
                exitCode = -3,
                standardOutput = string.Empty,
                standardError = "Worktree CLI call failed: " + task.Exception.GetBaseException().Message
            };
        }

        private void HandleGraphResult(WorktreeCliResult result)
        {
            bool parsed = WorktreeCliJson.TryParse(result, out WorktreeGraphDto graph, out string errorMessage);
            if (!parsed)
            {
                this.ShowError(errorMessage);
                return;
            }

            this.ClearError();
            this.currentGraph = graph;
            this.RebuildGraph(graph);
            this.ApplySelectionToNodes();
            this.UpdateInspectorForSelection();

            this.gateQueueStrip.Refresh(this.requestStore);
            this.UpdateStatusLine(graph);
        }

        private void RebuildGraph(WorktreeGraphDto graph)
        {
            List<WorktreeLayoutNode> layoutNodes = WorktreeTreeLayout.Compute(graph);

            Dictionary<string, WorktreeNodeDto> worktreeById = new Dictionary<string, WorktreeNodeDto>(StringComparer.Ordinal);
            if (graph.worktrees != null)
            {
                foreach (WorktreeNodeDto worktreeNode in graph.worktrees)
                {
                    if (worktreeNode != null && !string.IsNullOrEmpty(worktreeNode.id))
                    {
                        worktreeById[worktreeNode.id] = worktreeNode;
                    }
                }
            }

            Dictionary<string, WorktreeLayoutNode> layoutById = new Dictionary<string, WorktreeLayoutNode>(StringComparer.Ordinal);
            HashSet<string> currentNodeIds = new HashSet<string>(StringComparer.Ordinal);

            float maxRight = 0f;
            float maxBottom = 0f;

            foreach (WorktreeLayoutNode layoutNode in layoutNodes)
            {
                layoutById[layoutNode.nodeId] = layoutNode;
                currentNodeIds.Add(layoutNode.nodeId);

                if (!this.nodeElementsById.TryGetValue(layoutNode.nodeId, out WorktreeNodeElement nodeElement))
                {
                    nodeElement = new WorktreeNodeElement(layoutNode.nodeId);
                    nodeElement.SelectionRequested += this.HandleNodeSelectionRequested;
                    nodeElement.ActionRequested += this.HandleNodeActionRequested;
                    this.nodeElementsById.Add(layoutNode.nodeId, nodeElement);
                    this.canvasElement.Add(nodeElement);
                }

                float leftPosition = CanvasMargin + (layoutNode.column * ColumnStepPixels);
                float topPosition = CanvasMargin + (layoutNode.row * RowStepPixels);
                nodeElement.style.position = Position.Absolute;
                nodeElement.style.left = leftPosition;
                nodeElement.style.top = topPosition;

                if (layoutNode.isTrunk)
                {
                    bool trunkIsOnStage = graph.stage != null && string.Equals(graph.stage.branch, graph.trunk, StringComparison.Ordinal);
                    nodeElement.BindTrunk(graph.stage, graph.trunk, trunkIsOnStage);
                }
                else if (worktreeById.TryGetValue(layoutNode.nodeId, out WorktreeNodeDto worktreeNode))
                {
                    bool worktreeIsOnStage = graph.stage != null && string.Equals(worktreeNode.branch, graph.stage.branch, StringComparison.Ordinal);
                    bool stageIsBusy = graph.stage != null && graph.stage.busyWith != null && graph.stage.busyWith.IsHeld;
                    nodeElement.BindWorktree(worktreeNode, worktreeIsOnStage, stageIsBusy);
                }

                nodeElement.SetTrunkBranch(graph.trunk);

                maxRight = Mathf.Max(maxRight, leftPosition + WorktreeNodeElement.SlotWidth);
                maxBottom = Mathf.Max(maxBottom, topPosition + WorktreeNodeElement.SlotHeight);
            }

            List<string> staleNodeIds = new List<string>();
            foreach (string existingNodeId in this.nodeElementsById.Keys)
            {
                if (!currentNodeIds.Contains(existingNodeId))
                {
                    staleNodeIds.Add(existingNodeId);
                }
            }

            foreach (string staleNodeId in staleNodeIds)
            {
                this.nodeElementsById[staleNodeId].RemoveFromHierarchy();
                this.nodeElementsById.Remove(staleNodeId);
            }

            List<WorktreeEdge> edges = new List<WorktreeEdge>();
            foreach (WorktreeLayoutNode layoutNode in layoutNodes)
            {
                if (layoutNode.isTrunk || layoutNode.parentNodeId == null
                    || !layoutById.TryGetValue(layoutNode.parentNodeId, out WorktreeLayoutNode parentLayoutNode))
                {
                    continue;
                }

                float parentLeft = CanvasMargin + (parentLayoutNode.column * ColumnStepPixels);
                float parentTop = CanvasMargin + (parentLayoutNode.row * RowStepPixels);
                float childLeft = CanvasMargin + (layoutNode.column * ColumnStepPixels);
                float childTop = CanvasMargin + (layoutNode.row * RowStepPixels);

                Vector2 edgeStart = new Vector2(
                    parentLeft + ((WorktreeNodeElement.SlotWidth + WorktreeNodeElement.TileSize) / 2f),
                    parentTop + (WorktreeNodeElement.TileSize / 2f));
                Vector2 edgeEnd = new Vector2(
                    childLeft + ((WorktreeNodeElement.SlotWidth - WorktreeNodeElement.TileSize) / 2f),
                    childTop + (WorktreeNodeElement.TileSize / 2f));

                WorktreeTileState childTileState = this.nodeElementsById[layoutNode.nodeId].TileState;
                edges.Add(new WorktreeEdge(edgeStart, edgeEnd, childTileState));
            }

            this.edgeLayer.SetEdges(edges);

            this.canvasElement.style.width = maxRight + CanvasMargin;
            this.canvasElement.style.height = maxBottom + CanvasMargin;

            this.UpdateEmptyStateHint(graph, layoutById);
            this.UpdateStageChip(graph);
        }

        private void UpdateEmptyStateHint(WorktreeGraphDto graph, Dictionary<string, WorktreeLayoutNode> layoutById)
        {
            bool graphIsEmpty = graph.worktrees == null || graph.worktrees.Length == 0;
            if (!graphIsEmpty)
            {
                if (this.emptyHintLabel != null)
                {
                    this.emptyHintLabel.RemoveFromHierarchy();
                    this.emptyHintLabel = null;
                }
                return;
            }

            if (this.emptyHintLabel == null)
            {
                this.emptyHintLabel = new Label("No worktrees yet. Run /worktree-run in Claude, or press + to create one.");
                this.emptyHintLabel.AddToClassList("worktree-empty-hint");
                this.emptyHintLabel.style.position = Position.Absolute;
                this.canvasElement.Add(this.emptyHintLabel);
            }

            WorktreeLayoutNode trunkLayoutNode = layoutById[WorktreeTreeLayout.TrunkNodeId];
            float trunkLeft = CanvasMargin + (trunkLayoutNode.column * ColumnStepPixels);
            float trunkTop = CanvasMargin + (trunkLayoutNode.row * RowStepPixels);
            this.emptyHintLabel.style.left = trunkLeft;
            this.emptyHintLabel.style.top = trunkTop + WorktreeNodeElement.SlotHeight;
        }

        private void UpdateStageChip(WorktreeGraphDto graph)
        {
            string branchName = graph.stage != null ? graph.stage.branch : "unknown";
            this.stageChipLabel.text = "Open in Editor: " + branchName;

            int dirtyTrackedCount = graph.stage != null ? graph.stage.dirtyTracked : 0;
            if (dirtyTrackedCount > 0)
            {
                string uncommittedFileWord = dirtyTrackedCount == 1 ? "file" : "files";
                this.stageChipBadgeLabel.text = " · " + dirtyTrackedCount.ToString() + " uncommitted " + uncommittedFileWord;
                this.stageChipBadgeLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                this.stageChipBadgeLabel.style.display = DisplayStyle.None;
            }
        }

        private void UpdateStatusLine(WorktreeGraphDto graph)
        {
            bool stageIsBusy = graph.stage != null && graph.stage.busyWith != null && graph.stage.busyWith.IsHeld;
            if (stageIsBusy)
            {
                this.statusLineTextLabel.text = "busy: " + graph.stage.busyWith.holder;
                this.statusLineTextLabel.AddToClassList("worktree-status-line__text--busy");
                return;
            }

            this.statusLineTextLabel.RemoveFromClassList("worktree-status-line__text--busy");

            int totalWorktreeCount = graph.worktrees != null ? graph.worktrees.Length : 0;
            int buildingCount = 0;
            int gatingCount = 0;
            int readyCount = 0;
            if (graph.worktrees != null)
            {
                foreach (WorktreeNodeDto worktreeNode in graph.worktrees)
                {
                    string leadStatus = worktreeNode != null && worktreeNode.lead != null ? worktreeNode.lead.status : null;
                    if (string.Equals(leadStatus, "building", StringComparison.OrdinalIgnoreCase))
                    {
                        buildingCount++;
                    }
                    else if (string.Equals(leadStatus, "gating", StringComparison.OrdinalIgnoreCase))
                    {
                        gatingCount++;
                    }
                    else if (string.Equals(leadStatus, "ready", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(leadStatus, "done", StringComparison.OrdinalIgnoreCase))
                    {
                        readyCount++;
                    }
                }
            }

            string worktreeCountWord = totalWorktreeCount == 1 ? " worktree" : " worktrees";
            this.statusLineTextLabel.text = totalWorktreeCount.ToString() + worktreeCountWord + " · " + buildingCount.ToString()
                + " building · " + gatingCount.ToString() + " checking · " + readyCount.ToString() + " ready";
        }

        private void UpdateStatusSpinner(double currentTimeSeconds)
        {
            bool stageIsBusy = this.currentGraph != null && this.currentGraph.stage != null
                && this.currentGraph.stage.busyWith != null && this.currentGraph.stage.busyWith.IsHeld;
            if (!stageIsBusy)
            {
                this.statusSpinnerImage.style.display = DisplayStyle.None;
                return;
            }

            this.statusSpinnerImage.style.display = DisplayStyle.Flex;
            this.statusSpinnerImage.image = WorktreeIcons.SpinnerFrame(currentTimeSeconds);
        }

        private void HandleNodeActionRequested(string nodeId, WorktreeNodeAction action)
        {
            switch (action)
            {
                case WorktreeNodeAction.PutOnStage:
                    this.HandlePutOnStage(nodeId);
                    break;
                case WorktreeNodeAction.ReturnStage:
                    this.HandleReturnStage();
                    break;
                case WorktreeNodeAction.Merge:
                    this.HandleMerge(nodeId);
                    break;
                case WorktreeNodeAction.Remove:
                    this.HandleRemove(nodeId);
                    break;
                case WorktreeNodeAction.Reveal:
                    this.HandleReveal(nodeId);
                    break;
            }
        }

        private void HandlePutOnStage(string nodeId)
        {
            WorktreeNodeDto targetNode = this.FindWorktreeNode(nodeId);
            if (targetNode == null)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Open in Editor",
                "Switch this Editor to " + targetNode.branch
                    + "? Unsaved scenes, or unsaved assets that this branch changes, will stop the switch.",
                "Open",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            StageMoveOutcome outcome = StageSwapGuard.MoveStage(targetNode.branch, "review", nodeId);
            this.HandleStageMoveOutcome(outcome);
            this.RequestGraphRefresh();
        }

        private void HandleReturnStage()
        {
            if (this.currentGraph == null)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Back to " + this.currentGraph.trunk,
                "Switch this Editor back to " + this.currentGraph.trunk + "?",
                "Switch back",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            StageMoveOutcome outcome = StageSwapGuard.MoveStage(this.currentGraph.trunk, "return");
            this.HandleStageMoveOutcome(outcome);
            this.RequestGraphRefresh();
        }

        private void HandleMerge(string nodeId)
        {
            WorktreeNodeDto targetNode = this.FindWorktreeNode(nodeId);
            if (targetNode == null)
            {
                return;
            }

            string trunkBranchName = this.currentGraph != null ? this.currentGraph.trunk : "trunk";
            bool confirmed = EditorUtility.DisplayDialog(
                "Merge into " + trunkBranchName,
                "Add " + targetNode.branch + "'s commits to " + trunkBranchName + "? History stays linear.",
                "Merge",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            WorktreeCliResult cliResult = WorktreeCliClient.Run("merge", nodeId);
            if (!cliResult.Succeeded)
            {
                this.ShowError(ResolveCliFailureMessage(cliResult));
            }
            else
            {
                this.ClearError();
            }

            this.RequestGraphRefresh();
        }

        private void HandleRemove(string nodeId)
        {
            WorktreeNodeDto targetNode = this.FindWorktreeNode(nodeId);
            if (targetNode == null)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete worktree",
                "Delete the worktree folder for " + targetNode.branch + "? An unmerged branch is kept.",
                "Delete",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            WorktreeCliResult cliResult = WorktreeCliClient.Run("remove", nodeId);
            if (!cliResult.Succeeded)
            {
                this.ShowError(ResolveCliFailureMessage(cliResult));
            }
            else
            {
                this.ClearError();
            }

            this.RequestGraphRefresh();
        }

        private void HandleReveal(string nodeId)
        {
            if (string.Equals(nodeId, WorktreeTreeLayout.TrunkNodeId, StringComparison.Ordinal))
            {
                EditorUtility.RevealInFinder(WorktreeCliClient.ProjectRootPath);
                return;
            }

            WorktreeNodeDto targetNode = this.FindWorktreeNode(nodeId);
            if (targetNode != null)
            {
                EditorUtility.RevealInFinder(targetNode.path);
            }
        }

        private void HandleStageMoveOutcome(StageMoveOutcome outcome)
        {
            if (outcome.blockers != null && outcome.blockers.Count > 0)
            {
                this.ShowError(string.Join("\n", outcome.blockers));
                return;
            }

            if (outcome.cliResult != null && !outcome.cliResult.Succeeded)
            {
                this.ShowError(ResolveCliFailureMessage(outcome.cliResult));
                return;
            }

            this.ClearError();
        }

        private WorktreeNodeDto FindWorktreeNode(string nodeId)
        {
            if (this.currentGraph == null || this.currentGraph.worktrees == null)
            {
                return null;
            }

            foreach (WorktreeNodeDto worktreeNode in this.currentGraph.worktrees)
            {
                if (worktreeNode != null && string.Equals(worktreeNode.id, nodeId, StringComparison.Ordinal))
                {
                    return worktreeNode;
                }
            }

            return null;
        }

        private static string ResolveCliFailureMessage(WorktreeCliResult cliResult)
        {
            // On a failed result TryParse never touches T, so any DTO type resolves the same message.
            WorktreeCliJson.TryParse(cliResult, out CliErrorDto _, out string errorMessage);
            return errorMessage;
        }

        private void ShowError(string message)
        {
            this.statusLineTextLabel.text = message;
            this.statusLineTextLabel.RemoveFromClassList("worktree-status-line__text--busy");
            this.statusLineTextLabel.AddToClassList("worktree-status-line__text--error");
        }

        private void ClearError()
        {
            this.statusLineTextLabel.RemoveFromClassList("worktree-status-line__text--error");
        }
    }
}
