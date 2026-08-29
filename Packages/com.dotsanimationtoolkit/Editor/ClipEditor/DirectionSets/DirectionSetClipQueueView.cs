// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The clip queue: one row per east-side slot, showing which clip serves it and which facings
    /// that slot covers once its free mirror is counted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>UI over the five slots, not a list of its own.</strong> A row <em>is</em> a slot of
    /// the open <see cref="DirectionSetAsset"/>; there is no queue data structure behind it and no
    /// (clip, facing-flags) pairing to keep in step. That is what makes the coverage readout and the
    /// bake's warning incapable of disagreeing — both read
    /// <see cref="DirectionSetAsset.TryGetEffectiveDirections"/>, and this view never derives
    /// coverage itself.
    /// </para>
    /// <para>
    /// Which rows exist is a display question: the required slots for the current target coverage,
    /// plus every slot that already holds a clip, plus any the author asked for with Add Clip.
    /// Unfilled required slots show as empty rows on purpose — "what do I still have to draw" is the
    /// question this panel exists to answer at a glance.
    /// </para>
    /// </remarks>
    public sealed class DirectionSetClipQueueView : VisualElement
    {
        /// <summary>Promotion order — which slot a set fills next as its coverage climbs.</summary>
        public static readonly Direction[] SlotOrder = new[]
        {
            Direction.SouthEast, Direction.NorthEast, Direction.South, Direction.North, Direction.East
        };

        private static readonly string[] SlotChoiceLabels = new[]
        {
            "SouthEast", "NorthEast", "South", "North", "East"
        };

        private readonly ScrollView rowScroll;

        /// <summary>Raised when a row writes a clip into a slot. The panel owns the undo record.</summary>
        public event Action<Direction, ClipAsset> SlotAssigned;

        /// <summary>Raised when a row is re-slotted: the clip moves, and the old slot is cleared.</summary>
        public event Action<Direction, Direction> SlotMoved;

        /// <summary>Raised by a row's Open in Clip Editor button.</summary>
        public event Action<ClipAsset> OpenClipRequested;

        /// <summary>Raised when a row is removed, which clears its slot rather than hiding a row.</summary>
        public event Action<Direction> SlotCleared;

        public DirectionSetClipQueueView()
        {
            style.flexGrow = 1f;

            rowScroll = new ScrollView();
            rowScroll.style.flexGrow = 1f;
            Add(rowScroll);
        }

        /// <summary>
        /// Rebuilds every row from the set as it currently stands.
        /// </summary>
        /// <param name="directionSet">The open set, or null for an empty queue.</param>
        /// <param name="visibleSlots">Which slots get a row, in promotion order.</param>
        /// <param name="clipWarnings">
        /// Per-clip warnings to show inline — a clip that failed validation against the preview rig,
        /// keyed by the clip itself so only the offending row is marked and the rest keep previewing.
        /// </param>
        public void Rebuild(
            DirectionSetAsset directionSet,
            IReadOnlyList<Direction> visibleSlots,
            IReadOnlyDictionary<ClipAsset, string> clipWarnings)
        {
            rowScroll.Clear();

            if (directionSet == null)
            {
                Label emptyLabel = new Label("Assign a Direction Set above, or press New Set.");
                emptyLabel.style.whiteSpace = WhiteSpace.Normal;
                emptyLabel.style.marginTop = 4f;
                rowScroll.Add(emptyLabel);
                return;
            }

            for (int slotIndex = 0; slotIndex < visibleSlots.Count; slotIndex++)
            {
                rowScroll.Add(BuildRow(directionSet, visibleSlots[slotIndex], clipWarnings));
            }
        }

        private VisualElement BuildRow(
            DirectionSetAsset directionSet,
            Direction slot,
            IReadOnlyDictionary<ClipAsset, string> clipWarnings)
        {
            ClipAsset slotClip = directionSet.GetSlot(slot);

            VisualElement row = new VisualElement();
            row.style.marginBottom = 4f;
            row.style.paddingLeft = 4f;
            row.style.paddingRight = 4f;
            row.style.paddingTop = 3f;
            row.style.paddingBottom = 3f;
            row.style.backgroundColor = slotClip != null
                ? new Color(0.20f, 0.20f, 0.21f)
                : new Color(0.16f, 0.16f, 0.17f);

            VisualElement topLine = new VisualElement();
            topLine.style.flexDirection = FlexDirection.Row;
            topLine.style.alignItems = Align.Center;
            row.Add(topLine);

            ObjectField clipField = new ObjectField { objectType = typeof(ClipAsset), value = slotClip };
            clipField.style.flexGrow = 1f;
            clipField.RegisterValueChangedCallback(
                changeEvent => SlotAssigned?.Invoke(slot, changeEvent.newValue as ClipAsset));
            topLine.Add(clipField);

            DropdownField slotDropdown = new DropdownField(new List<string>(SlotChoiceLabels), IndexOfSlot(slot));
            slotDropdown.style.width = 96f;
            slotDropdown.tooltip =
                "Which east-side facing this clip serves. Its west-side twin comes free as a mirror.";
            slotDropdown.RegisterValueChangedCallback(changeEvent =>
            {
                Direction newSlot = SlotOrder[slotDropdown.index];
                if (newSlot != slot)
                {
                    SlotMoved?.Invoke(slot, newSlot);
                }
            });
            topLine.Add(slotDropdown);

            Button openButton = new Button(() => OpenClipRequested?.Invoke(directionSet.GetSlot(slot)))
            {
                text = "Open"
            };
            openButton.tooltip = "Edit this clip's content in the Clip Editor.";
            openButton.SetEnabled(slotClip != null);
            topLine.Add(openButton);

            Button clearButton = new Button(() => SlotCleared?.Invoke(slot)) { text = "×" };
            clearButton.tooltip = "Clear this slot.";
            clearButton.SetEnabled(slotClip != null);
            topLine.Add(clearButton);

            Label servesLabel = new Label(DescribeCoverageOfSlot(slot));
            servesLabel.style.marginLeft = 2f;
            servesLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            servesLabel.style.color = new Color(0.65f, 0.65f, 0.68f);
            row.Add(servesLabel);

            string clipWarning;
            if (slotClip != null && clipWarnings != null
                && clipWarnings.TryGetValue(slotClip, out clipWarning))
            {
                Label warningLabel = new Label(clipWarning);
                warningLabel.style.whiteSpace = WhiteSpace.Normal;
                warningLabel.style.color = new Color(1f, 0.55f, 0.2f);
                row.Add(warningLabel);
            }

            return row;
        }

        /// <summary>
        /// What one filled slot actually buys, mirror included — "SE + SW (mirror)" rather than "SE",
        /// because the free mirror is the whole reason a six-direction character costs four clips.
        /// </summary>
        public static string DescribeCoverageOfSlot(Direction slot)
        {
            switch (slot)
            {
                case Direction.SouthEast: return "serves: SE + SW (mirror)";
                case Direction.NorthEast: return "serves: NE + NW (mirror)";
                case Direction.East: return "serves: E + W (mirror)";
                case Direction.South: return "serves: S only (its own mirror)";
                case Direction.North: return "serves: N only (its own mirror)";
                default: return string.Empty;
            }
        }

        /// <summary>The two-letter compass form used in the coverage and gap readouts.</summary>
        public static string ShortName(Direction facing)
        {
            switch (facing)
            {
                case Direction.North: return "N";
                case Direction.NorthEast: return "NE";
                case Direction.East: return "E";
                case Direction.SouthEast: return "SE";
                case Direction.South: return "S";
                case Direction.SouthWest: return "SW";
                case Direction.West: return "W";
                default: return "NW";
            }
        }

        private static int IndexOfSlot(Direction slot)
        {
            for (int index = 0; index < SlotOrder.Length; index++)
            {
                if (SlotOrder[index] == slot)
                {
                    return index;
                }
            }
            return 0;
        }
    }
}
