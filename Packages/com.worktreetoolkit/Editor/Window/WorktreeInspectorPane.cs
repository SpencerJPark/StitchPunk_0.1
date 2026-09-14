using System;
using System.IO;
using UnityEngine.UIElements;

namespace WorktreeToolkit.Editor
{
    /// <summary>
    /// The fixed inspector pane of the graph window: shows either the trunk, a selected worktree,
    /// or a "nothing selected" hint. Built once; Show* methods only mutate text/tooltip/visibility.
    /// </summary>
    public sealed class WorktreeInspectorPane : VisualElement
    {
        private readonly Label titleLabel;
        private readonly Label statusLabel;

        private readonly VisualElement specRow;
        private readonly Label specValueLabel;
        private readonly VisualElement modelsRow;
        private readonly Label modelsValueLabel;
        private readonly VisualElement aheadBehindRow;
        private readonly Label aheadBehindValueLabel;
        private readonly Label uncommittedValueLabel;
        private readonly Label pathValueLabel;

        private readonly Button putOnStageButton;
        private readonly Button returnToTrunkButton;
        private readonly Button mergeButton;
        private readonly Button removeButton;
        private readonly Button revealButton;

        private readonly Label emptyHintLabel;
        private readonly VisualElement contentContainer;

        private string currentNodeId;

        public event Action<string, WorktreeNodeAction> ActionRequested;

        public WorktreeInspectorPane()
        {
            AddToClassList("worktree-inspector");
            style.flexGrow = 1f;

            this.titleLabel = new Label { name = "worktree-inspector-title" };
            this.titleLabel.AddToClassList("worktree-inspector__title");

            this.statusLabel = new Label { name = "worktree-inspector-status" };
            this.statusLabel.AddToClassList("worktree-inspector__status");

            VisualElement keyValueContainer = new VisualElement { name = "worktree-inspector-kv-container" };

            this.specValueLabel = new Label();
            this.specRow = this.CreateKeyValueRow("Spec", this.specValueLabel);

            this.modelsValueLabel = new Label();
            this.modelsRow = this.CreateKeyValueRow("Models", this.modelsValueLabel);

            this.aheadBehindValueLabel = new Label();
            this.aheadBehindRow = this.CreateKeyValueRow("Ahead / Behind", this.aheadBehindValueLabel);

            this.uncommittedValueLabel = new Label();
            VisualElement uncommittedRow = this.CreateKeyValueRow("Uncommitted", this.uncommittedValueLabel);

            this.pathValueLabel = new Label();
            this.pathValueLabel.RegisterCallback<ClickEvent>(this.OnPathValueClicked);
            VisualElement pathRow = this.CreateKeyValueRow("Path", this.pathValueLabel);

            keyValueContainer.Add(this.specRow);
            keyValueContainer.Add(this.modelsRow);
            keyValueContainer.Add(this.aheadBehindRow);
            keyValueContainer.Add(uncommittedRow);
            keyValueContainer.Add(pathRow);

            VisualElement ruleElement = new VisualElement { name = "worktree-inspector-rule" };
            ruleElement.AddToClassList("worktree-inspector__rule");

            this.putOnStageButton = WorktreeIcons.MakeIconTextButton(
                () => this.RaiseActionRequested(WorktreeNodeAction.PutOnStage),
                WorktreeIcons.SelectionEye, "Put this worktree on stage", "Put on stage");
            this.putOnStageButton.AddToClassList("worktree-action-button");
            this.putOnStageButton.AddToClassList("worktree-action-button--teal");

            this.returnToTrunkButton = WorktreeIcons.MakeIconTextButton(
                () => this.RaiseActionRequested(WorktreeNodeAction.ReturnStage),
                WorktreeIcons.ReturnStage, "Return the stage to trunk", "Return to trunk");
            this.returnToTrunkButton.AddToClassList("worktree-action-button");
            this.returnToTrunkButton.AddToClassList("worktree-action-button--teal");

            this.mergeButton = WorktreeIcons.MakeIconTextButton(
                () => this.RaiseActionRequested(WorktreeNodeAction.Merge),
                WorktreeIcons.Merge, "Merge this worktree", "Merge");
            this.mergeButton.AddToClassList("worktree-action-button");
            this.mergeButton.AddToClassList("worktree-action-button--teal");

            this.removeButton = WorktreeIcons.MakeIconTextButton(
                () => this.RaiseActionRequested(WorktreeNodeAction.Remove),
                WorktreeIcons.Remove, "Remove this worktree", "Remove");
            this.removeButton.AddToClassList("worktree-action-button");
            this.removeButton.AddToClassList("worktree-action-button--crimson");

            this.revealButton = WorktreeIcons.MakeIconTextButton(
                () => this.RaiseActionRequested(WorktreeNodeAction.Reveal),
                WorktreeIcons.Reveal, "Reveal in file explorer", "Reveal");
            this.revealButton.AddToClassList("worktree-action-button");
            this.revealButton.AddToClassList("worktree-action-button--neutral");

            this.contentContainer = new VisualElement { name = "worktree-inspector-content" };
            this.contentContainer.Add(this.titleLabel);
            this.contentContainer.Add(this.statusLabel);
            this.contentContainer.Add(keyValueContainer);
            this.contentContainer.Add(ruleElement);
            this.contentContainer.Add(this.putOnStageButton);
            this.contentContainer.Add(this.returnToTrunkButton);
            this.contentContainer.Add(this.mergeButton);
            this.contentContainer.Add(this.removeButton);
            this.contentContainer.Add(this.revealButton);

            this.emptyHintLabel = new Label("Select a tile") { name = "worktree-inspector-empty-hint" };
            this.emptyHintLabel.AddToClassList("worktree-empty-hint");

            this.Add(this.contentContainer);
            this.Add(this.emptyHintLabel);

            this.ShowNothingSelected();
        }

        private VisualElement CreateKeyValueRow(string keyText, Label valueLabel)
        {
            VisualElement rowElement = new VisualElement();
            rowElement.AddToClassList("worktree-kv");

            Label keyLabel = new Label(keyText);
            keyLabel.AddToClassList("worktree-kv__key");

            valueLabel.AddToClassList("worktree-kv__value");

            rowElement.Add(keyLabel);
            rowElement.Add(valueLabel);
            return rowElement;
        }

        public void ShowNothingSelected()
        {
            this.currentNodeId = null;
            this.contentContainer.style.display = DisplayStyle.None;
            this.emptyHintLabel.style.display = DisplayStyle.Flex;
        }

        public void ShowTrunk(StageDto stage, string trunkBranch, bool trunkIsOnStage)
        {
            this.currentNodeId = null;
            this.contentContainer.style.display = DisplayStyle.Flex;
            this.emptyHintLabel.style.display = DisplayStyle.None;

            this.titleLabel.text = (trunkBranch ?? string.Empty).ToUpperInvariant();
            this.statusLabel.text = trunkIsOnStage ? "trunk · on stage" : "trunk";

            this.specRow.style.display = DisplayStyle.None;
            this.modelsRow.style.display = DisplayStyle.None;
            this.aheadBehindRow.style.display = DisplayStyle.None;

            this.uncommittedValueLabel.text = stage.dirtyTracked.ToString();
            this.pathValueLabel.text = stage.path;
            this.pathValueLabel.tooltip = stage.path;

            // Trunk is already the stage; the only stage-move offered here is returning to it when
            // the stage currently sits on some other worktree.
            this.putOnStageButton.style.display = DisplayStyle.None;
            this.returnToTrunkButton.style.display = trunkIsOnStage ? DisplayStyle.None : DisplayStyle.Flex;
            this.returnToTrunkButton.SetEnabled(true);
            this.returnToTrunkButton.tooltip = "Return the stage to trunk";

            this.mergeButton.style.display = DisplayStyle.None;
            this.removeButton.style.display = DisplayStyle.None;

            this.revealButton.style.display = DisplayStyle.Flex;
            this.revealButton.SetEnabled(true);
            this.revealButton.tooltip = "Reveal in file explorer";
        }

        public void ShowWorktree(WorktreeNodeDto node, bool isOnStage, bool stageIsBusy)
        {
            this.currentNodeId = node.id;
            this.contentContainer.style.display = DisplayStyle.Flex;
            this.emptyHintLabel.style.display = DisplayStyle.None;

            this.titleLabel.text = (node.branch ?? string.Empty).ToUpperInvariant();
            this.statusLabel.text = string.IsNullOrEmpty(node.lead?.status) ? "unclaimed" : node.lead.status;

            this.specRow.style.display = DisplayStyle.Flex;
            this.modelsRow.style.display = DisplayStyle.Flex;
            this.aheadBehindRow.style.display = DisplayStyle.Flex;

            bool leadIsBound = node.lead != null && node.lead.IsBound;
            this.specValueLabel.text = leadIsBound && !string.IsNullOrEmpty(node.lead.spec)
                ? Path.GetFileName(node.lead.spec)
                : "unclaimed";

            this.modelsValueLabel.text = leadIsBound && !string.IsNullOrEmpty(node.lead.leadModel)
                ? node.lead.leadModel + " ▸ " + node.lead.workerModel
                : "—";

            this.aheadBehindValueLabel.text = node.ahead + " · " + node.behind;
            this.uncommittedValueLabel.text = node.dirty.ToString();
            this.pathValueLabel.text = node.path;
            this.pathValueLabel.tooltip = node.path;

            this.putOnStageButton.style.display = isOnStage ? DisplayStyle.None : DisplayStyle.Flex;
            this.returnToTrunkButton.style.display = isOnStage ? DisplayStyle.Flex : DisplayStyle.None;
            this.mergeButton.style.display = DisplayStyle.Flex;
            this.removeButton.style.display = DisplayStyle.Flex;
            this.revealButton.style.display = DisplayStyle.Flex;

            bool leadIsBusy = string.Equals(node.lead?.status, "building", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.lead?.status, "gating", StringComparison.OrdinalIgnoreCase);

            this.putOnStageButton.SetEnabled(true);
            this.putOnStageButton.tooltip = "Put this worktree on stage";
            this.returnToTrunkButton.SetEnabled(true);
            this.returnToTrunkButton.tooltip = "Return the stage to trunk";
            this.mergeButton.SetEnabled(true);
            this.mergeButton.tooltip = "Merge this worktree";
            this.removeButton.SetEnabled(true);
            this.removeButton.tooltip = "Remove this worktree";
            this.revealButton.SetEnabled(true);
            this.revealButton.tooltip = "Reveal in file explorer";

            if (stageIsBusy)
            {
                this.putOnStageButton.SetEnabled(false);
                this.putOnStageButton.tooltip = "Stage is busy";
                this.returnToTrunkButton.SetEnabled(false);
                this.returnToTrunkButton.tooltip = "Stage is busy";
                this.mergeButton.SetEnabled(false);
                this.mergeButton.tooltip = "Stage is busy";
            }

            if (leadIsBusy)
            {
                this.putOnStageButton.SetEnabled(false);
                this.putOnStageButton.tooltip = "lead still running";
                this.mergeButton.SetEnabled(false);
                this.mergeButton.tooltip = "lead still running";
            }

            if (node.parked)
            {
                this.mergeButton.SetEnabled(false);
                this.mergeButton.tooltip = "parked for review";
                this.removeButton.SetEnabled(false);
                this.removeButton.tooltip = "parked for review";
            }

            if (node.stale || node.missing)
            {
                this.mergeButton.SetEnabled(false);
                this.mergeButton.tooltip = node.missing ? "worktree is missing" : "worktree is stale";
            }
        }

        private void OnPathValueClicked(ClickEvent clickEvent)
        {
            this.RaiseActionRequested(WorktreeNodeAction.Reveal);
        }

        private void RaiseActionRequested(WorktreeNodeAction action)
        {
            this.ActionRequested?.Invoke(this.currentNodeId, action);
        }
    }
}
