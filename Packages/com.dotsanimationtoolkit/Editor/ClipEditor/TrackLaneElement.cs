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
        /// Half the event marker's drawn width, in pixels.
        /// </summary>
        /// <remarks>
        /// Narrower than <see cref="EventMarkerHalfHeight"/> on purpose: a pin reads as a pin
        /// because it is taller than it is wide, the way a real one is. Widened to match the
        /// height it would stop being a pin and start being a rounded diamond again.
        /// </remarks>
        private const float EventMarkerHalfWidth = 5f;

        /// <summary>
        /// Half the event marker's drawn height, in pixels — the same footprint the old
        /// scaled-up diamond used (<c>KeyDrawRadius * 1.35</c>), so this phase changes the shape
        /// without also relitigating how much lane height an event key is allowed to claim.
        /// </summary>
        private const float EventMarkerHalfHeight = TimelineGeometry.KeyDrawRadius * 1.35f;

        /// <summary>
        /// How far below centre the marker's flat shoulders sit before the sides taper to the
        /// point, as a fraction of <see cref="EventMarkerHalfHeight"/>.
        /// </summary>
        private const float EventMarkerShoulderFraction = 0.2f;

        /// <summary>
        /// Half-width of an event marker's grab box, in pixels.
        /// </summary>
        /// <remarks>
        /// Matched to <see cref="EventMarkerHalfWidth"/> plus roughly the same margin
        /// <see cref="TimelineGeometry.KeyHitRadius"/> carries over a pose key's draw radius (7
        /// over 5, a 2px pad) — so the new shape is exactly as forgiving to click as the old one
        /// was, no more and no less.
        /// </remarks>
        private const float EventKeyHitRadius = EventMarkerHalfWidth + 2f;

        /// <summary>
        /// Two event times within this many normalized units of each other are the same time for
        /// stacking and click-cycling purposes (D14, Task 2).
        /// </summary>
        /// <remarks>
        /// Deliberately not <c>ClipEditorWindow</c>'s frame-grid epsilon — that one is scaled by
        /// frame count because it answers "is this key on a frame boundary." This one answers "did
        /// the author place these two markers at the same instant," which two independent
        /// <c>Add Event</c> presses only ever satisfy by writing the identical
        /// <c>float</c> the playhead already held, not by landing within some fraction of a frame
        /// of each other. A value far below any frame spacing at a sane rate is what keeps this
        /// from ever folding two markers together that were placed a frame or more apart.
        /// </remarks>
        private const float CoLocatedTimeEpsilon = 1e-5f;

        /// <summary>
        /// Vertical distance, in pixels, between the drawn centres of adjacent markers in a
        /// co-located stack — before <see cref="EventStackMaxSpanPixels"/> starts compressing it.
        /// </summary>
        private const float EventStackOffsetStep = 3f;

        /// <summary>
        /// The most a co-located stack is ever allowed to spread vertically, in pixels.
        /// </summary>
        /// <remarks>
        /// The lane is 22px tall and centred on it; a pin is 13.5px of that
        /// (<see cref="EventMarkerHalfHeight"/> * 2), leaving roughly 4px of slack above and below
        /// centre before a pin's tip or shoulder would cross the lane's own edge. Capping the
        /// spread here rather than letting it grow with the group size is what keeps a five-deep
        /// stack inside its own lane instead of bleeding into the row below it — past the cap,
        /// additional markers simply overlap more tightly. That is an acceptable trade because a
        /// click never has to land on a specific one of them: <see cref="ResolveTiedClick"/> cycles
        /// through the whole group regardless of how little of any one pin is showing.
        /// </remarks>
        private const float EventStackMaxSpanPixels = 8f;

        /// <summary>
        /// How much closer one hit-tested key must be than another before OnPointerDown treats them
        /// as genuinely different distances rather than a tie.
        /// </summary>
        /// <remarks>
        /// Co-located markers share a normalized time and therefore an identical
        /// <c>TimeToX</c> result, so their distance-to-pointer values come out bit-for-bit equal —
        /// this exists only to make that comparison robust rather than to widen what counts as
        /// "the same x" the way <see cref="CoLocatedTimeEpsilon"/> does for time.
        /// </remarks>
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

        /// <summary>
        /// Chooses which member of a group tied for nearest-to-the-pointer a click should select,
        /// given whichever member (if any) is already selected (D14, Task 2).
        /// </summary>
        /// <remarks>
        /// Several event markers sharing a time draw at the same x, so a click there always ties
        /// for nearest between all of them — there is no pixel position that means "the second
        /// one" the way there is for markers spread out in time, because they are not spread out in
        /// time; that is the whole scenario. Cycling is what makes each one reachable anyway: the
        /// first click on a stack lands on its first member, and a click repeated at the same spot
        /// walks forward through the rest before wrapping back to the start. This is pure — no
        /// <see cref="VisualElement"/>, no painter — specifically so the cycling policy can be unit
        /// tested without a viewport; <see cref="OnPointerDown"/> is the only caller and supplies
        /// the group in draw order (ascending key index), which is also ascending stack slot, so
        /// "forward" here always means "down the pile" the same way the vertical offset in
        /// <see cref="OnGenerateVisualContent"/> reads.
        /// </remarks>
        /// <param name="tiedIndices">
        /// The key indices tied for nearest-to-the-pointer, in draw order. Never empty when called
        /// from <see cref="OnPointerDown"/>.
        /// </param>
        /// <param name="isSelected">Whether a given key index is currently selected.</param>
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

        /// <summary>
        /// Groups a sorted list of key times into runs of mutually co-located keys, and reports
        /// each key's position and group size within its own run (D14, Task 2).
        /// </summary>
        /// <remarks>
        /// Assumes <paramref name="sortedKeyTimes"/> is already ascending — true of every list
        /// <see cref="OnGenerateVisualContent"/> hands it, because the window keeps a track's own
        /// key list sorted (architecture section 7, <c>SortTrackKeys</c>) and builds a lane's times
        /// straight from it. That lets one linear pass find every run: a run only ever breaks when
        /// the next time has moved past <see cref="CoLocatedTimeEpsilon"/> of the run's first
        /// member, and nothing later in an ascending list can fall back inside it. Pure and static
        /// so the grouping — the thing <see cref="OnGenerateVisualContent"/> turns into a vertical
        /// offset — can be checked directly, without generating a mesh to read it back out of.
        /// </remarks>
        /// <param name="sortedKeyTimes">Key times, ascending.</param>
        /// <returns>One slot per input time, same order and length as the input.</returns>
        public static CoLocatedSlot[] ComputeCoLocatedSlots(IReadOnlyList<float> sortedKeyTimes)
        {
            int keyCount = sortedKeyTimes.Count;
            CoLocatedSlot[] slots = new CoLocatedSlot[keyCount];

            int groupStart = 0;
            for (int keyIndex = 1; keyIndex <= keyCount; keyIndex++)
            {
                bool endOfGroup = keyIndex == keyCount
                    || Mathf.Abs(sortedKeyTimes[keyIndex] - sortedKeyTimes[groupStart]) > CoLocatedTimeEpsilon;
                if (!endOfGroup)
                {
                    continue;
                }

                int groupSize = keyIndex - groupStart;
                for (int memberIndex = groupStart; memberIndex < keyIndex; memberIndex++)
                {
                    slots[memberIndex] = new CoLocatedSlot(memberIndex - groupStart, groupSize);
                }
                groupStart = keyIndex;
            }

            return slots;
        }

        /// <summary>
        /// One key's position within its own co-located stack, and how many keys share that stack.
        /// </summary>
        public readonly struct CoLocatedSlot
        {
            /// <summary>0 for the first key at a time, 1 for the second, and so on.</summary>
            public readonly int SlotIndex;

            /// <summary>How many keys, including this one, share this key's time.</summary>
            public readonly int GroupSize;

            public CoLocatedSlot(int slotIndex, int groupSize)
            {
                SlotIndex = slotIndex;
                GroupSize = groupSize;
            }
        }

        /// <summary>
        /// The vertical offset from lane centre a stack member at <paramref name="slotIndex"/> of
        /// <paramref name="groupSize"/> draws at, in pixels.
        /// </summary>
        /// <remarks>
        /// Symmetric around centre rather than growing downward from it, so a stack's middle always
        /// sits where a lone marker would — nothing about a track's key count should shift where an
        /// unrelated single marker on the same lane appears. The span is capped at
        /// <see cref="EventStackMaxSpanPixels"/> and the step shrinks to fit within it, which is
        /// what keeps an arbitrarily large stack inside its own lane instead of the step
        /// (<see cref="EventStackOffsetStep"/>) multiplying out past the lane's edge.
        /// </remarks>
        private static float StackOffsetY(int slotIndex, int groupSize)
        {
            if (groupSize <= 1)
            {
                return 0f;
            }
            float span = Mathf.Min(EventStackMaxSpanPixels, EventStackOffsetStep * (groupSize - 1));
            float step = span / (groupSize - 1);
            return -span * 0.5f + slotIndex * step;
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

            // Only every computed for an event lane: it is the one lane kind where two keys can
            // share a time (D14, Task 2), so it is the only one with a stack to offset.
            CoLocatedSlot[] eventStacks = trackKind == TimelineTrackKind.Event
                ? ComputeCoLocatedSlots(keyTimes)
                : null;

            for (int keyIndex = 0; keyIndex < keyTimes.Count; keyIndex++)
            {
                bool selected = isKeySelected != null
                    && isKeySelected(new KeyAddress(trackKind, trackIndex, keyIndex));

                Color fill = trackKind == TimelineTrackKind.Event ? EventKeyFill : KeyFill;
                painter.fillColor = selected ? KeySelectedFill : fill;
                painter.strokeColor = KeyOutline;
                painter.lineWidth = 1f;

                float x = geometry.TimeToX(keyTimes[keyIndex]);

                if (trackKind == TimelineTrackKind.Event)
                {
                    // The x is the truth about when this fires and never moves for stacking — only
                    // centreY gives, and only within the tight budget StackOffsetY enforces, so a
                    // pile of markers is visibly a pile without one of them lying about its time.
                    CoLocatedSlot stack = eventStacks[keyIndex];
                    float markerCentreY = centreY + StackOffsetY(stack.SlotIndex, stack.GroupSize);
                    DrawEventMarker(painter, x, markerCentreY);
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

        /// <summary>
        /// Draws one event marker as a pin — flat shoulders tapering to a single point at the
        /// exact key time — instead of a bigger diamond.
        /// </summary>
        /// <remarks>
        /// <strong>Why a pin and not a bigger diamond.</strong> The pose key is already a diamond,
        /// so scaling that same shape up only ever reads as "a bigger key," not "a different kind
        /// of thing" — which was the actual ask: an event obviously not-a-pose-key at a glance. A
        /// pin is also the shape most non-linear editors already use for a timeline marker, so it
        /// borrows recognition nobody watching has to learn fresh.
        /// <strong>Legibility at 8-12px is the real constraint,</strong> not looking clever at
        /// full size. Five straight edges hold their silhouette at a few pixels the way a circle
        /// or a rounded blob does not — a curve is the first thing anti-aliasing eats at small
        /// sizes, a corner is the last. And the point still lands exactly on the key's time, the
        /// same way the diamond's widest point did, so precise placement reads at a glance the
        /// same as before.
        /// </remarks>
        private static void DrawEventMarker(Painter2D painter, float x, float centreY)
        {
            float shoulderY = centreY - EventMarkerHalfHeight;
            float taperStartY = centreY + EventMarkerHalfHeight * EventMarkerShoulderFraction;
            float tipY = centreY + EventMarkerHalfHeight;

            painter.BeginPath();
            painter.MoveTo(new Vector2(x - EventMarkerHalfWidth, shoulderY));
            painter.LineTo(new Vector2(x + EventMarkerHalfWidth, shoulderY));
            painter.LineTo(new Vector2(x + EventMarkerHalfWidth, taperStartY));
            painter.LineTo(new Vector2(x, tipY));
            painter.LineTo(new Vector2(x - EventMarkerHalfWidth, taperStartY));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
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
