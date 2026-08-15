// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StitchPunk.AnimationToolkit.Editor
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

        private readonly List<float> keyTimes = new List<float>();

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
            MarkDirtyRepaint();
        }

        private TimelineGeometry Geometry
        {
            get { return TimelineGeometry.Create(contentRect.width); }
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
            float centreY = rect.height * 0.5f;

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
    }
}
