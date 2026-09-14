using UnityEngine;
using UnityEngine.UIElements;

namespace WorktreeToolkit.Editor
{
    /// <summary>Controller-prompt style footer: round key badges paired with a short action word.</summary>
    public sealed class WorktreeFooterBar : VisualElement
    {
        public WorktreeFooterBar()
        {
            AddToClassList("worktree-footer");
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexShrink = 0f;

            Add(CreateHint("2×", "Put on stage", "Double-click a worktree tile to put that worktree on stage."));
            Add(CreateHint("RMB", "Actions", "Right-click a worktree tile to open its actions menu (Put on stage, Merge, Remove, Reveal)."));
            Add(CreateHint("F5", "Refresh", "Press F5 while this window has focus to refresh the worktree list."));
        }

        private static VisualElement CreateHint(string keyText, string wordText, string tooltipText)
        {
            VisualElement hintContainer = new VisualElement();
            hintContainer.AddToClassList("worktree-footer__hint");
            hintContainer.style.flexDirection = FlexDirection.Row;
            hintContainer.style.alignItems = Align.Center;
            hintContainer.tooltip = tooltipText;

            Label keyBadgeLabel = new Label(keyText);
            keyBadgeLabel.AddToClassList("worktree-footer__key");
            keyBadgeLabel.style.minWidth = 20f;
            keyBadgeLabel.style.height = 20f;
            keyBadgeLabel.style.paddingLeft = 5f;
            keyBadgeLabel.style.paddingRight = 5f;
            keyBadgeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            keyBadgeLabel.tooltip = tooltipText;

            Label wordLabel = new Label(wordText);
            wordLabel.AddToClassList("worktree-footer__word");
            wordLabel.tooltip = tooltipText;

            hintContainer.Add(keyBadgeLabel);
            hintContainer.Add(wordLabel);
            return hintContainer;
        }
    }
}
