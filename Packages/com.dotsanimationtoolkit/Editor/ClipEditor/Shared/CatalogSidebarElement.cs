// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Sidebar host that switches between named catalog columns, sharing one header and actions slot.</summary>
    public class CatalogSidebarElement : VisualElement
    {
        private sealed class ModeEntry
        {
            public string Label;
            public VisualElement Column;
            public VisualElement HeaderActions;
        }

        private readonly VisualElement modesHost;
        private readonly VisualElement actionsSlot;
        private readonly Dictionary<string, ModeEntry> modeEntriesByName = new Dictionary<string, ModeEntry>();
        private readonly List<string> modeNamesInOrder = new List<string>();

        private VisualElement segmentedControl;

        public string Mode { get; private set; }

        public event Action<string> ModeChanged;

        public CatalogSidebarElement()
        {
            AddToClassList("toolkit-column");
            AddToClassList("toolkit-column--host");
            style.flexGrow = 1f;
            style.minWidth = 200f;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");

            // One group, not two loose children: the header spreads its children with
            // space-between, which would push the toggles to opposite edges.
            modesHost = new VisualElement { name = "sidebar-mode-toggles-host" };
            header.Add(modesHost);

            actionsSlot = new VisualElement { name = "sidebar-actions" };
            actionsSlot.AddToClassList("toolkit-pane-actions");
            // Stays on the right edge even when the header wraps it onto a second row.
            actionsSlot.style.marginLeft = StyleKeyword.Auto;
            header.Add(actionsSlot);

            Add(header);
        }

        public void AddMode(string modeName, string toggleText, VisualElement column, VisualElement headerActions)
        {
            ModeEntry modeEntry = new ModeEntry { Label = toggleText, Column = column, HeaderActions = headerActions };
            modeEntriesByName[modeName] = modeEntry;
            modeNamesInOrder.Add(modeName);
            Add(column);

            modesHost.Clear();
            List<string> modeLabels = new List<string>();
            foreach (string existingModeName in modeNamesInOrder)
            {
                modeLabels.Add(modeEntriesByName[existingModeName].Label);
            }

            int selectedIndex = Mode != null ? modeNamesInOrder.IndexOf(Mode) : 0;
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            segmentedControl = ToolkitChrome.MakeSegmentedControl(
                "sidebar-mode-toggles",
                modeLabels,
                selectedIndex,
                index => SetMode(modeNamesInOrder[index]));
            modesHost.Add(segmentedControl);

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

            if (segmentedControl != null)
            {
                ToolkitChrome.SetSegmentedSelection(segmentedControl, modeNamesInOrder.IndexOf(modeName));
            }

            foreach (KeyValuePair<string, ModeEntry> modeEntryPair in modeEntriesByName)
            {
                bool modeEntryIsActive = modeEntryPair.Key == modeName;
                modeEntryPair.Value.Column.style.display = modeEntryIsActive ? DisplayStyle.Flex : DisplayStyle.None;
            }

            Mode = modeName;

            actionsSlot.Clear();
            if (activeModeEntry.HeaderActions != null)
            {
                actionsSlot.Add(activeModeEntry.HeaderActions);
            }

            // No re-entry guard needed: SetSegmentedSelection only moves a class and raises nothing.
            ModeChanged?.Invoke(modeName);
        }
    }
}
