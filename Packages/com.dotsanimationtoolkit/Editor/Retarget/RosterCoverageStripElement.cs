// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Shows one chip per rig in the roster with a bound/total count and a five-block coverage
    /// bar; clicking a chip requests that rig become the shared selection.
    /// </summary>
    public sealed class RosterCoverageStripElement : VisualElement
    {
        private const int TotalBlockCount = 5;

        private readonly Label headingLabel;

        public event Action<RigAsset> RigChipClicked;

        public RosterCoverageStripElement()
        {
            name = "retarget-roster-strip";
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexWrap = Wrap.Wrap;
            style.paddingTop = 4f;
            style.paddingBottom = 4f;
            style.paddingLeft = 4f;
            style.paddingRight = 4f;

            headingLabel = new Label("Roster:") { name = "retarget-roster-heading" };
            headingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
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
                hintLabel.AddToClassList("clip-editor__hint");
                Add(hintLabel);
                return;
            }

            foreach (RosterCoverageEntry entry in entries)
            {
                Add(BuildChip(entry, selectedRig));
            }
        }

        public static int FilledBlockCount(int boundCount, int totalCount)
        {
            if (totalCount <= 0)
            {
                return 0;
            }

            int roundedBlockCount = Mathf.RoundToInt(TotalBlockCount * (float)boundCount / totalCount);

            if (boundCount > 0 && roundedBlockCount < 1)
            {
                roundedBlockCount = 1;
            }

            if (boundCount < totalCount && roundedBlockCount > TotalBlockCount - 1)
            {
                roundedBlockCount = TotalBlockCount - 1;
            }

            if (roundedBlockCount > TotalBlockCount)
            {
                roundedBlockCount = TotalBlockCount;
            }

            return roundedBlockCount;
        }

        private VisualElement BuildChip(RosterCoverageEntry entry, RigAsset selectedRig)
        {
            RosterCoverageEntry capturedEntry = entry;

            VisualElement chip = new VisualElement { name = "retarget-roster-chip" };
            chip.style.flexDirection = FlexDirection.Row;
            chip.style.alignItems = Align.Center;
            chip.style.marginRight = 10f;
            chip.style.paddingTop = 2f;
            chip.style.paddingBottom = 2f;
            chip.style.paddingLeft = 6f;
            chip.style.paddingRight = 6f;
            chip.style.borderTopLeftRadius = 3f;
            chip.style.borderTopRightRadius = 3f;
            chip.style.borderBottomLeftRadius = 3f;
            chip.style.borderBottomRightRadius = 3f;
            chip.style.borderTopWidth = 1f;
            chip.style.borderBottomWidth = 1f;
            chip.style.borderLeftWidth = 1f;
            chip.style.borderRightWidth = 1f;

            Color borderColor = entry.rig == selectedRig ? ToolkitPalette.Selected : ToolkitPalette.BoxBorder;
            chip.style.borderTopColor = borderColor;
            chip.style.borderBottomColor = borderColor;
            chip.style.borderLeftColor = borderColor;
            chip.style.borderRightColor = borderColor;

            string rigName = entry.rig != null ? entry.rig.name : "(missing rig)";

            Label nameLabel = new Label(rigName) { name = "retarget-roster-chip-name" };
            nameLabel.style.marginRight = 4f;
            chip.Add(nameLabel);

            Label countLabel = new Label(entry.boundCount + "/" + entry.totalCount)
            {
                name = "retarget-roster-chip-count"
            };
            countLabel.style.marginRight = 4f;
            if (entry.boundCount == 0 && entry.totalCount > 0)
            {
                countLabel.style.color = ToolkitPalette.Error;
            }
            chip.Add(countLabel);

            int filledBlockCount = FilledBlockCount(entry.boundCount, entry.totalCount);
            Color filledBlockColor = entry.boundCount == entry.totalCount ? ToolkitPalette.Clean : ToolkitPalette.Warning;

            for (int blockIndex = 0; blockIndex < TotalBlockCount; blockIndex++)
            {
                VisualElement block = new VisualElement { name = "retarget-roster-chip-block" };
                block.style.width = 6f;
                block.style.height = 10f;
                block.style.marginLeft = 1f;
                block.style.backgroundColor = blockIndex < filledBlockCount ? filledBlockColor : ToolkitPalette.BoxBorder;
                chip.Add(block);
            }

            chip.tooltip = "Show this clip on " + rigName;
            chip.RegisterCallback<ClickEvent>(clickEvent => RigChipClicked?.Invoke(capturedEntry.rig));

            return chip;
        }
    }
}
