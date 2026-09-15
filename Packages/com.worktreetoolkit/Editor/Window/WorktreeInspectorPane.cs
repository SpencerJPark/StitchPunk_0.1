using System;
using System.IO;
using UnityEditor;
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
        // The label shows a short project-relative path; Copy and the tooltip use the full one.
        private string currentFullPath = string.Empty;

        private readonly Button putOnStageButton;
        private readonly Label putOnStageDescriptionLabel;

        private readonly Button returnToTrunkButton;
        private readonly Label returnToTrunkTitleLabel;
        private readonly Label returnToTrunkDescriptionLabel;

        private readonly Button mergeButton;
        private readonly Label mergeTitleLabel;
        private readonly Label mergeDescriptionLabel;

        private readonly Button removeButton;
        private readonly Label removeDescriptionLabel;

        private readonly Button revealButton;
        private readonly Label revealDescriptionLabel;

        private readonly Label emptyHintLabel;
        private readonly VisualElement contentContainer;

        private string currentNodeId;
        private string trunkBranchName = "main";

        public event Action<string, WorktreeNodeAction> ActionRequested;

        public WorktreeInspectorPane()
        {
            AddToClassList("worktree-inspector");
            style.flexGrow = 1f;

            this.titleLabel = new Label { name = "worktree-inspector-title" };
            this.titleLabel.AddToClassList("worktree-inspector__title");

            this.statusLabel = new Label { name = "worktree-inspector-status" };
            this.statusLabel.AddToClassList("worktree-inspector__status");
            this.statusLabel.style.whiteSpace = WhiteSpace.Normal;

            Label detailsSectionLabel = new Label("DETAILS");
            detailsSectionLabel.AddToClassList("worktree-inspector__section");

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
            // A path has no spaces to wrap at, so it ellipsizes instead of shoving Copy off the pane.
            this.pathValueLabel.style.whiteSpace = WhiteSpace.NoWrap;
            this.pathValueLabel.style.overflow = Overflow.Hidden;
            this.pathValueLabel.style.textOverflow = TextOverflow.Ellipsis;
            this.pathValueLabel.style.flexShrink = 1f;
            this.pathValueLabel.style.minWidth = 0f;
            this.pathValueLabel.RegisterCallback<ClickEvent>(this.OnPathValueClicked);
            VisualElement pathRow = this.CreateKeyValueRow("Path", this.pathValueLabel);

            Button copyPathButton = new Button(this.OnCopyPathClicked) { text = "Copy" };
            copyPathButton.AddToClassList("worktree-kv__copy");
            copyPathButton.style.flexShrink = 0f;
            pathRow.Add(copyPathButton);

            keyValueContainer.Add(this.specRow);
            keyValueContainer.Add(this.modelsRow);
            keyValueContainer.Add(this.aheadBehindRow);
            keyValueContainer.Add(uncommittedRow);
            keyValueContainer.Add(pathRow);

            Label actionsSectionLabel = new Label("ACTIONS");
            actionsSectionLabel.AddToClassList("worktree-inspector__section");

            this.putOnStageButton = this.CreateActionBox(
                WorktreeIcons.SelectionEye, "worktree-action--teal", "Open in Editor",
                () => this.RaiseActionRequested(WorktreeNodeAction.PutOnStage),
                out _, out this.putOnStageDescriptionLabel);

            this.returnToTrunkButton = this.CreateActionBox(
                WorktreeIcons.ReturnStage, "worktree-action--teal", "Back to " + this.trunkBranchName,
                () => this.RaiseActionRequested(WorktreeNodeAction.ReturnStage),
                out this.returnToTrunkTitleLabel, out this.returnToTrunkDescriptionLabel);

            this.mergeButton = this.CreateActionBox(
                WorktreeIcons.Merge, "worktree-action--teal", "Merge into " + this.trunkBranchName,
                () => this.RaiseActionRequested(WorktreeNodeAction.Merge),
                out this.mergeTitleLabel, out this.mergeDescriptionLabel);

            this.removeButton = this.CreateActionBox(
                WorktreeIcons.Remove, "worktree-action--crimson", "Delete worktree",
                () => this.RaiseActionRequested(WorktreeNodeAction.Remove),
                out _, out this.removeDescriptionLabel);

            this.revealButton = this.CreateActionBox(
                WorktreeIcons.Reveal, "worktree-action--neutral", "Show in Explorer",
                () => this.RaiseActionRequested(WorktreeNodeAction.Reveal),
                out _, out this.revealDescriptionLabel);

            this.contentContainer = new VisualElement { name = "worktree-inspector-content" };
            this.contentContainer.Add(this.titleLabel);
            this.contentContainer.Add(this.statusLabel);
            this.contentContainer.Add(detailsSectionLabel);
            this.contentContainer.Add(keyValueContainer);
            this.contentContainer.Add(actionsSectionLabel);
            this.contentContainer.Add(this.putOnStageButton);
            this.contentContainer.Add(this.returnToTrunkButton);
            this.contentContainer.Add(this.mergeButton);
            this.contentContainer.Add(this.removeButton);
            this.contentContainer.Add(this.revealButton);

            this.emptyHintLabel = new Label("Select a tile to see its details and actions.") { name = "worktree-inspector-empty-hint" };
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

        // Every action reads as a labeled box (icon + title + one-line description) instead of a bare
        // icon button, so the owner can tell what a control does without hovering for a tooltip.
        private Button CreateActionBox(
            string iconName,
            string colorModifierClass,
            string titleText,
            Action onClick,
            out Label titleLabelOut,
            out Label descriptionLabelOut)
        {
            Button actionButton = new Button(onClick) { text = string.Empty };
            actionButton.AddToClassList("worktree-action");
            actionButton.AddToClassList(colorModifierClass);
            actionButton.style.flexDirection = FlexDirection.Row;
            actionButton.style.alignItems = Align.FlexStart;

            Image actionIconImage = WorktreeIcons.MakeIcon(iconName, 16f);
            actionIconImage.AddToClassList("worktree-action__icon");

            VisualElement actionTextColumn = new VisualElement();
            actionTextColumn.AddToClassList("worktree-action__text");
            actionTextColumn.style.flexGrow = 1f;

            Label actionTitleLabel = new Label(titleText);
            actionTitleLabel.AddToClassList("worktree-action__title");

            Label actionDescriptionLabel = new Label();
            actionDescriptionLabel.AddToClassList("worktree-action__description");
            actionDescriptionLabel.style.whiteSpace = WhiteSpace.Normal;

            actionTextColumn.Add(actionTitleLabel);
            actionTextColumn.Add(actionDescriptionLabel);

            actionButton.Add(actionIconImage);
            actionButton.Add(actionTextColumn);

            titleLabelOut = actionTitleLabel;
            descriptionLabelOut = actionDescriptionLabel;
            return actionButton;
        }

        // A disabled action stays visible (so the owner still sees it exists) but its description
        // swaps to the reason it is blocked, rather than hiding the control entirely.
        private void ApplyActionDescription(Label descriptionLabel, string normalDescription, string blockedReason)
        {
            bool isBlocked = !string.IsNullOrEmpty(blockedReason);
            descriptionLabel.text = isBlocked ? "Unavailable: " + blockedReason + "." : normalDescription;
            descriptionLabel.EnableInClassList("worktree-action__description--blocked", isBlocked);
        }

        public void SetTrunkBranch(string trunkBranch)
        {
            this.trunkBranchName = string.IsNullOrEmpty(trunkBranch) ? "main" : trunkBranch;
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

            string trunkDisplayName = string.IsNullOrEmpty(trunkBranch) ? this.trunkBranchName : trunkBranch;
            this.titleLabel.text = trunkDisplayName.ToUpperInvariant();

            string trunkStatusSentence = "Trunk — the branch your Editor normally shows.";
            this.statusLabel.text = trunkIsOnStage
                ? trunkStatusSentence + " It is open in your Editor now."
                : trunkStatusSentence;

            this.specRow.style.display = DisplayStyle.None;
            this.modelsRow.style.display = DisplayStyle.None;
            this.aheadBehindRow.style.display = DisplayStyle.None;

            this.uncommittedValueLabel.text = stage.dirtyTracked.ToString();
            this.SetPathValue(stage.path);

            // Trunk is already the stage; the only stage-move offered here is returning to it when
            // the stage currently sits on some other worktree.
            this.putOnStageButton.style.display = DisplayStyle.None;

            this.returnToTrunkButton.style.display = trunkIsOnStage ? DisplayStyle.None : DisplayStyle.Flex;
            this.returnToTrunkTitleLabel.text = "Back to " + trunkDisplayName;
            this.returnToTrunkButton.SetEnabled(true);
            this.ApplyActionDescription(
                this.returnToTrunkDescriptionLabel, "Switch this Editor back to " + trunkDisplayName + ".", null);

            this.mergeButton.style.display = DisplayStyle.None;
            this.removeButton.style.display = DisplayStyle.None;

            this.revealButton.style.display = DisplayStyle.Flex;
            this.revealButton.SetEnabled(true);
            this.ApplyActionDescription(this.revealDescriptionLabel, "Open the worktree folder.", null);
        }

        public void ShowWorktree(WorktreeNodeDto node, bool isOnStage, bool stageIsBusy)
        {
            this.currentNodeId = node.id;
            this.contentContainer.style.display = DisplayStyle.Flex;
            this.emptyHintLabel.style.display = DisplayStyle.None;

            this.titleLabel.text = (node.branch ?? string.Empty).ToUpperInvariant();

            bool leadIsBound = node.lead != null && node.lead.IsBound;
            string leadStatusRaw = leadIsBound ? node.lead.status : null;
            string normalizedLeadStatus = string.IsNullOrEmpty(leadStatusRaw) ? string.Empty : leadStatusRaw.ToLowerInvariant();

            string leadStatusSentence;
            if (!leadIsBound)
            {
                leadStatusSentence = "Created by hand — no spec lead is attached.";
            }
            else
            {
                switch (normalizedLeadStatus)
                {
                    case "building":
                        leadStatusSentence = "Building — a spec lead is working in this worktree.";
                        break;
                    case "gating":
                        leadStatusSentence = "Checking — the Editor is compiling this worktree's latest commit.";
                        break;
                    case "ready":
                        leadStatusSentence = "Ready — the lead finished. Open it in the Editor to review, then merge.";
                        break;
                    case "failed":
                        leadStatusSentence = "Failed — the lead stopped. Read its report before deciding.";
                        break;
                    case "done":
                        leadStatusSentence = "Done — already merged.";
                        break;
                    default:
                        leadStatusSentence = "Unclaimed — no spec lead has claimed this worktree yet.";
                        break;
                }
            }

            // Stale/missing/locked/parked describe the worktree itself and take priority over
            // whatever the lead status says, since those states make the lead status moot.
            string overrideSentence = null;
            if (node.stale)
            {
                overrideSentence = "Stale — far behind " + this.trunkBranchName + " with nothing new.";
            }
            else if (node.missing)
            {
                overrideSentence = "Missing — the worktree folder is gone.";
            }
            else if (node.locked)
            {
                overrideSentence = "Locked — a Claude session is still using it.";
            }
            else if (node.parked)
            {
                overrideSentence = "Parked — its branch is open in your Editor for review.";
            }

            string statusSentence = overrideSentence ?? leadStatusSentence;
            this.statusLabel.text = isOnStage ? statusSentence + " It is open in your Editor now." : statusSentence;

            this.specRow.style.display = DisplayStyle.Flex;
            this.modelsRow.style.display = DisplayStyle.Flex;
            this.aheadBehindRow.style.display = DisplayStyle.Flex;

            if (leadIsBound && !string.IsNullOrEmpty(node.lead.spec))
            {
                this.specValueLabel.text = Path.GetFileName(node.lead.spec);
            }
            else
            {
                this.specValueLabel.text = leadIsBound ? "unclaimed" : "—";
            }

            this.modelsValueLabel.text = leadIsBound && !string.IsNullOrEmpty(node.lead.leadModel)
                ? node.lead.leadModel + " ▸ " + node.lead.workerModel
                : "—";

            this.aheadBehindValueLabel.text = node.ahead + " · " + node.behind;
            this.uncommittedValueLabel.text = node.dirty.ToString();
            this.SetPathValue(node.path);

            this.putOnStageButton.style.display = isOnStage ? DisplayStyle.None : DisplayStyle.Flex;
            this.returnToTrunkButton.style.display = isOnStage ? DisplayStyle.Flex : DisplayStyle.None;
            this.mergeButton.style.display = DisplayStyle.Flex;
            this.removeButton.style.display = DisplayStyle.Flex;
            this.revealButton.style.display = DisplayStyle.Flex;

            this.returnToTrunkTitleLabel.text = "Back to " + this.trunkBranchName;
            this.mergeTitleLabel.text = "Merge into " + this.trunkBranchName;

            bool leadIsBusy = string.Equals(node.lead?.status, "building", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.lead?.status, "gating", StringComparison.OrdinalIgnoreCase);

            string putOnStageBlockedReason = null;
            string returnToTrunkBlockedReason = null;
            string mergeBlockedReason = null;
            string removeBlockedReason = null;

            if (stageIsBusy)
            {
                putOnStageBlockedReason = "the stage is busy";
                returnToTrunkBlockedReason = "the stage is busy";
                mergeBlockedReason = "the stage is busy";
            }

            if (leadIsBusy)
            {
                putOnStageBlockedReason = "the lead is still running";
                mergeBlockedReason = "the lead is still running";
            }

            if (node.parked)
            {
                mergeBlockedReason = "this worktree is parked for review";
                removeBlockedReason = "this worktree is parked for review";
            }

            if (node.stale || node.missing)
            {
                mergeBlockedReason = node.missing
                    ? "the worktree folder is gone"
                    : "this worktree is far behind " + this.trunkBranchName;
            }

            this.putOnStageButton.SetEnabled(putOnStageBlockedReason == null);
            this.ApplyActionDescription(
                this.putOnStageDescriptionLabel, "Switch this Editor to " + node.branch + " so you can try it.", putOnStageBlockedReason);

            this.returnToTrunkButton.SetEnabled(returnToTrunkBlockedReason == null);
            this.ApplyActionDescription(
                this.returnToTrunkDescriptionLabel, "Switch this Editor back to " + this.trunkBranchName + ".", returnToTrunkBlockedReason);

            this.mergeButton.SetEnabled(mergeBlockedReason == null);
            this.ApplyActionDescription(
                this.mergeDescriptionLabel,
                "Add this branch's commits to " + this.trunkBranchName + ", keeping history linear.",
                mergeBlockedReason);

            this.removeButton.SetEnabled(removeBlockedReason == null);
            this.ApplyActionDescription(
                this.removeDescriptionLabel, "Remove the worktree folder. An unmerged branch is kept.", removeBlockedReason);

            this.revealButton.SetEnabled(true);
            this.ApplyActionDescription(this.revealDescriptionLabel, "Open the worktree folder.", null);
        }

        private void OnPathValueClicked(ClickEvent clickEvent)
        {
            this.RaiseActionRequested(WorktreeNodeAction.Reveal);
        }

        private void OnCopyPathClicked()
        {
            EditorGUIUtility.systemCopyBuffer = this.currentFullPath;
        }

        private void SetPathValue(string fullPath)
        {
            this.currentFullPath = fullPath ?? string.Empty;
            string projectRootPath = WorktreeCliClient.ProjectRootPath;
            string displayPath = this.currentFullPath;
            if (!string.IsNullOrEmpty(projectRootPath)
                && displayPath.StartsWith(projectRootPath, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = displayPath.Substring(projectRootPath.Length).TrimStart('\\', '/').Replace('\\', '/');
                displayPath = string.IsNullOrEmpty(relativePath) ? "(this project folder)" : relativePath;
            }

            this.pathValueLabel.text = displayPath;
            this.pathValueLabel.tooltip = this.currentFullPath + "  (click to show in Explorer)";
        }

        private void RaiseActionRequested(WorktreeNodeAction action)
        {
            this.ActionRequested?.Invoke(this.currentNodeId, action);
        }
    }
}
