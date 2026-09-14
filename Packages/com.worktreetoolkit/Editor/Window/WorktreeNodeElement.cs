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

    public enum WorktreeTileState
    {
        Locked,
        Unlocked,
        OnStage
    }

    /// <summary>
    /// One skill-tree tile: either a worktree node (BindWorktree) or the single trunk node (BindTrunk).
    /// Built once in the constructor; Bind*/SetSelected/Tick only mutate text, images, colours and classes.
    /// </summary>
    public sealed class WorktreeNodeElement : VisualElement
    {
        public const float SlotWidth = 168f;
        public const float SlotHeight = 156f;
        public const float TileSize = 96f;

        private const float GlyphSize = 52f;
        private const float GlyphStrokeWidth = 3.5f;
        private const float LockGlyphSize = 20f;
        private const float LockGlyphStrokeWidth = 2f;
        private const float GatingPulsePeriodSeconds = 1.2f;

        public string NodeId { get; }

        public WorktreeTileState TileState { get; private set; }

        public event Action<string> SelectionRequested;
        public event Action<string, WorktreeNodeAction> ActionRequested;

        private readonly VisualElement selectionOutline;
        private readonly VisualElement tileElement;
        private readonly WorktreeGlyphElement tileGlyphElement;
        private readonly WorktreeGlyphElement lockGlyphElement;
        private readonly VisualElement gatingPipElement;
        private readonly Label ribbonLabel;
        private readonly Label captionLabel;

        private bool isTrunkNode;
        private bool isCurrentlyOnStage;
        private bool isStageBusy;
        private bool isSelected;
        private bool isSpinnerActive;
        private bool isGatingActive;
        private string fullBranchName;
        private string shortBranchName;
        private string trunkBranchName = "main";

        public WorktreeNodeElement(string nodeId)
        {
            this.NodeId = nodeId;

            this.style.width = SlotWidth;
            this.style.height = SlotHeight;
            this.style.flexDirection = FlexDirection.Column;
            this.style.alignItems = Align.Center;
            this.pickingMode = PickingMode.Position;
            this.AddToClassList("worktree-node");

            this.selectionOutline = new VisualElement();
            this.selectionOutline.AddToClassList("worktree-node__selection");
            this.selectionOutline.style.position = Position.Absolute;
            this.selectionOutline.style.width = TileSize + 12f;
            this.selectionOutline.style.height = TileSize + 12f;
            this.selectionOutline.style.left = (SlotWidth - (TileSize + 12f)) / 2f;
            this.selectionOutline.style.top = -6f;
            this.selectionOutline.style.display = DisplayStyle.None;
            this.selectionOutline.pickingMode = PickingMode.Ignore;
            this.Add(this.selectionOutline);

            this.tileElement = new VisualElement();
            this.tileElement.AddToClassList("worktree-node__tile");
            this.tileElement.style.width = TileSize;
            this.tileElement.style.height = TileSize;
            this.tileElement.style.justifyContent = Justify.Center;
            this.tileElement.style.alignItems = Align.Center;
            this.Add(this.tileElement);

            this.tileGlyphElement = new WorktreeGlyphElement();
            this.tileGlyphElement.AddToClassList("worktree-node__glyph");
            this.tileGlyphElement.style.width = GlyphSize;
            this.tileGlyphElement.style.height = GlyphSize;
            this.tileGlyphElement.StrokeWidth = GlyphStrokeWidth;
            this.tileElement.Add(this.tileGlyphElement);

            this.lockGlyphElement = new WorktreeGlyphElement();
            this.lockGlyphElement.AddToClassList("worktree-node__lock");
            this.lockGlyphElement.style.width = LockGlyphSize;
            this.lockGlyphElement.style.height = LockGlyphSize;
            this.lockGlyphElement.Glyph = WorktreeGlyph.Padlock;
            this.lockGlyphElement.StrokeWidth = LockGlyphStrokeWidth;
            this.lockGlyphElement.GlyphColor = WorktreePalette.Cream;
            this.lockGlyphElement.style.position = Position.Absolute;
            this.lockGlyphElement.style.top = 2f;
            this.lockGlyphElement.style.right = 2f;
            this.lockGlyphElement.style.display = DisplayStyle.None;
            this.tileElement.Add(this.lockGlyphElement);

            this.ribbonLabel = new Label("ON STAGE");
            this.ribbonLabel.AddToClassList("worktree-node__ribbon");
            this.ribbonLabel.style.position = Position.Absolute;
            this.ribbonLabel.style.left = 0f;
            this.ribbonLabel.style.right = 0f;
            this.ribbonLabel.style.bottom = 0f;
            this.ribbonLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            this.ribbonLabel.style.display = DisplayStyle.None;
            this.ribbonLabel.pickingMode = PickingMode.Ignore;
            this.tileElement.Add(this.ribbonLabel);

            this.gatingPipElement = new VisualElement();
            this.gatingPipElement.AddToClassList("worktree-node__pip");
            this.gatingPipElement.style.position = Position.Absolute;
            this.gatingPipElement.style.top = 2f;
            this.gatingPipElement.style.left = 2f;
            this.gatingPipElement.style.width = 8f;
            this.gatingPipElement.style.height = 8f;
            this.gatingPipElement.style.borderTopLeftRadius = 4f;
            this.gatingPipElement.style.borderTopRightRadius = 4f;
            this.gatingPipElement.style.borderBottomLeftRadius = 4f;
            this.gatingPipElement.style.borderBottomRightRadius = 4f;
            this.gatingPipElement.style.backgroundColor = WorktreePalette.Amber;
            this.gatingPipElement.style.display = DisplayStyle.None;
            this.gatingPipElement.pickingMode = PickingMode.Ignore;
            this.tileElement.Add(this.gatingPipElement);

            this.captionLabel = new Label();
            this.captionLabel.AddToClassList("worktree-node__caption");
            this.captionLabel.style.marginTop = 6f;
            this.Add(this.captionLabel);

            this.RegisterCallback<ClickEvent>(this.OnTileClicked);
            this.AddManipulator(new ContextualMenuManipulator(this.PopulateContextMenu));
        }

        public void BindWorktree(WorktreeNodeDto node, bool isOnStage, bool stageIsBusy)
        {
            this.isTrunkNode = false;
            this.isCurrentlyOnStage = isOnStage;
            this.isStageBusy = stageIsBusy;

            this.fullBranchName = node.branch;
            this.shortBranchName = ShortenBranchName(node.branch);

            bool isLeadBound = node.lead != null && node.lead.IsBound;
            string leadStatusText = isLeadBound ? node.lead.status : null;

            // Locked wins over on-stage: a tile can be pinned to the stage yet still be unusable
            // (e.g. the worktree went missing while it was checked out).
            // A hand-made worktree has no lead and is still usable; only a hook-made one that was never claimed is locked.
            bool isLockedTile = node.stale || node.missing || node.locked || node.parked
                || string.Equals(leadStatusText, "unclaimed", StringComparison.OrdinalIgnoreCase);

            WorktreeTileState newTileState = isLockedTile
                ? WorktreeTileState.Locked
                : (isOnStage ? WorktreeTileState.OnStage : WorktreeTileState.Unlocked);

            this.ApplyTileState(newTileState);
            this.ApplyCentralIcon(isTrunkNodeIcon: false, leadStatusText: leadStatusText);

            string plainLanguageStateLine = isLockedTile
                ? $"Locked: {DescribeLockReason(node, isLeadBound, leadStatusText)}"
                : (isOnStage ? "Open in your Editor" : "Ready to act on — click for details");

            string tooltipText = $"{plainLanguageStateLine}\n{node.branch}\n{(isLeadBound ? node.lead.status : "unclaimed")}\n" +
                $"ahead {node.ahead} · behind {node.behind} · dirty {node.dirty}";

            if (stageIsBusy)
            {
                tooltipText += "\nstage busy";
            }

            this.tileElement.tooltip = tooltipText;

            this.RefreshCaptionText();
        }

        public void BindTrunk(StageDto stage, string trunkBranch, bool isOnStage)
        {
            this.isTrunkNode = true;
            this.isCurrentlyOnStage = isOnStage;
            this.isStageBusy = stage.busyWith != null && stage.busyWith.IsHeld;

            this.fullBranchName = trunkBranch;
            this.shortBranchName = ShortenBranchName(trunkBranch);

            WorktreeTileState newTileState = isOnStage ? WorktreeTileState.OnStage : WorktreeTileState.Unlocked;
            this.ApplyTileState(newTileState);
            this.ApplyCentralIcon(isTrunkNodeIcon: true, leadStatusText: null);

            string plainLanguageStateLine = isOnStage ? "Open in your Editor" : "Ready to act on — click for details";
            string statusText = this.isStageBusy
                ? $"busy: {stage.busyWith.holder}"
                : (stage.dirtyTracked == 0 ? "stage clean" : $"{stage.dirtyTracked} modified");
            this.tileElement.tooltip = $"{plainLanguageStateLine}\n{trunkBranch}\n{statusText}";

            this.RefreshCaptionText();
        }

        public void SetTrunkBranch(string trunkBranch)
        {
            this.trunkBranchName = string.IsNullOrEmpty(trunkBranch) ? "main" : trunkBranch;
        }

        public void SetSelected(bool isSelectedNow)
        {
            this.isSelected = isSelectedNow;
            this.selectionOutline.style.display = isSelectedNow ? DisplayStyle.Flex : DisplayStyle.None;
            this.RefreshCaptionText();
        }

        public void Tick(double editorTimeSeconds)
        {
            if (this.isSpinnerActive)
            {
                this.tileGlyphElement.SpinnerAngleRadians = (float)(editorTimeSeconds * Mathf.PI * 2.0 * 0.8);
            }

            if (this.isGatingActive)
            {
                float sineWave = Mathf.Sin((float)(editorTimeSeconds * Mathf.PI * 2.0 / GatingPulsePeriodSeconds));
                this.gatingPipElement.style.opacity = 0.35f + 0.65f * (0.5f + 0.5f * sineWave);
            }
        }

        private void ApplyTileState(WorktreeTileState newTileState)
        {
            this.TileState = newTileState;

            this.tileElement.EnableInClassList("worktree-node__tile--locked", newTileState == WorktreeTileState.Locked);
            this.tileElement.EnableInClassList("worktree-node__tile--unlocked", newTileState == WorktreeTileState.Unlocked);
            this.tileElement.EnableInClassList("worktree-node__tile--on-stage", newTileState == WorktreeTileState.OnStage);

            this.lockGlyphElement.style.display = newTileState == WorktreeTileState.Locked ? DisplayStyle.Flex : DisplayStyle.None;
            this.ribbonLabel.style.display = newTileState == WorktreeTileState.OnStage ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ApplyCentralIcon(bool isTrunkNodeIcon, string leadStatusText)
        {
            this.isSpinnerActive = false;
            this.isGatingActive = false;
            this.gatingPipElement.style.display = DisplayStyle.None;

            if (isTrunkNodeIcon)
            {
                this.tileGlyphElement.Glyph = WorktreeGlyph.BranchFork;
                this.tileGlyphElement.GlyphColor = this.TileState == WorktreeTileState.Locked ? WorktreePalette.CreamDim : WorktreePalette.Cream;
                return;
            }

            string normalizedStatus = string.IsNullOrEmpty(leadStatusText) ? string.Empty : leadStatusText.ToLowerInvariant();
            switch (normalizedStatus)
            {
                case "building":
                    this.isSpinnerActive = true;
                    this.tileGlyphElement.Glyph = WorktreeGlyph.Spinner;
                    break;
                case "gating":
                    this.isSpinnerActive = true;
                    this.isGatingActive = true;
                    this.gatingPipElement.style.display = DisplayStyle.Flex;
                    this.tileGlyphElement.Glyph = WorktreeGlyph.Spinner;
                    break;
                case "ready":
                case "done":
                    this.tileGlyphElement.Glyph = WorktreeGlyph.Check;
                    break;
                case "failed":
                    this.tileGlyphElement.Glyph = WorktreeGlyph.Cross;
                    break;
                case "unclaimed":
                    this.tileGlyphElement.Glyph = WorktreeGlyph.DashedRing;
                    break;
                default:
                    // No lead bound at all: a hand-made worktree, nothing tracking it yet.
                    this.tileGlyphElement.Glyph = WorktreeGlyph.BranchFork;
                    break;
            }

            this.tileGlyphElement.GlyphColor = this.TileState == WorktreeTileState.Locked ? WorktreePalette.CreamDim : WorktreePalette.Cream;
        }

        private void RefreshCaptionText()
        {
            this.captionLabel.text = this.isTrunkNode
                ? $"{this.fullBranchName} · trunk"
                : (this.isSelected ? this.fullBranchName : this.shortBranchName);
            this.captionLabel.EnableInClassList("worktree-node__caption--selected", this.isSelected);
        }

        private void OnTileClicked(ClickEvent clickEvent)
        {
            clickEvent.StopPropagation();

            if (clickEvent.clickCount == 1)
            {
                this.SelectionRequested?.Invoke(this.NodeId);
            }
            else if (clickEvent.clickCount == 2)
            {
                WorktreeNodeAction doubleClickAction = this.isTrunkNode ? WorktreeNodeAction.ReturnStage : WorktreeNodeAction.PutOnStage;
                this.ActionRequested?.Invoke(this.NodeId, doubleClickAction);
            }
        }

        private void PopulateContextMenu(ContextualMenuPopulateEvent populateEvent)
        {
            if (this.isTrunkNode)
            {
                if (!this.isCurrentlyOnStage)
                {
                    populateEvent.menu.AppendAction($"Back to {this.trunkBranchName}", trunkReturnAction =>
                        this.ActionRequested?.Invoke(this.NodeId, WorktreeNodeAction.ReturnStage));
                }

                populateEvent.menu.AppendAction("Show in Explorer", trunkRevealAction =>
                    this.ActionRequested?.Invoke(this.NodeId, WorktreeNodeAction.Reveal));
                return;
            }

            string stageMenuLabel = this.isCurrentlyOnStage ? $"Back to {this.trunkBranchName}" : "Open in Editor";
            WorktreeNodeAction stageMenuAction = this.isCurrentlyOnStage ? WorktreeNodeAction.ReturnStage : WorktreeNodeAction.PutOnStage;

            populateEvent.menu.AppendAction(stageMenuLabel, stageAction =>
                this.ActionRequested?.Invoke(this.NodeId, stageMenuAction));
            populateEvent.menu.AppendAction($"Merge into {this.trunkBranchName}", mergeAction =>
                this.ActionRequested?.Invoke(this.NodeId, WorktreeNodeAction.Merge));
            populateEvent.menu.AppendAction("Delete worktree", removeAction =>
                this.ActionRequested?.Invoke(this.NodeId, WorktreeNodeAction.Remove));
            populateEvent.menu.AppendAction("Show in Explorer", revealAction =>
                this.ActionRequested?.Invoke(this.NodeId, WorktreeNodeAction.Reveal));
        }

        private static string ShortenBranchName(string branchName)
        {
            if (string.IsNullOrEmpty(branchName))
            {
                return branchName;
            }

            int lastSlashIndex = branchName.LastIndexOf('/');
            return lastSlashIndex >= 0 ? branchName.Substring(lastSlashIndex + 1) : branchName;
        }

        private static string DescribeLockReason(WorktreeNodeDto node, bool isLeadBound, string leadStatusText)
        {
            if (node.missing)
            {
                return "missing";
            }

            if (node.locked)
            {
                return "locked";
            }

            if (node.stale)
            {
                return "stale";
            }

            if (node.parked)
            {
                return "parked";
            }

            if (string.Equals(leadStatusText, "unclaimed", StringComparison.OrdinalIgnoreCase))
            {
                return "lead unclaimed";
            }

            return "locked";
        }
    }
}
