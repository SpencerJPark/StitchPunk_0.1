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
        private readonly Label emptyHintLabel;

        public event Action<TrackBinding, VisualElement> RemapRequested;

        public RetargetTrackTableElement()
        {
            name = "retarget-track-table";
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            headerTitleLabel = new Label("Tracks (0)");
            headerTitleLabel.AddToClassList("toolkit-pane-title");
            header.Add(headerTitleLabel);
            Add(header);

            trackListView = new ListView();
            trackListView.name = "retarget-track-list";
            trackListView.style.flexGrow = 1f;
            trackListView.fixedItemHeight = 22f;
            trackListView.selectionType = SelectionType.None;
            trackListView.makeItem = MakeTrackRow;
            trackListView.bindItem = BindTrackRow;
            trackListView.itemsSource = bindings;
            Add(trackListView);

            emptyHintLabel = new Label("Pick a clip and a rig to see where its tracks land.");
            emptyHintLabel.AddToClassList("clip-editor__hint");
            Add(emptyHintLabel);

            RefreshEmptyState();
        }

        public void SetBindings(IReadOnlyList<TrackBinding> bindings)
        {
            this.bindings.Clear();
            if (bindings != null)
            {
                this.bindings.AddRange(bindings);
            }

            headerTitleLabel.text = "Tracks (" + this.bindings.Count.ToString() + ")";
            trackListView.Rebuild();
            RefreshEmptyState();
        }

        private void RefreshEmptyState()
        {
            bool hasBindings = bindings.Count > 0;
            trackListView.style.display = hasBindings ? DisplayStyle.Flex : DisplayStyle.None;
            emptyHintLabel.style.display = hasBindings ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private VisualElement MakeTrackRow()
        {
            VisualElement row = new VisualElement();
            row.name = "retarget-track-row";
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 4f;

            Label glyphLabel = new Label();
            glyphLabel.name = "retarget-track-glyph";
            glyphLabel.style.width = 16f;
            row.Add(glyphLabel);

            Label nameLabel = new Label();
            nameLabel.name = "retarget-track-name";
            nameLabel.style.width = 140f;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(nameLabel);

            Label detailLabel = new Label();
            detailLabel.name = "retarget-track-detail";
            detailLabel.style.flexGrow = 1f;
            row.Add(detailLabel);

            Button remapButton = new Button();
            remapButton.name = "retarget-remap-button";
            remapButton.text = "remap ▾";
            remapButton.tooltip = "Point this track at one of the rig's tags";
            remapButton.clicked += () =>
            {
                int boundIndex = (int)remapButton.userData;
                if (boundIndex >= 0 && boundIndex < bindings.Count)
                {
                    RemapRequested?.Invoke(bindings[boundIndex], remapButton);
                }
            };
            row.Add(remapButton);

            return row;
        }

        private void BindTrackRow(VisualElement element, int index)
        {
            TrackBinding binding = bindings[index];
            Label glyphLabel = element.Q<Label>("retarget-track-glyph");
            Label nameLabel = element.Q<Label>("retarget-track-name");
            Label detailLabel = element.Q<Label>("retarget-track-detail");
            Button remapButton = element.Q<Button>("retarget-remap-button");

            switch (binding.state)
            {
                case TrackBindingState.Bound:
                    glyphLabel.text = "✓";
                    glyphLabel.style.color = ToolkitPalette.Clean;
                    glyphLabel.tooltip = "Bound";
                    break;
                case TrackBindingState.Skipped:
                    glyphLabel.text = "●";
                    glyphLabel.style.color = ToolkitPalette.Warning;
                    glyphLabel.tooltip = "Skipped: this rig has no part for this track";
                    break;
                default:
                    glyphLabel.text = "✗";
                    glyphLabel.style.color = ToolkitPalette.Error;
                    glyphLabel.tooltip = "Dangling: the tag is not in the registry";
                    break;
            }

            nameLabel.text = binding.trackName;

            if (binding.state == TrackBindingState.Bound)
            {
                detailLabel.text = "→ " + binding.targetDisplayName;
                detailLabel.style.color = StyleKeyword.Null;
            }
            else
            {
                detailLabel.text = "(" + binding.reason + ")";
                detailLabel.style.color = binding.state == TrackBindingState.Skipped
                    ? ToolkitPalette.Warning
                    : ToolkitPalette.Error;
            }

            bool showRemapButton = binding.state != TrackBindingState.Bound && binding.CanRemapTag;
            remapButton.style.display = showRemapButton ? DisplayStyle.Flex : DisplayStyle.None;
            remapButton.userData = index;
        }
    }
}
