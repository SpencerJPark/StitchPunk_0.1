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
        private const float ColumnWidth = 272f;
        private const float RowHeight = 100f;
        private const float CanvasMargin = 16f;

        [MenuItem("Window/Worktree Toolkit")]
        public static void Open()
        {
            WorktreeGraphWindow window = GetWindow<WorktreeGraphWindow>();
            window.titleContent = new GUIContent("Worktree Toolkit");
        }

        private readonly Dictionary<string, WorktreeNodeElement> nodeElementsById =
            new Dictionary<string, WorktreeNodeElement>(StringComparer.Ordinal);

        private Label stageLabel;
        private Toggle brokerToggle;
        private Label queueCountLabel;
        private VisualElement createRow;
        private TextField createIdField;
        private Label busyBannerLabel;
        private Label errorLabel;
        private GateQueuePanel queuePanel;
        private VisualElement canvasElement;
        private WorktreeEdgeLayer edgeLayer;

        // EditorWindow fields survive a domain reload; per-session bookkeeping must not.
        [System.NonSerialized] private GateRequestStore requestStore;
        [System.NonSerialized] private WorktreeGraphDto currentGraph;
        // Polled from EditorApplication.update: queuing a main-thread callback from a thread-pool continuation silently lost it.
        private Task<WorktreeCliResult> pendingGraphTask;
        private Task<WorktreeCliResult> pendingWhereTask;
        [System.NonSerialized] private bool hasRequestedStateDirectory;
        [System.NonSerialized] private double lastPollTimeSeconds;

        public void CreateGUI()
        {
            VisualElement root = this.rootVisualElement;

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.worktreetoolkit/Editor/Window/WorktreeToolkit.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            VisualElement toolbarRow = new VisualElement();
            toolbarRow.style.flexDirection = FlexDirection.Row;
            root.Add(toolbarRow);

            Button refreshButton = new Button(this.RequestGraphRefresh) { text = "⟳" };
            Button addWorktreeButton = new Button { text = "＋" };
            this.stageLabel = new Label("Stage: unknown");
            this.brokerToggle = new Toggle("Broker") { value = GateBroker.IsEnabled };
            this.brokerToggle.RegisterValueChangedCallback(this.HandleBrokerToggleChanged);
            this.queueCountLabel = new Label("Queue: 0");

            toolbarRow.Add(refreshButton);
            toolbarRow.Add(addWorktreeButton);
            toolbarRow.Add(this.stageLabel);
            toolbarRow.Add(this.brokerToggle);
            toolbarRow.Add(this.queueCountLabel);

            this.createIdField = new TextField();
            Button createButton = new Button(this.HandleCreateButtonClicked) { text = "Create" };
            this.createRow = new VisualElement();
            this.createRow.style.flexDirection = FlexDirection.Row;
            this.createRow.style.display = DisplayStyle.None;
            this.createRow.Add(this.createIdField);
            this.createRow.Add(createButton);
            root.Add(this.createRow);

            addWorktreeButton.clicked += this.HandleAddWorktreeButtonClicked;

            this.busyBannerLabel = new Label();
            this.busyBannerLabel.AddToClassList("worktree-window__busy-banner");
            this.busyBannerLabel.style.display = DisplayStyle.None;
            root.Add(this.busyBannerLabel);

            this.errorLabel = new Label();
            this.errorLabel.AddToClassList("worktree-window__error");
            this.errorLabel.style.display = DisplayStyle.None;
            root.Add(this.errorLabel);

            this.queuePanel = new GateQueuePanel();
            root.Add(this.queuePanel);

            ScrollView scrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scrollView.style.flexGrow = 1f;
            root.Add(scrollView);

            this.canvasElement = new VisualElement();
            this.canvasElement.style.position = Position.Relative;
            scrollView.Add(this.canvasElement);

            this.edgeLayer = new WorktreeEdgeLayer();
            this.canvasElement.Add(this.edgeLayer);

            this.ResolveStateDirectoryOnce();
            this.RequestGraphRefresh();
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

            if (!this.hasFocus)
            {
                return;
            }

            double currentTimeSeconds = EditorApplication.timeSinceStartup;
            if (currentTimeSeconds - this.lastPollTimeSeconds < PollIntervalSeconds)
            {
                return;
            }

            this.lastPollTimeSeconds = currentTimeSeconds;
            this.RequestGraphRefresh();
        }

        private void HandleBrokerToggleChanged(ChangeEvent<bool> changeEvent)
        {
            GateBroker.IsEnabled = changeEvent.newValue;
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
        // queue panel and queue count have somewhere to read from.
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
                    this.queuePanel.Refresh(this.requestStore);
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

            this.queuePanel.Refresh(this.requestStore);
            int queuedCount = this.requestStore != null ? this.requestStore.ListQueuedRequests().Count : 0;
            this.queueCountLabel.text = "Queue: " + queuedCount.ToString();
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
                    nodeElement.ActionRequested += this.HandleNodeActionRequested;
                    this.nodeElementsById.Add(layoutNode.nodeId, nodeElement);
                    this.canvasElement.Add(nodeElement);
                }

                float leftPosition = CanvasMargin + (layoutNode.column * ColumnWidth);
                float topPosition = CanvasMargin + (layoutNode.row * RowHeight);
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

                maxRight = Mathf.Max(maxRight, leftPosition + WorktreeNodeElement.CardWidth);
                maxBottom = Mathf.Max(maxBottom, topPosition + WorktreeNodeElement.CardHeight);
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

                float parentLeft = CanvasMargin + (parentLayoutNode.column * ColumnWidth);
                float parentTop = CanvasMargin + (parentLayoutNode.row * RowHeight);
                float childLeft = CanvasMargin + (layoutNode.column * ColumnWidth);
                float childTop = CanvasMargin + (layoutNode.row * RowHeight);

                Vector2 edgeStart = new Vector2(parentLeft + WorktreeNodeElement.CardWidth, parentTop + (WorktreeNodeElement.CardHeight / 2f));
                Vector2 edgeEnd = new Vector2(childLeft, childTop + (WorktreeNodeElement.CardHeight / 2f));

                bool isActive = graph.stage != null
                    && worktreeById.TryGetValue(layoutNode.nodeId, out WorktreeNodeDto childWorktreeNode)
                    && string.Equals(childWorktreeNode.branch, graph.stage.branch, StringComparison.Ordinal);

                edges.Add(new WorktreeEdge(edgeStart, edgeEnd, isActive));
            }

            this.edgeLayer.SetEdges(edges);

            this.canvasElement.style.width = maxRight + CanvasMargin;
            this.canvasElement.style.height = maxBottom + CanvasMargin;

            this.UpdateBusyBanner(graph);
            this.stageLabel.text = graph.stage != null ? "Stage: " + graph.stage.branch : "Stage: unknown";
        }

        private void UpdateBusyBanner(WorktreeGraphDto graph)
        {
            bool stageIsBusy = graph.stage != null && graph.stage.busyWith != null && graph.stage.busyWith.IsHeld;
            if (stageIsBusy)
            {
                this.busyBannerLabel.text = "Stage busy: " + graph.stage.busyWith.holder;
                this.busyBannerLabel.style.display = DisplayStyle.Flex;
            }
            else if (EditorApplication.isCompiling)
            {
                this.busyBannerLabel.text = "Unity is compiling";
                this.busyBannerLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                this.busyBannerLabel.style.display = DisplayStyle.None;
            }
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
                "Put on stage",
                "Switch this project to " + targetNode.branch
                    + "? Unsaved scenes, or unsaved assets that this branch changes, will block the move.",
                "Switch",
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

            bool confirmed = EditorUtility.DisplayDialog(
                "Merge worktree",
                "Merge " + targetNode.branch + " into "
                    + (this.currentGraph != null ? this.currentGraph.trunk : "trunk") + "?",
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
                "Remove worktree",
                "Remove the worktree for " + targetNode.branch + "? An unmerged branch is kept.",
                "Remove",
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
            this.errorLabel.text = message;
            this.errorLabel.style.display = DisplayStyle.Flex;
        }

        private void ClearError()
        {
            this.errorLabel.text = string.Empty;
            this.errorLabel.style.display = DisplayStyle.None;
        }
    }
}
