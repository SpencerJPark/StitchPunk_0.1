// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;
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
    /// <strong>An overlay inside the window rather than a dropdown window of its own.</strong> A
    /// separate <c>EditorWindow</c> would have to convert the button's panel-space rect into screen
    /// coordinates, which is the kind of arithmetic that is right until someone docks the window
    /// somewhere new. Living in the same panel means the card is positioned by
    /// <see cref="VisualElement.WorldToLocal"/> against layout that has already been resolved, and
    /// the whole thing dies with the panel that owns it.
    /// </para>
    /// <para>
    /// <strong>Unavailable kinds are listed, dimmed, with the reason on the card.</strong> A menu
    /// that silently omits what you came looking for reads as a bug, and the reason is usually
    /// actionable.
    /// </para>
    /// </remarks>
    public sealed class ClipComponentPicker : VisualElement
    {
        public const string OverlayUssClassName = "clip-editor__picker-overlay";
        public const string PanelUssClassName = "clip-editor__picker";
        public const string RowUssClassName = "clip-editor__picker-row";
        public const string RowUnavailableUssClassName = "clip-editor__picker-row--unavailable";
        public const string RowHoveredUssClassName = "clip-editor__picker-row--hovered";
        public const string CardUssClassName = "clip-editor__picker-card";
        public const string CardTitleUssClassName = "clip-editor__picker-card-title";
        public const string CardBodyUssClassName = "clip-editor__picker-card-body";
        public const string CardReasonUssClassName = "clip-editor__picker-card-reason";

        private const float PanelWidth = 190f;
        private const float CardWidth = 270f;
        private const float EdgeMargin = 4f;

        /// <summary>How far the card sits from the panel it explains.</summary>
        private const float CardGap = 6f;

        private readonly VisualElement listPanel;
        private readonly VisualElement card;
        private readonly Label cardTitle;
        private readonly Label cardBody;
        private readonly Label cardReason;
        private readonly Action<ClipComponentKind> onPick;

        // Kept rather than read back out of the style, which hands back a StyleLength that has to
        // be unwrapped twice and means nothing until layout has run. These are the numbers that
        // were decided; the style is where they were sent.
        private float panelLeft;
        private float panelTop;

        private ClipComponentPicker(
            IReadOnlyList<ClipComponentPickerEntry> entries, Action<ClipComponentKind> onPick)
        {
            this.onPick = onPick;

            AddToClassList(OverlayUssClassName);
            style.position = Position.Absolute;
            style.left = 0f;
            style.top = 0f;
            style.right = 0f;
            style.bottom = 0f;

            // Closing on a press outside the list, in the trickle-down phase, so the click that
            // dismisses the picker does not also land on whatever was underneath it. Dismissing a
            // menu is the whole of that click's meaning.
            RegisterCallback<PointerDownEvent>(OnOverlayPointerDown, TrickleDown.TrickleDown);

            listPanel = new VisualElement();
            listPanel.AddToClassList(PanelUssClassName);
            listPanel.style.position = Position.Absolute;
            listPanel.style.width = PanelWidth;
            Add(listPanel);

            card = new VisualElement();
            card.AddToClassList(CardUssClassName);
            card.style.position = Position.Absolute;
            card.style.width = CardWidth;
            card.style.display = DisplayStyle.None;
            Add(card);

            cardTitle = new Label();
            cardTitle.AddToClassList(CardTitleUssClassName);
            card.Add(cardTitle);

            cardBody = new Label();
            cardBody.AddToClassList(CardBodyUssClassName);
            card.Add(cardBody);

            cardReason = new Label();
            cardReason.AddToClassList(CardReasonUssClassName);
            card.Add(cardReason);

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
            host.Add(picker);
            picker.PlacePanel(host, anchor);

            // Focusable so Escape reaches it. A menu that can only be dismissed with the mouse is
            // one the keyboard cannot get out of.
            picker.focusable = true;
            picker.RegisterCallback<KeyDownEvent>(picker.OnKeyDown);
            picker.schedule.Execute(() => picker.Focus());
            return picker;
        }

        public void Close()
        {
            RemoveFromHierarchy();
        }

        private VisualElement BuildRow(ClipComponentPickerEntry entry)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(RowUssClassName);
            row.EnableInClassList(RowUnavailableUssClassName, !entry.isAvailable);

            Label label = new Label(entry.displayName);
            row.Add(label);

            row.RegisterCallback<PointerEnterEvent>(pointerEvent =>
            {
                row.AddToClassList(RowHoveredUssClassName);
                ShowCard(entry, row);
            });
            row.RegisterCallback<PointerLeaveEvent>(pointerEvent =>
            {
                row.RemoveFromClassList(RowHoveredUssClassName);
                HideCard();
            });

            if (entry.isAvailable)
            {
                ClipComponentKind picked = entry.kind;
                row.RegisterCallback<PointerDownEvent>(pointerEvent =>
                {
                    pointerEvent.StopPropagation();
                    Close();
                    if (onPick != null)
                    {
                        onPick(picked);
                    }
                });
            }
            else
            {
                // Swallowed rather than left to fall through to the overlay: a press on a row you
                // are being told about should not close the thing telling you.
                row.RegisterCallback<PointerDownEvent>(
                    pointerEvent => pointerEvent.StopPropagation());
            }
            return row;
        }

        private void ShowCard(ClipComponentPickerEntry entry, VisualElement row)
        {
            cardTitle.text = entry.displayName;
            cardBody.text = entry.description;

            bool hasReason = !entry.isAvailable && !string.IsNullOrEmpty(entry.unavailableReason);
            cardReason.text = hasReason ? entry.unavailableReason : string.Empty;
            cardReason.style.display = hasReason ? DisplayStyle.Flex : DisplayStyle.None;

            card.style.display = DisplayStyle.Flex;

            // Placed against resolved layout: the row is on screen already, so its world rect is
            // the real one rather than a prediction of where it will end up.
            Rect rowBounds = this.WorldToLocal(row.worldBound);

            float rightOfPanel = panelLeft + PanelWidth + CardGap;
            float left = rightOfPanel + CardWidth + EdgeMargin <= layout.width
                ? rightOfPanel
                : panelLeft - CardGap - CardWidth;
            card.style.left = Mathf.Max(EdgeMargin, left);
            card.style.top = rowBounds.yMin;
        }

        private void HideCard()
        {
            card.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Hangs the panel off the button, pulled back inside the host when it would overhang.
        /// </summary>
        /// <remarks>
        /// The height is not known until the panel has been laid out, so the vertical clamp waits
        /// for the first geometry pass. Guessing it from the row count would be a second layout
        /// engine, agreeing with the real one until a style changed.
        /// </remarks>
        private void PlacePanel(VisualElement host, VisualElement anchor)
        {
            Rect anchorBounds = anchor != null
                ? host.WorldToLocal(anchor.worldBound)
                : new Rect(EdgeMargin, EdgeMargin, PanelWidth, 0f);

            float left = Mathf.Max(EdgeMargin, anchorBounds.xMin);
            if (left + PanelWidth + EdgeMargin > host.layout.width)
            {
                left = Mathf.Max(EdgeMargin, host.layout.width - PanelWidth - EdgeMargin);
            }
            panelLeft = left;
            panelTop = anchorBounds.yMax + 2f;
            listPanel.style.left = panelLeft;
            listPanel.style.top = panelTop;

            listPanel.RegisterCallback<GeometryChangedEvent>(geometryEvent =>
            {
                float overhang = panelTop + listPanel.layout.height + EdgeMargin - layout.height;
                if (overhang <= 0f)
                {
                    return;
                }
                panelTop = Mathf.Max(EdgeMargin, panelTop - overhang);
                listPanel.style.top = panelTop;
            });
        }

        private void OnOverlayPointerDown(PointerDownEvent pointerEvent)
        {
            VisualElement pressed = pointerEvent.target as VisualElement;
            if (pressed != null && (pressed == listPanel || listPanel.Contains(pressed)))
            {
                return;
            }
            pointerEvent.StopPropagation();
            Close();
        }

        private void OnKeyDown(KeyDownEvent keyEvent)
        {
            if (keyEvent.keyCode != KeyCode.Escape)
            {
                return;
            }
            keyEvent.StopPropagation();
            Close();
        }
    }
}
