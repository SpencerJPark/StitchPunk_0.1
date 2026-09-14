// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One row of the timeline: the keys of a single track, drawn and grabbed through <see cref="TimelineGeometry"/>.</summary>
    public sealed class TrackLaneElement : VisualElement
    {
        private static readonly Color LaneBackground = new Color(0.18f, 0.18f, 0.19f);
        private static readonly Color LaneAlternate = new Color(0.21f, 0.21f, 0.22f);
        private static readonly Color LaneChannel = new Color(0.155f, 0.155f, 0.165f);
        private static readonly Color LaneChannelAlternate = new Color(0.175f, 0.175f, 0.185f);
        private static readonly Color KeyFill = new Color(0.78f, 0.78f, 0.80f);
        private static readonly Color KeySelectedFill = new Color(0.30f, 0.62f, 0.95f);
        private static readonly Color KeyOutline = new Color(0.08f, 0.08f, 0.09f);

        private const float EventKeyHitRadius = EventLaneStyle.PinHitHalfWidth;

        private const long FlashDurationMilliseconds = 120;
        private const float FlashOutlineWidth = 3f;

        // How much closer one hit-tested key must be than another before OnPointerDown treats them
        // as genuinely different rather than a tie — two keys at the same time give bit-for-bit equal distances.
        private const float PointerTieEpsilonPixels = 0.01f;

        private readonly List<float> keyTimes = new List<float>();

        /// <summary>
        /// Window length per key as a fraction of the clip, parallel to <see cref="keyTimes"/>;
        /// empty on non-event lanes and 0 for a pulse-only marker.
        /// </summary>
        private readonly List<float> keyWindows = new List<float>();

        /// <summary>
        /// Scratch buffer for the keys tied for nearest-to-the-pointer on the current press.
        /// Reused across presses rather than allocated per click: a click is exactly the kind of
        /// frequent, latency-sensitive event a per-call <c>List</c> allocation should stay out of.
        /// </summary>
        private readonly List<int> pointerTiedKeyIndices = new List<int>();

        // Lane-local key index to the EditorApplication.timeSinceStartup its flash ends at.
        private readonly Dictionary<int, double> flashExpiryByKeyIndex = new Dictionary<int, double>();

        public TimelineTrackKind trackKind;
        public int trackIndex;
        public bool isAlternateRow;

        // Whether this row is one channel of an expanded track rather than the track itself. Drawn
        // smaller and dimmer, since a channel row shows the same keys as its track, not a separate set.
        public bool isChannelRow;

        // Set by the window per lane, from the event key, so one name is one colour in every timeline.
        public Color eventColor = ToolkitPalette.EventColors[0];

        /// <summary>The times this lane currently draws, for box selection to test against.</summary>
        public IReadOnlyList<float> KeyTimes
        {
            get { return keyTimes; }
        }

        /// <summary>Selection lives on the window; the lane only asks whether an address is in it.</summary>
        public Func<KeyAddress, bool> isKeySelected;

        /// <summary>Raised when the pointer grabs a key. The window owns what happens next.</summary>
        public event Action<KeyAddress, PointerDownEvent> keyPointerDown;

        /// <summary>Raised when the pointer presses empty lane space, with the normalized time.</summary>
        public event Action<TimelineTrackKind, int, float, PointerDownEvent> lanePointerDown;

        /// <summary>
        /// Row height comes from ClipEditorWindow.uss, which pairs it with the track header's
        /// height. Setting it inline here would beat that rule and let the two columns drift.
        /// </summary>
        public const string UssClassName = "clip-editor__lane";

        // Raised on a right-click over an event pin; the lane only says which marker is under the
        // pointer, the pane fills the menu.
        public event Action<KeyAddress, DropdownMenu> eventKeyContextMenu;

        public TrackLaneElement()
        {
            AddToClassList(UssClassName);
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.AddManipulator(new ContextualMenuManipulator(OnContextualMenuPopulate));
        }

        private void OnContextualMenuPopulate(ContextualMenuPopulateEvent menuEvent)
        {
            if (trackKind != TimelineTrackKind.Event || eventKeyContextMenu == null)
            {
                return;
            }

            TimelineGeometry geometry = Geometry;
            float localX = menuEvent.localMousePosition.x;
            int nearestIndex = -1;
            float nearestDistance = float.MaxValue;
            for (int keyIndex = 0; keyIndex < keyTimes.Count; keyIndex++)
            {
                if (!geometry.HitsKey(localX, keyTimes[keyIndex], EventKeyHitRadius))
                {
                    continue;
                }
                float distance = Mathf.Abs(localX - geometry.TimeToX(keyTimes[keyIndex]));
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = keyIndex;
                }
            }

            if (nearestIndex >= 0)
            {
                eventKeyContextMenu(new KeyAddress(trackKind, trackIndex, nearestIndex), menuEvent.menu);
            }
        }

        /// <summary>Replaces the times this lane shows and repaints.</summary>
        public void SetKeyTimes(IReadOnlyList<float> times)
        {
            keyTimes.Clear();
            for (int keyIndex = 0; keyIndex < times.Count; keyIndex++)
            {
                keyTimes.Add(times[keyIndex]);
            }
            keyWindows.Clear();

            // Indices no longer point at the same keys once the list is rebuilt.
            flashExpiryByKeyIndex.Clear();
            MarkDirtyRepaint();
        }

        public void FlashPin(int keyIndex)
        {
            if (keyIndex < 0 || keyIndex >= keyTimes.Count)
            {
                return;
            }

            flashExpiryByKeyIndex[keyIndex] = EditorApplication.timeSinceStartup + FlashDurationMilliseconds / 1000.0;
            MarkDirtyRepaint();
            schedule.Execute(MarkDirtyRepaint).StartingIn(FlashDurationMilliseconds);
        }

        // Supplies the window length behind each event key, as a fraction of the clip. Call after
        // SetKeyTimes, which clears these.
        public void SetKeyWindows(IReadOnlyList<float> windows)
        {
            keyWindows.Clear();
            for (int keyIndex = 0; keyIndex < windows.Count; keyIndex++)
            {
                keyWindows.Add(windows[keyIndex]);
            }
            MarkDirtyRepaint();
        }


        /// <summary>
        /// The timeline view, pushed in by the window. Never derived here: a lane that computed its
        /// own zoom would drift from the ruler's, which is the bug TimelineGeometry exists to stop.
        /// </summary>
        public float viewZoom = 1f;
        public float viewPan;


        /// <summary>The timeline width the window wants used, in pixels. Zero means "measure yourself".</summary>
        // Pushed in rather than measured locally: per-element contentRect widths disagreed until layout
        // settled, and any disagreement is multiplied by zoom into a visible gap at high zoom.
        public float viewLaneWidth;

        /// <summary>The width to build geometry from: the pushed one, or our own before layout.</summary>
        private float ResolvedWidth
        {
            get { return viewLaneWidth > 1f ? viewLaneWidth : contentRect.width; }
        }

        private TimelineGeometry Geometry
        {
            get { return TimelineGeometry.Create(ResolvedWidth, viewZoom, viewPan); }
        }

        private void OnPointerDown(PointerDownEvent pointerEvent)
        {
            TimelineGeometry geometry = Geometry;
            float localX = pointerEvent.localPosition.x;

            // A whole lane is one track kind, so the grab box is chosen once per press rather than
            // per key — an event lane's pentagon needs a different box than every other lane's
            // diamond, but never a mix of both within the same lane.
            float hitRadius = trackKind == TimelineTrackKind.Event
                ? EventKeyHitRadius
                : TimelineGeometry.KeyHitRadius;

            // Nearest-first so overlapping keys resolve to the one actually under the cursor rather
            // than to whichever happens to be earliest in the list.
            int nearestIndex = -1;
            float nearestDistance = float.MaxValue;
            for (int keyIndex = 0; keyIndex < keyTimes.Count; keyIndex++)
            {
                if (!geometry.HitsKey(localX, keyTimes[keyIndex], hitRadius))
                {
                    continue;
                }
                float distance = Mathf.Abs(localX - geometry.TimeToX(keyTimes[keyIndex]));
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = keyIndex;
                }
            }

            if (nearestIndex >= 0)
            {
                // A second pass, not folded into the one above: the first pass has to finish before
                // "nearest" is known, and only once it is known can every key that ties with it be
                // collected. Almost always this collects exactly nearestIndex on its own — several
                // markers at one normalized time is the one case where it collects more than that,
                // because they share a TimeToX result and therefore a distance-to-pointer.
                pointerTiedKeyIndices.Clear();
                for (int keyIndex = 0; keyIndex < keyTimes.Count; keyIndex++)
                {
                    if (!geometry.HitsKey(localX, keyTimes[keyIndex], hitRadius))
                    {
                        continue;
                    }
                    float distance = Mathf.Abs(localX - geometry.TimeToX(keyTimes[keyIndex]));
                    if (distance <= nearestDistance + PointerTieEpsilonPixels)
                    {
                        pointerTiedKeyIndices.Add(keyIndex);
                    }
                }

                int chosenIndex = pointerTiedKeyIndices.Count <= 1
                    ? nearestIndex
                    : ResolveTiedClick(
                        pointerTiedKeyIndices,
                        candidateIndex => isKeySelected != null
                            && isKeySelected(new KeyAddress(trackKind, trackIndex, candidateIndex)));

                if (keyPointerDown != null)
                {
                    keyPointerDown(new KeyAddress(trackKind, trackIndex, chosenIndex), pointerEvent);
                }
                pointerEvent.StopPropagation();
                return;
            }

            if (lanePointerDown != null)
            {
                lanePointerDown(trackKind, trackIndex, geometry.XToTime(localX), pointerEvent);
            }
        }

        // Chooses which member of a group tied for nearest-to-the-pointer a click should select: the
        // first click lands on the first member, and a repeated click cycles forward through the
        // rest. Pure — no VisualElement, no painter — so the cycling policy can be unit tested.
        /// <returns>The index in <paramref name="tiedIndices"/> the click should select.</returns>
        public static int ResolveTiedClick(IReadOnlyList<int> tiedIndices, Func<int, bool> isSelected)
        {
            if (tiedIndices.Count == 0)
            {
                return -1;
            }
            if (tiedIndices.Count == 1)
            {
                return tiedIndices[0];
            }

            for (int position = 0; position < tiedIndices.Count; position++)
            {
                if (isSelected(tiedIndices[position]))
                {
                    int nextPosition = (position + 1) % tiedIndices.Count;
                    return tiedIndices[nextPosition];
                }
            }
            return tiedIndices[0];
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Painter2D painter = context.painter2D;

            painter.fillColor = isChannelRow
                ? (isAlternateRow ? LaneChannelAlternate : LaneChannel)
                : (isAlternateRow ? LaneAlternate : LaneBackground);
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, 0f));
            painter.LineTo(new Vector2(rect.width, 0f));
            painter.LineTo(new Vector2(rect.width, rect.height));
            painter.LineTo(new Vector2(0f, rect.height));
            painter.ClosePath();
            painter.Fill();

            TimelineGeometry geometry = Geometry;

            // After the lane fill and before the keys: the shading is a backdrop, and a key drawn
            // under it would look disabled rather than out of range.
            TimelineRangeShading.Paint(painter, geometry, rect);

            float centreY = rect.height * 0.5f;

            // Bars first, so every key sits on top of its own window rather than being half-hidden
            // by the translucent bar of the marker before it. Left at lane centre even when its
            // marker is about to draw offset by a stack: the bar reports a duration in clip time,
            // which a stacking position invented for on-screen legibility has no bearing on, and
            // co-located windows already overlapped before this phase touched marker drawing.
            DrawEventWindows(painter, geometry, rect, centreY);

            for (int keyIndex = 0; keyIndex < keyTimes.Count; keyIndex++)
            {
                bool selected = isKeySelected != null
                    && isKeySelected(new KeyAddress(trackKind, trackIndex, keyIndex));

                Color fill = trackKind == TimelineTrackKind.Event ? eventColor : KeyFill;
                // An event key's fill is the event's identity, so selection shows as a stroke rather than swapping the fill.
                bool isEventSelectedKey = selected && trackKind == TimelineTrackKind.Event;
                painter.fillColor = selected && !isEventSelectedKey ? KeySelectedFill : fill;
                painter.strokeColor = isEventSelectedKey ? ToolkitPalette.Selected : KeyOutline;
                painter.lineWidth = isEventSelectedKey ? 2f : 1f;

                float x = geometry.TimeToX(keyTimes[keyIndex]);

                if (trackKind == TimelineTrackKind.Event)
                {
                    // The flash beats the selection outline for its 120 ms; otherwise scrubbing the
                    // event you just selected shows no crossing at all.
                    bool isFlashingKey = flashExpiryByKeyIndex.TryGetValue(keyIndex, out double flashExpiry)
                        && flashExpiry > EditorApplication.timeSinceStartup;
                    if (isFlashingKey)
                    {
                        EventLaneStyle.DrawPin(painter, x, centreY, eventColor, eventColor, FlashOutlineWidth);
                    }
                    else
                    {
                        EventLaneStyle.DrawPin(painter, x, centreY, eventColor, isEventSelectedKey);
                    }
                    continue;
                }

                // A diamond rather than a square: it reads as a keyframe at a glance and its widest
                // point is exactly on the key's time, so the shape itself communicates the value.
                float radius = isChannelRow
                    ? TimelineGeometry.KeyDrawRadius * 0.65f
                    : TimelineGeometry.KeyDrawRadius;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, centreY - radius));
                painter.LineTo(new Vector2(x + radius, centreY));
                painter.LineTo(new Vector2(x, centreY + radius));
                painter.LineTo(new Vector2(x - radius, centreY));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
            }
        }

        // Draws the translucent bar spanning each event marker's window. A window past the clip end
        // is clipped at the lane's right edge rather than wrapped to the left, which would read as a
        // second, invented window.
        private void DrawEventWindows(
            Painter2D painter,
            TimelineGeometry geometry,
            Rect rect,
            float centreY)
        {
            if (trackKind != TimelineTrackKind.Event || keyWindows.Count == 0)
            {
                return;
            }

            float barHalfHeight = Mathf.Min(rect.height * 0.5f - 1f, TimelineGeometry.KeyDrawRadius);
            if (barHalfHeight <= 0f)
            {
                return;
            }

            int barCount = Mathf.Min(keyWindows.Count, keyTimes.Count);
            for (int keyIndex = 0; keyIndex < barCount; keyIndex++)
            {
                float windowLength = keyWindows[keyIndex];
                if (windowLength <= 0f)
                {
                    continue;
                }

                float startX = geometry.TimeToX(keyTimes[keyIndex]);
                float endX = Mathf.Min(
                    geometry.TimeToX(keyTimes[keyIndex] + windowLength), rect.width);
                EventLaneStyle.DrawWindow(painter, startX, endX, centreY, barHalfHeight, eventColor);
            }
        }
    }
}
