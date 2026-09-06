// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One offerable component in the Add Component picker.</summary>
    public struct ClipComponentPickerEntry
    {
        public ClipComponentKind kind;
        public string displayName;

        /// <summary>What the component does — the card shown while the row is hovered.</summary>
        public string description;

        public bool isAvailable;

        /// <summary>Why it cannot be added, appended to the card. Empty when it can.</summary>
        public string unavailableReason;
    }

    /// <summary>The Add Component picker: a list of what an object could carry, each one's description shown on hover.</summary>
    public sealed class ClipComponentPicker : PickerOverlay
    {
        private const float PanelWidth = 190f;
        private const float CardWidth = 270f;

        private readonly Action<ClipComponentKind> onPick;

        private ClipComponentPicker(
            IReadOnlyList<ClipComponentPickerEntry> entries, Action<ClipComponentKind> onPick)
            : base(PanelWidth, CardWidth)
        {
            this.onPick = onPick;

            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                listPanel.Add(BuildRow(entries[entryIndex]));
            }
        }

        public static ClipComponentPicker Open(
            VisualElement host, VisualElement anchor,
            IReadOnlyList<ClipComponentPickerEntry> entries, Action<ClipComponentKind> onPick)
        {
            if (host == null || entries == null || entries.Count == 0)
            {
                return null;
            }

            ClipComponentPicker picker = new ClipComponentPicker(entries, onPick);
            picker.FinalizeOpen(host, anchor);
            return picker;
        }

        private VisualElement BuildRow(ClipComponentPickerEntry entry)
        {
            ClipComponentKind picked = entry.kind;
            return BuildRow(
                entry.displayName,
                entry.description,
                entry.isAvailable,
                entry.unavailableReason,
                () => onPick?.Invoke(picked));
        }
    }
}
