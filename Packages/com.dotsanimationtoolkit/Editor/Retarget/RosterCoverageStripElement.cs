// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Shows one chip per rig in the roster with a bound/total count badge; clicking a chip
    /// requests that rig become the shared selection.
    /// </summary>
    public sealed class RosterCoverageStripElement : VisualElement
    {
        private readonly Label headingLabel;

        public event Action<RigAsset> RigChipClicked;

        public RosterCoverageStripElement()
        {
            name = "retarget-roster-strip";
            AddToClassList("toolkit-status-row");
            AddToClassList("toolkit-status-row--footer");
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexWrap = Wrap.Wrap;

            headingLabel = new Label("Roster:") { name = "retarget-roster-heading" };
            headingLabel.AddToClassList("toolkit-status");
            headingLabel.style.marginRight = 6f;
            Add(headingLabel);
        }

        public void SetRoster(IReadOnlyList<RosterCoverageEntry> entries, RigAsset selectedRig)
        {
            for (int childIndex = childCount - 1; childIndex > 0; childIndex--)
            {
                RemoveAt(childIndex);
            }

            if (entries == null || entries.Count == 0)
            {
                Label hintLabel = new Label("No rigs found in the project.") { name = "retarget-roster-hint" };
                hintLabel.AddToClassList("toolkit-hint");
                Add(hintLabel);
                return;
            }

            foreach (RosterCoverageEntry entry in entries)
            {
                Add(BuildChip(entry, selectedRig));
            }
        }

        private VisualElement BuildChip(RosterCoverageEntry entry, RigAsset selectedRig)
        {
            RosterCoverageEntry capturedEntry = entry;
            bool isSelected = entry.rig == selectedRig;

            VisualElement chip = new VisualElement { name = "retarget-roster-chip" };
            chip.AddToClassList("toolkit-chip");
            chip.EnableInClassList("toolkit-chip--selected", isSelected);
            chip.style.flexDirection = FlexDirection.Row;
            chip.style.alignItems = Align.Center;
            chip.style.marginRight = 10f;

            string rigName = entry.rig != null ? entry.rig.name : "(missing rig)";

            Label nameLabel = new Label(rigName) { name = "retarget-roster-chip-name" };
            nameLabel.style.marginRight = 4f;
            chip.Add(nameLabel);

            ToolkitStatusTone countTone;
            if (entry.totalCount == 0)
            {
                countTone = ToolkitStatusTone.Neutral;
            }
            else if (entry.boundCount == 0)
            {
                countTone = ToolkitStatusTone.Error;
            }
            else if (entry.boundCount == entry.totalCount)
            {
                countTone = ToolkitStatusTone.Ok;
            }
            else
            {
                countTone = ToolkitStatusTone.Warning;
            }

            Label countLabel = ToolkitChrome.MakeBadge(entry.boundCount + "/" + entry.totalCount, countTone);
            countLabel.name = "retarget-roster-chip-count";
            countLabel.style.marginRight = 4f;
            chip.Add(countLabel);

            chip.tooltip = "Show this clip on " + rigName;
            chip.RegisterCallback<ClickEvent>(clickEvent => RigChipClicked?.Invoke(capturedEntry.rig));

            return chip;
        }
    }
}
