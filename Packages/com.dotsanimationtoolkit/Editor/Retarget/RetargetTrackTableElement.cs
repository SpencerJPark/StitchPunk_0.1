// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Lists a clip's tracks against the selected rig as rows of glyph, name and where
    /// the track lands, with a per-row remap button for tracks that are not bound.</summary>
    public sealed class RetargetTrackTableElement : VisualElement
    {
        private readonly List<TrackBinding> bindings = new List<TrackBinding>();
        private readonly ListView trackListView;
        private readonly Label headerTitleLabel;
        private readonly Label rigPartCaptionLabel;
        private readonly Label statusCaptionLabel;
        private readonly Label headerTrackCountBadge;
        private readonly VisualElement emptyHintLabel;

        public event Action<TrackBinding, VisualElement> RemapRequested;

        public RetargetTrackTableElement()
        {
            name = "retarget-track-table";
            AddToClassList("toolkit-column");
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            VisualElement header = ToolkitChrome.MakePaneHeader("Tracks", out headerTitleLabel, out VisualElement headerActions);

            headerTrackCountBadge = ToolkitChrome.MakeBadge(FormatTrackCount(bindings.Count), ToolkitStatusTone.Neutral);
            headerActions.Add(headerTrackCountBadge);

            Add(header);

            // Built like a track row, not inside the pane header: the header's padding and actions
            // gave the percentage columns a different width, so captions drifted off their cells.
            VisualElement captionRow = new VisualElement { name = "retarget-track-caption-row" };
            captionRow.AddToClassList("toolkit-list-row");
            captionRow.style.flexDirection = FlexDirection.Row;
            captionRow.style.alignItems = Align.Center;
            captionRow.style.flexShrink = 0f;

            Label trackCaptionLabel = new Label("Clip track");
            trackCaptionLabel.AddToClassList("toolkit-list-row__meta");
            trackCaptionLabel.AddToClassList("retarget-col-track");
            captionRow.Add(trackCaptionLabel);

            rigPartCaptionLabel = new Label("Rig part");
            rigPartCaptionLabel.AddToClassList("toolkit-list-row__meta");
            rigPartCaptionLabel.AddToClassList("retarget-col-part");
            captionRow.Add(rigPartCaptionLabel);

            statusCaptionLabel = new Label("Status");
            statusCaptionLabel.AddToClassList("toolkit-list-row__meta");
            statusCaptionLabel.AddToClassList("retarget-col-status");
            captionRow.Add(statusCaptionLabel);

            Add(captionRow);

            VisualElement listBody = new VisualElement { name = "retarget-track-body" };
            listBody.AddToClassList("toolkit-list-surface");
            listBody.style.flexGrow = 1f;
            Add(listBody);

            trackListView = new ListView();
            trackListView.name = "retarget-track-list";
            trackListView.style.flexGrow = 1f;
            trackListView.fixedItemHeight = 22f;
            trackListView.selectionType = SelectionType.None;
            trackListView.makeItem = MakeTrackRow;
            trackListView.bindItem = BindTrackRow;
            trackListView.itemsSource = bindings;
            listBody.Add(trackListView);

            emptyHintLabel = ToolkitChrome.MakeEmptyState(
                "retarget-track-empty",
                "No tracks to show",
                "Pick a clip and a rig in the bar above to see where each of its tracks lands on that rig.",
                null,
                null);
            listBody.Add(emptyHintLabel);

            RefreshEmptyState();
        }

        public void SetBindings(IReadOnlyList<TrackBinding> bindings)
        {
            this.bindings.Clear();
            if (bindings != null)
            {
                this.bindings.AddRange(bindings);
            }

            headerTrackCountBadge.text = FormatTrackCount(this.bindings.Count);
            trackListView.Rebuild();
            RefreshEmptyState();
        }

        private void RefreshEmptyState()
        {
            bool hasBindings = bindings.Count > 0;
            trackListView.style.display = hasBindings ? DisplayStyle.Flex : DisplayStyle.None;
            emptyHintLabel.style.display = hasBindings ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static string FormatTrackCount(int trackCount)
        {
            return trackCount == 1 ? "1 track" : trackCount + " tracks";
        }

        private VisualElement MakeTrackRow()
        {
            VisualElement row = new VisualElement();
            row.name = "retarget-track-row";
            row.AddToClassList("toolkit-list-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            Label nameLabel = new Label();
            nameLabel.name = "retarget-track-name";
            nameLabel.AddToClassList("toolkit-list-row__title");
            nameLabel.AddToClassList("retarget-col-track");
            row.Add(nameLabel);

            Label detailLabel = new Label();
            detailLabel.name = "retarget-track-detail";
            detailLabel.AddToClassList("toolkit-list-row__meta");
            detailLabel.AddToClassList("retarget-col-part");
            row.Add(detailLabel);

            VisualElement statusBadgeContainer = ToolkitChrome.MakeBadgeRow("retarget-track-status-container");
            statusBadgeContainer.AddToClassList("retarget-col-status");
            row.Add(statusBadgeContainer);

            Button remapButton = null;
            remapButton = ToolkitIcons.MakeIconTextButton(() =>
            {
                int boundIndex = (int)remapButton.userData;
                if (boundIndex >= 0 && boundIndex < bindings.Count)
                {
                    RemapRequested?.Invoke(bindings[boundIndex], remapButton);
                }
            }, "d_Linked", "Point this track at one of the rig's tags", "Remap");
            remapButton.name = "retarget-remap-button";
            ToolkitChrome.StyleButton(remapButton, ToolkitButtonVariant.Ghost);
            row.Add(remapButton);

            return row;
        }

        private void BindTrackRow(VisualElement element, int index)
        {
            TrackBinding binding = bindings[index];
            VisualElement statusBadgeContainer = element.Q<VisualElement>("retarget-track-status-container");
            Label nameLabel = element.Q<Label>("retarget-track-name");
            Label detailLabel = element.Q<Label>("retarget-track-detail");
            Button remapButton = element.Q<Button>("retarget-remap-button");

            statusBadgeContainer.Clear();
            Label statusBadgeLabel;
            switch (binding.state)
            {
                case TrackBindingState.Bound:
                    statusBadgeLabel = ToolkitChrome.MakeBadge("Bound", ToolkitStatusTone.Ok);
                    break;
                case TrackBindingState.Skipped:
                    statusBadgeLabel = ToolkitChrome.MakeBadge("Skipped", ToolkitStatusTone.Warning);
                    statusBadgeLabel.tooltip = "Skipped: this rig has no part for this track";
                    break;
                default:
                    statusBadgeLabel = ToolkitChrome.MakeBadge("Dangling", ToolkitStatusTone.Error);
                    statusBadgeLabel.tooltip = "Dangling: the tag is not in the registry";
                    break;
            }
            statusBadgeContainer.Add(statusBadgeLabel);

            nameLabel.text = binding.trackName;

            if (binding.state == TrackBindingState.Bound)
            {
                detailLabel.text = "→ " + binding.targetDisplayName;
                detailLabel.tooltip = binding.targetDisplayName;
                detailLabel.EnableInClassList("toolkit-text--warning", false);
                detailLabel.EnableInClassList("toolkit-text--error", false);
            }
            else
            {
                detailLabel.text = "(" + binding.reason + ")";
                detailLabel.tooltip = binding.reason;
                detailLabel.EnableInClassList("toolkit-text--warning", binding.state == TrackBindingState.Skipped);
                detailLabel.EnableInClassList("toolkit-text--error", binding.state != TrackBindingState.Skipped);
            }

            bool showRemapButton = binding.state != TrackBindingState.Bound && binding.CanRemapTag;
            remapButton.style.display = showRemapButton ? DisplayStyle.Flex : DisplayStyle.None;
            remapButton.userData = index;
        }
    }
}
