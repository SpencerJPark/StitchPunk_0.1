using UnityEngine;
using UnityEngine.UIElements;

namespace WorktreeToolkit.Editor
{
    /// <summary>Plain-language footer: gesture/action word pairs separated by dot separators.</summary>
    public sealed class WorktreeFooterBar : VisualElement
    {
        public WorktreeFooterBar()
        {
            AddToClassList("worktree-footer");
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexShrink = 0f;

            Add(CreateHint("Click a tile:", "details", "Click a worktree tile to see its details."));
            AddSeparator();
            Add(CreateHint("Double-click:", "open in Editor", "Double-click a worktree tile to open that worktree in the Editor."));
            AddSeparator();
            Add(CreateHint("Right-click:", "more actions", "Right-click a worktree tile to open its actions menu (Put on stage, Merge, Remove, Reveal)."));
            AddSeparator();
            Add(CreateHint("F5:", "refresh", "Press F5 while this window has focus to refresh the worktree list."));
        }

        private void AddSeparator()
        {
            Label separatorLabel = new Label("·");
            separatorLabel.AddToClassList("worktree-footer__separator");
            Add(separatorLabel);
        }

        private static VisualElement CreateHint(string gestureText, string wordText, string tooltipText)
        {
            VisualElement hintContainer = new VisualElement();
            hintContainer.AddToClassList("worktree-footer__hint");
            hintContainer.style.flexDirection = FlexDirection.Row;
            hintContainer.style.alignItems = Align.Center;
            hintContainer.tooltip = tooltipText;

            Label gestureLabel = new Label(gestureText);
            gestureLabel.AddToClassList("worktree-footer__gesture");
            gestureLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            gestureLabel.tooltip = tooltipText;

            Label wordLabel = new Label(wordText);
            wordLabel.AddToClassList("worktree-footer__word");
            wordLabel.tooltip = tooltipText;

            hintContainer.Add(gestureLabel);
            hintContainer.Add(wordLabel);
            return hintContainer;
        }
    }
}
