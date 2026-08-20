// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// One row of the timeline: the keys of a single track, drawn and grabbed through
    /// <see cref="TimelineGeometry"/> (architecture section 7.2).
    /// </summary>
    /// <remarks>
    /// Drawn with <c>generateVisualContent</c> rather than IMGUI, per section 7 and enforced by the
    /// packaging conformance scan. The element owns no layout maths of its own — every position
    /// comes from the shared geometry, which is what stops the drawn diamond and its grab box
    /// drifting apart under zoom.
    /// </remarks>
    public sealed class TrackLaneElement : VisualElement
    {
        private static readonly Color LaneBackground = new Color(0.18f, 0.18f, 0.19f);
        private static readonly Color LaneAlternate = new Color(0.21f, 0.21f, 0.22f);
        private static readonly Color LaneChannel = new Color(0.155f, 0.155f, 0.165f);
        private static readonly Color LaneChannelAlternate = new Color(0.175f, 0.175f, 0.185f);
        private static readonly Color KeyFill = new Color(0.78f, 0.78f, 0.80f);
        private static readonly Color KeySelectedFill = new Color(0.30f, 0.62f, 0.95f);
        private static readonly Color KeyOutline = new Color(0.08f, 0.08f, 0.09f);
        private static readonly Color EventKeyFill = new Color(0.92f, 0.72f, 0.32f);
        private static readonly Color EventWindowFill = new Color(0.92f, 0.72f, 0.32f, 0.30f);

        /// <summary>
        /// Event keys draw larger than pose keys. They are sparser, they are the rows a person
        /// scans for when lining a hit up against a pose, and unlike a pose key their exact
        /// sub-frame position matters less than being able to find them at all.
        /// </summary>
        private const float EventKeyRadiusScale = 1.35f;

        private readonly List<float> keyTimes = new List<float>();

        /// <summary>
        /// Window length per key as a fraction of the clip, parallel to <see cref="keyTimes"/>;
        /// empty on non-event lanes and 0 for a pulse-only marker.
        /// </summary>
        private readonly List<float> keyWindows = new List<float>();

        public TimelineTrackKind trackKind;
        public int trackIndex;
        public bool isAlternateRow;

        /// <summary>
        /// Whether this row is one channel of an expanded track rather than the track itself.
        /// </summary>
        /// <remarks>
        /// Drawn smaller and dimmer, because a channel row shows the <em>same</em> keys as its
        /// track: one key carries position, rotation and scale together, so the channel rows are a
        /// reading of one set of keys, not several sets. Making them look identical to track rows
        /// would imply keys that can be moved independently, which they cannot.
        /// </remarks>
        public bool isChannelRow;

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

        public TrackLaneElement()
        {
            AddToClassList(UssClassName);
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
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
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Supplies the window length behind each event key, as a fraction of the clip.
        /// </summary>
        /// <remarks>
        /// Call after <see cref="SetKeyTimes"/>, which clears these — a lane that has been given new
        /// key times but no new windows would otherwise draw the old bars under the new keys. A
        /// shorter list than the key list simply leaves the remaining keys unbarred.
        /// </remarks>
        /// <param name="windows">Window length per key, parallel to the key times.</param>
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


        /// <summary>
        /// The timeline width the window wants used, in pixels. Zero means "measure yourself".
        /// </summary>
        /// <remarks>
        /// <strong>Pushed in for the same reason zoom and pan are.</strong> The ruler and playhead
        /// sit in the lane stack while the lanes sit in a column inside it, so each element
        /// measuring its own <c>contentRect</c> gave three widths that agreed only once layout had
        /// settled. Any difference between them is multiplied by the zoom, so a few pixels of
        /// disagreement at 1x became a visible gap between the cursor and the key at 20x. One width
        /// for the whole timeline makes that gap unrepresentable.
        /// </remarks>
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

            // Nearest-first so overlapping keys resolve to the one actually under the cursor rather
            // than to whichever happens to be earliest in the list.
            int nearestIndex = -1;
            float nearestDistance = float.MaxValue;
            for (int keyIndex = 0; keyIndex < keyTimes.Count; keyIndex++)
            {
                if (!geometry.HitsKey(localX, keyTimes[keyIndex]))
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
                if (keyPointerDown != null)
                {
                    keyPointerDown(new KeyAddress(trackKind, trackIndex, nearestIndex), pointerEvent);
                }
                pointerEvent.StopPropagation();
                return;
            }

            if (lanePointerDown != null)
            {
                lanePointerDown(trackKind, trackIndex, geometry.XToTime(localX), pointerEvent);
            }
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
            // by the translucent bar of the marker before it.
            DrawEventWindows(painter, geometry, rect, centreY);

            for (int keyIndex = 0; keyIndex < keyTimes.Count; keyIndex++)
            {
                bool selected = isKeySelected != null
                    && isKeySelected(new KeyAddress(trackKind, trackIndex, keyIndex));

                Color fill = trackKind == TimelineTrackKind.Event ? EventKeyFill : KeyFill;
                painter.fillColor = selected ? KeySelectedFill : fill;
                painter.strokeColor = KeyOutline;
                painter.lineWidth = 1f;

                float x = geometry.TimeToX(keyTimes[keyIndex]);
                float radius = isChannelRow
                    ? TimelineGeometry.KeyDrawRadius * 0.65f
                    : TimelineGeometry.KeyDrawRadius;
                if (trackKind == TimelineTrackKind.Event)
                {
                    radius *= EventKeyRadiusScale;
                }

                // A diamond rather than a square: it reads as a keyframe at a glance and its widest
                // point is exactly on the key's time, so the shape itself communicates the value.
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

        /// <summary>
        /// Draws the translucent bar spanning each event marker's window, so a hit frame's duration
        /// is visible against the poses it has to line up with.
        /// </summary>
        /// <remarks>
        /// A window that runs past the end of the clip is clipped at the lane's right edge rather
        /// than wrapped around to the left. On a looping clip the runtime does wrap it, so this
        /// under-draws — but a bar that reappeared at the start of the lane reads as a second,
        /// earlier window, and inventing an event the author never placed is the worse of the two
        /// errors.
        /// </remarks>
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

            painter.fillColor = EventWindowFill;

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
                if (endX <= startX)
                {
                    continue;
                }

                painter.BeginPath();
                painter.MoveTo(new Vector2(startX, centreY - barHalfHeight));
                painter.LineTo(new Vector2(endX, centreY - barHalfHeight));
                painter.LineTo(new Vector2(endX, centreY + barHalfHeight));
                painter.LineTo(new Vector2(startX, centreY + barHalfHeight));
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
