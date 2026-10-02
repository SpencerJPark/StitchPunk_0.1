// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Stands in for a list while it is empty: the list's own tone with a title, why, and the action that fills it, instead of Unity's "List is empty". Place it next to the list it replaces.</summary>
    public sealed class ToolkitEmptyListSurface : VisualElement
    {
        private readonly VisualElement listBody;

        public ToolkitEmptyListSurface(string elementName, VisualElement listBody)
        {
            name = elementName;
            this.listBody = listBody;
            style.flexGrow = 1f;
            style.display = DisplayStyle.None;
            AddToClassList("toolkit-list-surface");
        }

        // Returns the action button so a host can anchor a menu to it; null when there is no action.
        public Button Show(string title, string why, string actionText, Action onAction)
        {
            Clear();
            VisualElement emptyState = ToolkitChrome.MakeEmptyState(name + "-state", title, why, actionText, onAction);
            Add(emptyState);
            listBody.style.display = DisplayStyle.None;
            style.display = DisplayStyle.Flex;
            return actionText == null ? null : emptyState.Q<Button>();
        }

        public void Hide()
        {
            Clear();
            style.display = DisplayStyle.None;
            listBody.style.display = DisplayStyle.Flex;
        }
    }
}
