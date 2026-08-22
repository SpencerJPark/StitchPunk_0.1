// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The empty rows under the last track, drawn to the bottom of the timeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>These rows exist to be pressed on, not to be looked at.</strong> A box select starts
    /// on whichever element the pointer went down on, so before this the space below a short clip's
    /// rows belonged to no element at all and a band could not be started there: the one part of the
    /// timeline with room to begin a drag was the one part that refused to.
    /// </para>
    /// <para>
    /// The strip picks and its rows do not. One capture target for the whole area means a drag that
    /// starts in one ghost row and travels through several keeps its pointer, which is what a band
    /// begun near the bottom edge does every time.
    /// </para>
    /// <para>
    /// Row height is never written here. It comes from <c>--clip-editor-lane-height</c> by way of
    /// the row class, and the count is derived by measuring a row that already exists — a copy of
    /// that number in C# would be free to drift from the track headers it has to line up with.
    /// </para>
    /// </remarks>
    public sealed class GhostLaneStripElement : VisualElement
    {
        public const string UssClassName = "clip-editor__ghost-lanes";
        public const string RowUssClassName = "clip-editor__ghost-lane";

        /// <summary>
        /// Darker than any real lane, including the dimmed channel rows, so the boundary between
        /// "rows with a track behind them" and "room left over" is readable without a label.
        /// </summary>
        private static readonly Color GhostBackground = new Color(0.132f, 0.132f, 0.140f);
        private static readonly Color GhostAlternate = new Color(0.148f, 0.148f, 0.157f);

        /// <summary>
        /// The timeline view, pushed in by the window exactly as it is pushed into the lanes. The
        /// strip converts a press to a time with it, and the rows shade the out-of-clip span with
        /// it so that shading does not stop halfway down the timeline.
        /// </summary>
        public float viewZoom = 1f;
        public float viewPan;
        public float viewLaneWidth;

        /// <summary>Raised when the pointer presses a ghost row, with the normalized time under it.</summary>
        public event Action<float, PointerDownEvent> ghostPointerDown;

        private bool firstRowIsAlternate;

        /// <summary>The height last asked for, so a later layout pass can act on it again.</summary>
        private float requestedHeight;

        public GhostLaneStripElement()
        {
            AddToClassList(UssClassName);
            RegisterCallback<PointerDownEvent>(OnPointerDown);

            // The second pass, and the reason the first one is allowed to be wrong. A row's height
            // is only knowable once a row has been laid out, so the pass that creates the first row
            // has nothing to divide the strip by and settles for one row. This brings us back the
            // moment that row has a size, and the count is right from then on. It cannot run away:
            // rows are added inside a strip whose height is already fixed, so filling it changes
            // nobody's geometry and no further event is raised.
            RegisterCallback<GeometryChangedEvent>(geometryEvent => ApplyRows());
        }

        private float ResolvedWidth
        {
            get { return viewLaneWidth > 1f ? viewLaneWidth : contentRect.width; }
        }

        private TimelineGeometry Geometry
        {
            get { return TimelineGeometry.Create(ResolvedWidth, viewZoom, viewPan); }
        }

        /// <summary>
        /// How many rows cover a strip of the given height, counting a partial row at the bottom —
        /// the strip clips it, and half a row of live area beats a dead gap above the edge.
        /// </summary>
        /// <param name="stripHeight">The space left under the last track row, in pixels.</param>
        /// <param name="rowHeight">
        /// A row's resolved height. Zero or NaN before that row has been through a layout pass.
        /// </param>
        /// <returns>
        /// Zero when there is no room, and one while the row height is still unknown — that one row
        /// is what the next pass measures to learn the height.
        /// </returns>
        public static int RowCountForHeight(float stripHeight, float rowHeight)
        {
            if (stripHeight < 1f)
            {
                return 0;
            }

            // Negated rather than written as "< 1f" so that an unmeasured NaN takes this branch as
            // well. Comparing NaN the other way round is false, which would send it on to divide by
            // it and answer with whatever CeilToInt does with a NaN — a count, silently, of none.
            if (!(rowHeight >= 1f))
            {
                return 1;
            }
            return Mathf.CeilToInt(stripHeight / rowHeight);
        }

        /// <summary>
        /// Resizes the strip to the space left under the tracks and fills it with rows.
        /// </summary>
        /// <remarks>
        /// The height is written inline because it is measured rather than authored — the same
        /// reason the ruler's frame markers set their own <c>left</c>. Everything about a row that
        /// could be authored still lives in the stylesheet.
        /// </remarks>
        /// <param name="availableHeight">Timeline height left under the last track row, in pixels.</param>
        /// <param name="continuesOnAlternateRow">
        /// Whether the first ghost row is an odd row, so the stripes carry on from the tracks above
        /// instead of restarting and putting two rows of the same shade against each other.
        /// </param>
        public void SyncRows(float availableHeight, bool continuesOnAlternateRow)
        {
            requestedHeight = availableHeight;
            firstRowIsAlternate = continuesOnAlternateRow;
            ApplyRows();
        }

        private void ApplyRows()
        {
            // Floored, so the rows can never total a fraction of a pixel more than the viewport
            // holds. They are sized to fill it exactly, and "exactly" plus float error is enough to
            // make the scroll view grow a vertical scrollbar with nothing to scroll.
            float stripHeight = Mathf.Max(0f, Mathf.Floor(requestedHeight));
            style.height = stripHeight;

            int rowCount = 0;
            if (stripHeight >= 1f)
            {
                // One row has to exist before there is a height to read off one. Topped up rather
                // than cut back to one: shrinking the strip to a single row on every pass would
                // rebuild the whole thing each time anything at all changed.
                if (childCount == 0)
                {
                    EnsureRowCount(1);
                }
                rowCount = RowCountForHeight(stripHeight, this[0].resolvedStyle.height);
            }
            EnsureRowCount(rowCount);
            ApplyRowShading();
        }

        /// <summary>Pushes the view into the strip and repaints its rows.</summary>
        public void PushView(float laneWidth, float zoom, float pan)
        {
            viewLaneWidth = laneWidth;
            viewZoom = zoom;
            viewPan = pan;
            for (int rowIndex = 0; rowIndex < childCount; rowIndex++)
            {
                this[rowIndex].MarkDirtyRepaint();
            }
        }

        private void EnsureRowCount(int rowCount)
        {
            while (childCount > rowCount)
            {
                RemoveAt(childCount - 1);
            }
            while (childCount < rowCount)
            {
                GhostRowElement row = new GhostRowElement(this);
                row.isAlternateRow = ((childCount + (firstRowIsAlternate ? 1 : 0)) & 1) == 1;
                Add(row);
            }
        }

        private void ApplyRowShading()
        {
            int rowOffset = firstRowIsAlternate ? 1 : 0;
            for (int rowIndex = 0; rowIndex < childCount; rowIndex++)
            {
                GhostRowElement row = this[rowIndex] as GhostRowElement;
                if (row == null)
                {
                    continue;
                }
                row.isAlternateRow = ((rowIndex + rowOffset) & 1) == 1;
                row.MarkDirtyRepaint();
            }
        }

        private void OnPointerDown(PointerDownEvent pointerEvent)
        {
            if (ghostPointerDown == null)
            {
                return;
            }
            ghostPointerDown(Geometry.XToTime(pointerEvent.localPosition.x), pointerEvent);
        }

        /// <summary>
        /// One empty row. Nested because nothing outside the strip has any reason to build one, and
        /// a row on its own — with no strip to take its view from — would draw the wrong shading.
        /// </summary>
        private sealed class GhostRowElement : VisualElement
        {
            private readonly GhostLaneStripElement strip;

            public bool isAlternateRow;

            public GhostRowElement(GhostLaneStripElement owningStrip)
            {
                strip = owningStrip;
                AddToClassList(RowUssClassName);

                // The strip owns the press. A row that picked would take the capture with it, and a
                // band dragged out of the row it started in would stop receiving moves.
                pickingMode = PickingMode.Ignore;
                generateVisualContent += OnGenerateVisualContent;
            }

            private void OnGenerateVisualContent(MeshGenerationContext context)
            {
                Rect rect = contentRect;
                if (rect.width <= 0f || rect.height <= 0f)
                {
                    return;
                }

                Painter2D painter = context.painter2D;
                painter.fillColor = isAlternateRow ? GhostAlternate : GhostBackground;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, 0f));
                painter.LineTo(new Vector2(rect.width, 0f));
                painter.LineTo(new Vector2(rect.width, rect.height));
                painter.LineTo(new Vector2(0f, rect.height));
                painter.ClosePath();
                painter.Fill();

                // Shared with the ruler and the lanes, so where the clip ends is one line down the
                // whole timeline rather than a line that stops at the last track.
                TimelineRangeShading.Paint(painter, strip.Geometry, rect);
            }
        }
    }
}
