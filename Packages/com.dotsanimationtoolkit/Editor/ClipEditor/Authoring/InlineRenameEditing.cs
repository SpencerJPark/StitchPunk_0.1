using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// Shared in-place rename editor for a list row's title label -- swaps it for a focused
    /// TextField, commits on Enter or focus loss, and cancels cleanly on Escape.
    public static class InlineRenameEditing
    {
        private const string InlineRenameFieldName = "inline-rename-field";

        public static void Begin(Label titleLabel, string currentName, Action<string> onCommit)
        {
            if (titleLabel == null || titleLabel.parent == null)
            {
                return;
            }

            VisualElement rowParent = titleLabel.parent;
            TextField existingRenameField = rowParent.Q<TextField>(InlineRenameFieldName);
            if (existingRenameField != null)
            {
                existingRenameField.Focus();
                return;
            }

            int titleLabelIndex = rowParent.IndexOf(titleLabel);
            titleLabel.style.display = DisplayStyle.None;

            TextField renameField = new TextField();
            renameField.name = InlineRenameFieldName;
            renameField.value = currentName;
            renameField.style.flexGrow = titleLabel.resolvedStyle.flexGrow;
            renameField.style.flexShrink = titleLabel.resolvedStyle.flexShrink;
            renameField.style.minWidth = 0f;
            renameField.selectAllOnFocus = true;
            rowParent.Insert(titleLabelIndex, renameField);

            // Escape cancels and blurs the field in the same call, so the FocusOutEvent that
            // follows would otherwise re-commit the very edit Escape just threw away -- one flag
            // shared by every handler makes whichever fires first the only one that runs.
            bool isFinished = false;

            void FinishEditing()
            {
                if (isFinished)
                {
                    return;
                }

                isFinished = true;
                renameField.RemoveFromHierarchy();
                titleLabel.style.display = DisplayStyle.Flex;
            }

            void CommitEditing()
            {
                if (isFinished)
                {
                    return;
                }

                string trimmedValue = renameField.value != null ? renameField.value.Trim() : string.Empty;
                FinishEditing();
                if (trimmedValue.Length > 0 && trimmedValue != currentName)
                {
                    onCommit(trimmedValue);
                }
            }

            renameField.RegisterCallback<KeyDownEvent>(keyDownEvent =>
            {
                if (keyDownEvent.keyCode == KeyCode.Return || keyDownEvent.keyCode == KeyCode.KeypadEnter)
                {
                    keyDownEvent.StopPropagation();
                    CommitEditing();
                }
                else if (keyDownEvent.keyCode == KeyCode.Escape)
                {
                    keyDownEvent.StopPropagation();
                    FinishEditing();
                }
            });

            renameField.RegisterCallback<FocusOutEvent>(focusOutEvent => CommitEditing());

            renameField.schedule.Execute(() => renameField.Focus());
        }
    }
}
