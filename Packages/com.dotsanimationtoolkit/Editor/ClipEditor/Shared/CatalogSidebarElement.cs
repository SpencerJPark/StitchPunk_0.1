// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Sidebar host that switches between named catalog columns, sharing one header and actions slot.</summary>
    public class CatalogSidebarElement : VisualElement
    {
        private const string TabUssClassName = "clip-editor__tab";
        private const string TabActiveUssClassName = "clip-editor__tab--active";

        private sealed class ModeEntry
        {
            public ToolbarToggle Toggle;
            public VisualElement Column;
            public VisualElement HeaderActions;
        }

        private readonly VisualElement modeToggles;
        private readonly VisualElement actionsSlot;
        private readonly Dictionary<string, ModeEntry> modeEntriesByName = new Dictionary<string, ModeEntry>();

        private bool isApplyingMode;

        public string Mode { get; private set; }

        public event Action<string> ModeChanged;

        public CatalogSidebarElement()
        {
            AddToClassList("toolkit-column");
            style.flexGrow = 1f;
            style.minWidth = 200f;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");

            // One group, not two loose children: the header spreads its children with
            // space-between, which would push the toggles to opposite edges.
            modeToggles = new VisualElement { name = "sidebar-mode-toggles" };
            modeToggles.AddToClassList("toolkit-sidebar__modes");
            header.Add(modeToggles);

            actionsSlot = new VisualElement { name = "sidebar-actions" };
            actionsSlot.AddToClassList("toolkit-pane-actions");
            // Stays on the right edge even when the header wraps it onto a second row.
            actionsSlot.style.marginLeft = StyleKeyword.Auto;
            header.Add(actionsSlot);

            Add(header);
        }

        public void AddMode(string modeName, string toggleText, VisualElement column, VisualElement headerActions)
        {
            ToolbarToggle toggle = new ToolbarToggle { name = "sidebar-" + modeName + "-toggle", text = toggleText };
            toggle.AddToClassList(TabUssClassName);
            toggle.RegisterValueChangedCallback(changeEvent => OnToggleChanged(modeName, changeEvent));
            modeToggles.Add(toggle);

            ModeEntry modeEntry = new ModeEntry { Toggle = toggle, Column = column, HeaderActions = headerActions };
            modeEntriesByName[modeName] = modeEntry;
            Add(column);

            if (Mode == null)
            {
                SetMode(modeName);
            }
        }

        public void SetMode(string modeName)
        {
            if (!modeEntriesByName.TryGetValue(modeName, out ModeEntry activeModeEntry))
            {
                return;
            }

            // The assignments below raise change callbacks on the toggles; the guard stops SetMode
            // from re-entering itself, and also lets a click on the already-lit toggle snap back to true.
            isApplyingMode = true;
            foreach (KeyValuePair<string, ModeEntry> modeEntryPair in modeEntriesByName)
            {
                modeEntryPair.Value.Toggle.SetValueWithoutNotify(modeEntryPair.Key == modeName);
            }
            isApplyingMode = false;

            foreach (KeyValuePair<string, ModeEntry> modeEntryPair in modeEntriesByName)
            {
                bool modeEntryIsActive = modeEntryPair.Key == modeName;
                modeEntryPair.Value.Toggle.EnableInClassList(TabActiveUssClassName, modeEntryIsActive);
                modeEntryPair.Value.Column.style.display = modeEntryIsActive ? DisplayStyle.Flex : DisplayStyle.None;
            }

            Mode = modeName;

            actionsSlot.Clear();
            if (activeModeEntry.HeaderActions != null)
            {
                actionsSlot.Add(activeModeEntry.HeaderActions);
            }

            ModeChanged?.Invoke(modeName);
        }

        private void OnToggleChanged(string modeName, ChangeEvent<bool> changeEvent)
        {
            if (isApplyingMode)
            {
                return;
            }

            SetMode(modeName);
        }
    }
}
