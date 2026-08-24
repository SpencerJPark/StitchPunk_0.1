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

    /// <summary>
    /// The Add Component picker: a list of what an object could carry, with each one's description
    /// shown on hover rather than printed beside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The descriptions moved off the rows because a menu is a list of choices, not a
    /// document.</strong> Five rows each carrying two sentences is a wall to read every time you
    /// want the one you already know the name of, and it pushed the row you were aiming for further
    /// down the further you read. Hovering asks for the explanation; the list stays a list.
    /// </para>
    /// <para>
    /// <strong>Unavailable kinds are listed, dimmed, with the reason on the card.</strong> A menu
    /// that silently omits what you came looking for reads as a bug, and the reason is usually
    /// actionable.
    /// </para>
    /// <para>
    /// <strong>No search field, unlike <see cref="TargetTagPicker"/>.</strong> The kinds this picker
    /// offers top out at a handful (architecture section 7.1); a filter earns its keep once a list
    /// can run into the dozens, which is <see cref="TargetTagPicker"/>'s situation once a project has
    /// accumulated a real tag vocabulary (Phase E target-tags spec §4.2.1), not this one's. The
    /// overlay chrome — dismiss-on-outside-press, Escape, hover card, panel placement — lives in the
    /// shared <see cref="PickerOverlay"/> base so the two pickers cannot drift apart on that behaviour
    /// even though only one of them filters.
    /// </para>
    /// </remarks>
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

        /// <summary>
        /// Opens the picker over <paramref name="host"/>, hung under <paramref name="anchor"/>.
        /// </summary>
        /// <param name="host">
        /// The element the overlay covers, and the space the panel and card are placed in. The
        /// window root, so that a card beside a narrow inspector still has somewhere to go.
        /// </param>
        /// <param name="anchor">The button that opened it; the panel hangs from its lower-left.</param>
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
