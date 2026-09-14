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
        public const float SlotWidth = 132f;
        public const float SlotHeight = 112f;
        public const float TileSize = 72f;

        private const int RayCount = 12;
        private const float RayLengthFraction = 0.45f;
        private const float RayAlpha = 0.06f;
        private const float RayLineWidth = 2f;
        private const float GatingPulsePeriodSeconds = 1.2f;

        public string NodeId { get; }

        public WorktreeTileState TileState { get; private set; }

        public event Action<string> SelectionRequested;
        public event Action<string, WorktreeNodeAction> ActionRequested;

        private readonly VisualElement selectionOutline;
        private readonly Image selectionEyeIcon;
        private readonly VisualElement tileElement;
        private readonly Image tileIconImage;
        private readonly Image lockBadgeImage;
        private readonly VisualElement gatingPipElement;
        private readonly Label captionLabel;

        private bool isTrunkNode;
        private bool isCurrentlyOnStage;
        private bool isStageBusy;
        private bool isSelected;
        private bool isSpinnerActive;
        private bool isGatingActive;
        private string fullBranchName;
        private string shortBranchName;

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

            this.selectionEyeIcon = WorktreeIcons.MakeIcon(WorktreeIcons.SelectionEye, 14f);
            this.selectionEyeIcon.AddToClassList("worktree-node__selection-eye");
            this.selectionEyeIcon.style.position = Position.Absolute;
            this.selectionEyeIcon.style.left = (SlotWidth - 14f) / 2f;
            this.selectionEyeIcon.style.top = -6f - 14f - 4f;
            this.selectionEyeIcon.style.display = DisplayStyle.None;
            this.Add(this.selectionEyeIcon);

            this.tileElement = new VisualElement();
            this.tileElement.AddToClassList("worktree-node__tile");
            this.tileElement.style.width = TileSize;
            this.tileElement.style.height = TileSize;
            this.tileElement.style.justifyContent = Justify.Center;
            this.tileElement.style.alignItems = Align.Center;
            this.tileElement.generateVisualContent += this.OnGenerateTileVisualContent;
            this.Add(this.tileElement);

            this.tileIconImage = WorktreeIcons.MakeIcon(WorktreeIcons.Unclaimed, 32f);
            this.tileIconImage.AddToClassList("worktree-node__tile-icon");
            this.tileElement.Add(this.tileIconImage);

            this.lockBadgeImage = WorktreeIcons.MakeIcon(WorktreeIcons.LockIconName, 12f);
            this.lockBadgeImage.AddToClassList("worktree-node__lock");
            this.lockBadgeImage.style.position = Position.Absolute;
            this.lockBadgeImage.style.top = 2f;
            this.lockBadgeImage.style.right = 2f;
            this.lockBadgeImage.style.display = DisplayStyle.None;
            this.tileElement.Add(this.lockBadgeImage);

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

            string tooltipText = $"{node.branch}\n{(isLeadBound ? node.lead.status : "unclaimed")}\n" +
                $"ahead {node.ahead} · behind {node.behind} · dirty {node.dirty}";
            if (isLockedTile)
            {
                tooltipText += $"\n{DescribeLockReason(node, isLeadBound, leadStatusText)}";
            }

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

            string statusText = this.isStageBusy
                ? $"busy: {stage.busyWith.holder}"
                : (stage.dirtyTracked == 0 ? "stage clean" : $"{stage.dirtyTracked} modified");
            this.tileElement.tooltip = $"{trunkBranch}\n{statusText}";

            this.RefreshCaptionText();
        }

        public void SetSelected(bool isSelectedNow)
        {
            this.isSelected = isSelectedNow;
            this.selectionOutline.style.display = isSelectedNow ? DisplayStyle.Flex : DisplayStyle.None;
            this.selectionEyeIcon.style.display = isSelectedNow ? DisplayStyle.Flex : DisplayStyle.None;
            this.RefreshCaptionText();
        }

        public void Tick(double editorTimeSeconds)
        {
            if (this.isSpinnerActive)
            {
                this.tileIconImage.image = WorktreeIcons.SpinnerFrame(editorTimeSeconds);
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

            this.tileIconImage.EnableInClassList("worktree-node__tile-icon--dimmed", newTileState == WorktreeTileState.Locked);
            this.lockBadgeImage.style.display = newTileState == WorktreeTileState.Locked ? DisplayStyle.Flex : DisplayStyle.None;

            // Rays are only drawn when unlocked/on-stage; force a repaint so a state flip is reflected immediately.
            this.tileElement.MarkDirtyRepaint();
        }

        private void ApplyCentralIcon(bool isTrunkNodeIcon, string leadStatusText)
        {
            this.isSpinnerActive = false;
            this.isGatingActive = false;
            this.gatingPipElement.style.display = DisplayStyle.None;

            if (isTrunkNodeIcon)
            {
                this.tileIconImage.image = WorktreeIcons.Resolve(WorktreeIcons.Branch);
                return;
            }

            string normalizedStatus = string.IsNullOrEmpty(leadStatusText) ? string.Empty : leadStatusText.ToLowerInvariant();
            switch (normalizedStatus)
            {
                case "building":
                    this.isSpinnerActive = true;
                    this.tileIconImage.image = WorktreeIcons.SpinnerFrame(0.0);
                    break;
                case "gating":
                    this.isSpinnerActive = true;
                    this.isGatingActive = true;
                    this.gatingPipElement.style.display = DisplayStyle.Flex;
                    this.tileIconImage.image = WorktreeIcons.SpinnerFrame(0.0);
                    break;
                case "ready":
                case "done":
                    this.tileIconImage.image = WorktreeIcons.Resolve(WorktreeIcons.Ready);
                    break;
                case "failed":
                    this.tileIconImage.image = WorktreeIcons.Resolve(WorktreeIcons.Failed);
                    break;
                default:
                    this.tileIconImage.image = WorktreeIcons.Resolve(WorktreeIcons.Unclaimed);
                    break;
            }
        }

        private void RefreshCaptionText()
        {
            this.captionLabel.text = this.isSelected ? this.fullBranchName : this.shortBranchName;
            this.captionLabel.EnableInClassList("worktree-node__caption--selected", this.isSelected);
        }

        private void OnGenerateTileVisualContent(MeshGenerationContext context)
        {
            if (this.TileState == WorktreeTileState.Locked)
            {
                return;
            }

            Rect contentRect = context.visualElement.contentRect;
            if (contentRect.width <= 0f || contentRect.height <= 0f)
            {
                return;
            }

            Vector2 centerPoint = new Vector2(contentRect.width * 0.5f, contentRect.height * 0.5f);
            float rayLength = Mathf.Min(contentRect.width, contentRect.height) * RayLengthFraction;

            Painter2D painter = context.painter2D;
            painter.strokeColor = new Color(WorktreePalette.Cream.r, WorktreePalette.Cream.g, WorktreePalette.Cream.b, RayAlpha);
            painter.lineWidth = RayLineWidth;

            for (int rayIndex = 0; rayIndex < RayCount; rayIndex++)
            {
                float rayAngleRadians = rayIndex * (Mathf.PI * 2f / RayCount);
                Vector2 rayEndPoint = centerPoint + new Vector2(Mathf.Cos(rayAngleRadians), Mathf.Sin(rayAngleRadians)) * rayLength;

                painter.BeginPath();
                painter.MoveTo(centerPoint);
                painter.LineTo(rayEndPoint);
                painter.Stroke();
            }
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
                populateEvent.menu.AppendAction("Reveal", trunkRevealAction =>
                    this.ActionRequested?.Invoke(this.NodeId, WorktreeNodeAction.Reveal));
                return;
            }

            string stageMenuLabel = this.isCurrentlyOnStage ? "Return to trunk" : "Put on stage";
            WorktreeNodeAction stageMenuAction = this.isCurrentlyOnStage ? WorktreeNodeAction.ReturnStage : WorktreeNodeAction.PutOnStage;

            populateEvent.menu.AppendAction(stageMenuLabel, stageAction =>
                this.ActionRequested?.Invoke(this.NodeId, stageMenuAction));
            populateEvent.menu.AppendAction("Merge", mergeAction =>
                this.ActionRequested?.Invoke(this.NodeId, WorktreeNodeAction.Merge));
            populateEvent.menu.AppendAction("Remove", removeAction =>
                this.ActionRequested?.Invoke(this.NodeId, WorktreeNodeAction.Remove));
            populateEvent.menu.AppendAction("Reveal", revealAction =>
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
