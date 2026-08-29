// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The searchable rig-part picker a timeline row's part half opens (amendment A56 D3): pick
    /// which part of the open rig wears the row's tag. Selection only, like every picker here —
    /// the parts listed are exactly the rig's targets, and the pick moves a tag, never types one.
    /// </summary>
    public sealed class RigTargetPicker : PickerOverlay
    {
        private const float PanelWidth = 260f;
        private const float CardWidth = 240f;
        private const float RowsMaxHeight = 240f;

        private readonly RigAsset rig;
        private readonly TargetTagRegistry tagRegistry;
        private readonly uint movingTagId;
        private readonly Action<uint> onPick;
        private readonly TextField filterField;
        private readonly VisualElement rowsContainer;

        private RigTargetPicker(
            RigAsset rig, TargetTagRegistry tagRegistry, uint movingTagId, Action<uint> onPick)
            : base(PanelWidth, CardWidth)
        {
            this.rig = rig;
            this.tagRegistry = tagRegistry;
            this.movingTagId = movingTagId;
            this.onPick = onPick;

            filterField = new TextField();
            filterField.style.marginLeft = 4f;
            filterField.style.marginRight = 4f;
            filterField.style.marginTop = 2f;
            filterField.style.marginBottom = 2f;
            filterField.RegisterValueChangedCallback(changeEvent => RefreshRows());
            listPanel.Add(filterField);

            ScrollView rowsScrollView = new ScrollView(ScrollViewMode.Vertical);
            rowsScrollView.style.maxHeight = RowsMaxHeight;
            listPanel.Add(rowsScrollView);

            rowsContainer = new VisualElement();
            rowsScrollView.Add(rowsContainer);

            RefreshRows();
        }

        /// <summary>Opens the picker over <paramref name="host"/>, hung under <paramref name="anchor"/>.</summary>
        /// <param name="movingTagId">The tag the pick will move; its current wearer lists as already bound.</param>
        /// <param name="onPick">Invoked with the chosen part's stable id after the picker closes.</param>
        public static RigTargetPicker Open(
            VisualElement host, VisualElement anchor, RigAsset rig,
            TargetTagRegistry tagRegistry, uint movingTagId, Action<uint> onPick)
        {
            if (host == null || rig == null)
            {
                return null;
            }
            RigTargetPicker picker = new RigTargetPicker(rig, tagRegistry, movingTagId, onPick);
            picker.FinalizeOpen(host, anchor);
            return picker;
        }

        private void RefreshRows()
        {
            rowsContainer.Clear();
            if (rig == null || rig.targets == null)
            {
                return;
            }

            string filterText = filterField.value ?? string.Empty;
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null || target.stableId == 0u)
                {
                    continue;
                }
                string partName = string.IsNullOrEmpty(target.displayName)
                    ? "(unnamed part)"
                    : target.displayName;
                if (!VocabularyPicker.MatchesFilter(partName, filterText))
                {
                    continue;
                }

                bool alreadyWearsIt = target.tagId == movingTagId && movingTagId != 0u;
                uint pickedTargetId = target.stableId;
                rowsContainer.Add(BuildRow(
                    partName,
                    DescribeConsequence(target),
                    !alreadyWearsIt,
                    "Already wears this row's tag — the keys land here now.",
                    () => onPick?.Invoke(pickedTargetId)));
            }
        }

        /// <summary>
        /// The hover card's body: what the part wears today, and what picking it displaces — a
        /// tag move is a rig-wide edit, so the consequence is stated before the click, not after.
        /// </summary>
        private string DescribeConsequence(RigTargetDefinition target)
        {
            if (target.tagId == 0u)
            {
                return "Untagged. Picking it moves this row's tag onto it; every clip set using "
                    + "this rig follows.";
            }
            string wornTagName = tagRegistry != null ? tagRegistry.FindName(target.tagId) : null;
            string wornTagText = wornTagName
                ?? "(unresolved 0x" + target.tagId.ToString("X8") + ")";
            return "Wears tag '" + wornTagText + "'. Picking it replaces that tag with this row's "
                + "— rows keyed against '" + wornTagText + "' will show (no tagged part) until it "
                + "is placed again.";
        }
    }
}
