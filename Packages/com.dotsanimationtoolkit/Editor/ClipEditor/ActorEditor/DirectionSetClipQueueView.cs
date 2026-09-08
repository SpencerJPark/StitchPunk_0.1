// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The clip queue: one row per east-side slot a <see cref="DirectionSlots"/> fills.</summary>
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

        /// <summary>Raised when a row writes a clip into a slot. The host owns the undo record.</summary>
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

        /// <summary>Rebuilds every row from <paramref name="slots"/> as it currently stands.</summary>
        /// <param name="slots">The slots to queue, or null for an empty queue.</param>
        public void Rebuild(
            DirectionSlots slots,
            IReadOnlyList<Direction> visibleSlots,
            IReadOnlyDictionary<ClipAsset, string> clipWarnings)
        {
            rowScroll.Clear();

            if (slots == null)
            {
                Label emptyLabel = new Label("No direction slots assigned.");
                emptyLabel.style.whiteSpace = WhiteSpace.Normal;
                emptyLabel.style.marginTop = 4f;
                rowScroll.Add(emptyLabel);
                return;
            }

            for (int slotIndex = 0; slotIndex < visibleSlots.Count; slotIndex++)
            {
                rowScroll.Add(BuildRow(slots, visibleSlots[slotIndex], clipWarnings));
            }
        }

        private VisualElement BuildRow(
            DirectionSlots slots,
            Direction slot,
            IReadOnlyDictionary<ClipAsset, string> clipWarnings)
        {
            ClipAsset slotClip = slots.GetSlot(slot);

            VisualElement row = new VisualElement();
            row.AddToClassList("toolkit-box");
            row.EnableInClassList("toolkit-box--active", slotClip != null);

            VisualElement topLine = new VisualElement();
            topLine.AddToClassList("toolkit-box__header");
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

            Button openButton = new Button(() => OpenClipRequested?.Invoke(slots.GetSlot(slot)))
            {
                text = "Open"
            };
            openButton.tooltip = "Edit this clip's content in the Clip Editor.";
            openButton.SetEnabled(slotClip != null);
            topLine.Add(openButton);

            Button clearButton = ToolkitIcons.MakeIconButton(
                () => SlotCleared?.Invoke(slot), ToolkitIcons.Trash, "Clear this slot.", "×");
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
