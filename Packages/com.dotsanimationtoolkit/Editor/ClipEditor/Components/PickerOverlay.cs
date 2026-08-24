// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The overlay chrome shared by every searchable picker in the Clip Editor (Phase E target-tags
    /// spec §4.2.1): a full-window scrim that dismisses on an outside press or Escape, a list panel
    /// hung under an anchor and pulled back inside the host when it would overhang, and a hover card
    /// beside the list that explains whatever row the pointer is over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Extracted from <see cref="ClipComponentPicker"/> rather than left duplicated.</strong>
    /// <see cref="ClipComponentPicker"/> was the only picker in the package before Phase E target
    /// tags needed a second one — <see cref="TargetTagPicker"/> — for selecting a tag by name, and a
    /// filterable search field on top (Phase E target-tags spec §4.2.1). The two pickers pick
    /// different kinds of thing (a <c>ClipComponentKind</c> versus a tag id) and the Add Component
    /// picker has no search field, so what is shared is exactly the chrome around the choice — not
    /// the choice itself. This class owns that chrome; each subclass owns its own row content, its
    /// own entry type, and (for <see cref="TargetTagPicker"/>) its own filter field above the list.
    /// </para>
    /// <para>
    /// <strong>An overlay inside the window rather than a dropdown window of its own</strong>, for the
    /// reason <see cref="ClipComponentPicker"/> always documented: a separate <c>EditorWindow</c>
    /// would have to convert the anchor's panel-space rect into screen coordinates, which drifts the
    /// moment the host window is docked somewhere new. Living in the same panel means placement uses
    /// <see cref="VisualElement.WorldToLocal"/> against layout that has already been resolved.
    /// </para>
    /// </remarks>
    public abstract class PickerOverlay : VisualElement
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

        private const float EdgeMargin = 4f;

        /// <summary>How far the card sits from the panel it explains.</summary>
        private const float CardGap = 6f;

        private readonly float panelWidth;
        private readonly float cardWidth;

        /// <summary>
        /// The panel a subclass adds its rows (and, for <see cref="TargetTagPicker"/>, its filter
        /// field) to.
        /// </summary>
        protected readonly VisualElement listPanel;

        private readonly VisualElement card;
        private readonly Label cardTitle;
        private readonly Label cardBody;
        private readonly Label cardReason;

        // Kept rather than read back out of the style, which hands back a StyleLength that has to be
        // unwrapped twice and means nothing until layout has run. These are the numbers that were
        // decided; the style is where they were sent.
        private float panelLeft;
        private float panelTop;

        protected PickerOverlay(float panelWidth, float cardWidth)
        {
            this.panelWidth = panelWidth;
            this.cardWidth = cardWidth;

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
            listPanel.style.width = panelWidth;
            Add(listPanel);

            card = new VisualElement();
            card.AddToClassList(CardUssClassName);
            card.style.position = Position.Absolute;
            card.style.width = cardWidth;
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
        }

        public void Close()
        {
            RemoveFromHierarchy();
        }

        /// <summary>
        /// Adds this picker to <paramref name="host"/>, hangs its panel under <paramref name="anchor"/>,
        /// and gives it keyboard focus so Escape reaches it.
        /// </summary>
        /// <param name="host">
        /// The element the overlay covers, and the space the panel and card are placed in. The
        /// window root, so a card beside a narrow inspector still has somewhere to go.
        /// </param>
        /// <param name="anchor">The control that opened it; the panel hangs from its lower-left.</param>
        protected void FinalizeOpen(VisualElement host, VisualElement anchor)
        {
            host.Add(this);
            PlacePanel(host, anchor);

            focusable = true;
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            schedule.Execute(Focus);
        }

        /// <summary>Builds one selectable row, wired for hover-card display and pick/dismiss.</summary>
        /// <param name="displayName">The row's label.</param>
        /// <param name="description">Shown in the card body on hover.</param>
        /// <param name="isAvailable">Whether the row can be picked; unavailable rows are dimmed.</param>
        /// <param name="unavailableReason">Why not, appended to the card when unavailable.</param>
        /// <param name="onPicked">
        /// Invoked when an available row is pressed, after the picker has already closed — the same
        /// order <see cref="ClipComponentPicker"/> always used, so a callback that opens another
        /// picker or dialog is not fighting this one for the panel it is still attached to.
        /// </param>
        protected VisualElement BuildRow(
            string displayName,
            string description,
            bool isAvailable,
            string unavailableReason,
            System.Action onPicked)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(RowUssClassName);
            row.EnableInClassList(RowUnavailableUssClassName, !isAvailable);

            Label label = new Label(displayName);
            row.Add(label);

            row.RegisterCallback<PointerEnterEvent>(pointerEvent =>
            {
                row.AddToClassList(RowHoveredUssClassName);
                ShowCard(row, displayName, description, isAvailable ? string.Empty : unavailableReason);
            });
            row.RegisterCallback<PointerLeaveEvent>(pointerEvent =>
            {
                row.RemoveFromClassList(RowHoveredUssClassName);
                HideCard();
            });

            if (isAvailable)
            {
                row.RegisterCallback<PointerDownEvent>(pointerEvent =>
                {
                    pointerEvent.StopPropagation();
                    Close();
                    onPicked?.Invoke();
                });
            }
            else
            {
                // Swallowed rather than left to fall through to the overlay: a press on a row you are
                // being told about should not close the thing telling you.
                row.RegisterCallback<PointerDownEvent>(
                    pointerEvent => pointerEvent.StopPropagation());
            }
            return row;
        }

        private void ShowCard(VisualElement row, string title, string body, string reason)
        {
            cardTitle.text = title;
            cardBody.text = body;

            bool hasReason = !string.IsNullOrEmpty(reason);
            cardReason.text = hasReason ? reason : string.Empty;
            cardReason.style.display = hasReason ? DisplayStyle.Flex : DisplayStyle.None;

            card.style.display = DisplayStyle.Flex;

            // Placed against resolved layout: the row is on screen already, so its world rect is the
            // real one rather than a prediction of where it will end up.
            Rect rowBounds = this.WorldToLocal(row.worldBound);

            float rightOfPanel = panelLeft + panelWidth + CardGap;
            float left = rightOfPanel + cardWidth + EdgeMargin <= layout.width
                ? rightOfPanel
                : panelLeft - CardGap - cardWidth;
            card.style.left = Mathf.Max(EdgeMargin, left);
            card.style.top = rowBounds.yMin;
        }

        protected void HideCard()
        {
            card.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Hangs the panel off the anchor, pulled back inside the host when it would overhang.
        /// </summary>
        /// <remarks>
        /// The height is not known until the panel has been laid out, so the vertical clamp waits for
        /// the first geometry pass. Guessing it from the row count would be a second layout engine,
        /// agreeing with the real one until a style changed.
        /// </remarks>
        private void PlacePanel(VisualElement host, VisualElement anchor)
        {
            Rect anchorBounds = anchor != null
                ? host.WorldToLocal(anchor.worldBound)
                : new Rect(EdgeMargin, EdgeMargin, panelWidth, 0f);

            float left = Mathf.Max(EdgeMargin, anchorBounds.xMin);
            if (left + panelWidth + EdgeMargin > host.layout.width)
            {
                left = Mathf.Max(EdgeMargin, host.layout.width - panelWidth - EdgeMargin);
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
