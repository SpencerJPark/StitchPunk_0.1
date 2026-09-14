using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorktreeToolkit.Editor
{
    public enum WorktreeNodeAction
    {
        PutOnStage,
        ReturnStage,
        Merge,
        Remove,
        Reveal
    }

    /// <summary>
    /// One graph card: either a worktree node (BindWorktree) or the single trunk node (BindTrunk).
    /// Built once in the constructor; Bind* methods only mutate text/tooltip/visibility/classes.
    /// </summary>
    public sealed class WorktreeNodeElement : VisualElement
    {
        public const float CardWidth = 240f;
        public const float CardHeight = 84f;

        public string NodeId { get; }

        public event Action<string, WorktreeNodeAction> ActionRequested;

        private readonly Label titleLabel;
        private readonly Label modelsLabel;
        private readonly Label statusLabel;
        private readonly Label countsLabel;
        private readonly Button primaryActionButton;
        private readonly Button moreActionsButton;

        private bool isTrunkCard;
        private WorktreeNodeAction primaryAction;

        public WorktreeNodeElement(string nodeId)
        {
            this.NodeId = nodeId;

            this.style.width = CardWidth;
            this.style.height = CardHeight;
            this.AddToClassList("worktree-node");

            this.titleLabel = new Label();
            this.titleLabel.AddToClassList("worktree-node__title");
            this.Add(this.titleLabel);

            this.modelsLabel = new Label();
            this.modelsLabel.AddToClassList("worktree-node__models");
            this.Add(this.modelsLabel);

            this.statusLabel = new Label();
            this.statusLabel.AddToClassList("worktree-node__status");
            this.Add(this.statusLabel);

            this.countsLabel = new Label();
            this.countsLabel.AddToClassList("worktree-node__counts");
            this.Add(this.countsLabel);

            VisualElement actionsRow = new VisualElement();
            actionsRow.AddToClassList("worktree-node__actions");
            this.Add(actionsRow);

            this.primaryActionButton = new Button(() =>
                this.ActionRequested?.Invoke(this.NodeId, this.primaryAction));
            actionsRow.Add(this.primaryActionButton);

            this.moreActionsButton = new Button();
            this.moreActionsButton.text = "⋯";
            this.moreActionsButton.tooltip = "More actions";
            actionsRow.Add(this.moreActionsButton);

            ContextualMenuManipulator moreActionsMenu = new ContextualMenuManipulator(this.PopulateMoreActionsMenu);
            moreActionsMenu.activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
            this.moreActionsButton.AddManipulator(moreActionsMenu);
        }

        public void BindWorktree(WorktreeNodeDto node, bool isOnStage, bool stageIsBusy)
        {
            this.isTrunkCard = false;

            this.titleLabel.text = node.branch;

            bool leadBound = node.lead != null && node.lead.IsBound;
            if (leadBound)
            {
                this.modelsLabel.text = $"{node.lead.leadModel} ▸ {node.lead.workerModel}";
                this.modelsLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                this.modelsLabel.style.display = DisplayStyle.None;
            }

            string statusText;
            if (node.missing)
            {
                statusText = "missing";
            }
            else if (node.locked)
            {
                statusText = "locked";
            }
            else if (node.stale)
            {
                statusText = "stale";
            }
            else
            {
                statusText = leadBound ? node.lead.status : string.Empty;
            }

            this.statusLabel.text = statusText;
            this.statusLabel.tooltip = statusText;

            string countsText = $"▲{node.ahead} ▼{node.behind} ✎{node.dirty}";
            if (node.sizeMegabytes > 0)
            {
                countsText += $" {node.sizeMegabytes} MB";
            }

            this.countsLabel.text = countsText;
            this.countsLabel.style.display = DisplayStyle.Flex;

            this.EnableInClassList("worktree-node--trunk", false);
            this.EnableInClassList("worktree-node--on-stage", isOnStage);
            this.EnableInClassList("worktree-node--inactive", !isOnStage);
            this.EnableInClassList("worktree-node--stale", node.stale);
            this.EnableInClassList("worktree-node--missing", node.missing);

            this.primaryAction = isOnStage ? WorktreeNodeAction.ReturnStage : WorktreeNodeAction.PutOnStage;
            this.primaryActionButton.text = isOnStage ? "⏏" : "▶";
            this.primaryActionButton.tooltip = isOnStage ? "Return stage" : "Put on stage";
            this.primaryActionButton.SetEnabled(!stageIsBusy);
            this.primaryActionButton.style.display = DisplayStyle.Flex;
        }

        public void BindTrunk(StageDto stage, string trunkBranch, bool isOnStage)
        {
            this.isTrunkCard = true;

            this.titleLabel.text = trunkBranch;

            this.modelsLabel.style.display = DisplayStyle.None;
            this.countsLabel.style.display = DisplayStyle.None;

            string statusText;
            if (stage.busyWith != null && stage.busyWith.IsHeld)
            {
                statusText = $"busy: {stage.busyWith.holder}";
            }
            else
            {
                statusText = stage.dirtyTracked == 0 ? "stage clean" : $"{stage.dirtyTracked} modified";
            }

            this.statusLabel.text = statusText;
            this.statusLabel.tooltip = statusText;

            this.EnableInClassList("worktree-node--trunk", true);
            this.EnableInClassList("worktree-node--on-stage", isOnStage);
            this.EnableInClassList("worktree-node--inactive", !isOnStage);
            this.EnableInClassList("worktree-node--stale", false);
            this.EnableInClassList("worktree-node--missing", false);

            this.primaryAction = WorktreeNodeAction.ReturnStage;
            this.primaryActionButton.text = "⏏";
            this.primaryActionButton.tooltip = "Return stage";
            this.primaryActionButton.SetEnabled(true);
            this.primaryActionButton.style.display = isOnStage ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void PopulateMoreActionsMenu(ContextualMenuPopulateEvent populateEvent)
        {
            if (!this.isTrunkCard)
            {
                populateEvent.menu.AppendAction("Merge", menuAction =>
                    this.ActionRequested?.Invoke(this.NodeId, WorktreeNodeAction.Merge));
                populateEvent.menu.AppendAction("Remove", menuAction =>
                    this.ActionRequested?.Invoke(this.NodeId, WorktreeNodeAction.Remove));
            }

            populateEvent.menu.AppendAction("Reveal", menuAction =>
                this.ActionRequested?.Invoke(this.NodeId, WorktreeNodeAction.Reveal));
        }
    }
}
