// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The tick strip above the lanes (architecture section 7.2).
    /// </summary>
    /// <remarks>
    /// Ticks come from <see cref="TimelineGeometry"/>, the same converter the lanes and playhead
    /// use, so a tick labelled 0.5s sits exactly where a key at 0.5s is drawn. A ruler with its own
    /// maths is the classic way for a timeline to start lying about where things are.
    /// </remarks>
    public sealed class TimeRulerElement : VisualElement
    {
        private static readonly Color RulerBackground = new Color(0.15f, 0.15f, 0.16f);
        private static readonly Color MajorTick = new Color(0.70f, 0.70f, 0.72f);
        private static readonly Color MinorTick = new Color(0.38f, 0.38f, 0.40f);

        /// <summary>Frame 0 and the last frame, tinted to match the clip boundary lines.</summary>
        private static readonly Color ClipBoundaryLabel = new Color(0.85f, 0.62f, 0.28f);

        /// <summary>
        /// How much room a frame number needs, and how much a minor tick needs.
        /// </summary>
        /// <remarks>
        /// Shared by the tick painter and the label builder so the two cannot pick different steps
        /// and leave numbers floating between ticks.
        /// </remarks>
        private const float MinimumLabelSpacingPixels = 46f;
        private const float MinimumMinorSpacingPixels = 5f;

        /// <summary>Clip length in seconds, used only for the labels.</summary>
        public float durationSeconds = 1f;

        /// <summary>Number of frames spanning the clip; also the minor tick count.</summary>
        public int frameCount = 30;

        /// <summary>
        /// The timeline view, pushed in by the window. Never derived here: a lane that computed its
        /// own zoom would drift from the ruler's, which is the bug TimelineGeometry exists to stop.
        /// </summary>
        public float viewZoom = 1f;
        public float viewPan;


        /// <summary>Raised when the pointer scrubs the ruler, with the normalized time.</summary>
        public event System.Action<float> scrubbed;

        /// <summary>
        /// Height and shrink come from ClipEditorWindow.uss, which pairs this element's height with
        /// the spacer above the track headers. Inline styles would win over that rule and put the
        /// two stacks one row out of step with no way to correct it from the stylesheet.
        /// </summary>
        public const string UssClassName = "clip-editor__ruler";

        public TimeRulerElement()
        {
            AddToClassList(UssClassName);
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);

            // A resize changes where every second lands, and is the one trigger the window cannot
            // see for itself.
            RegisterCallback<GeometryChangedEvent>(geometryEvent => RefreshSecondLabels());
        }

        private void OnPointerDown(PointerDownEvent pointerEvent)
        {
            this.CapturePointer(pointerEvent.pointerId);
            RaiseScrub(pointerEvent.localPosition.x, pointerEvent.altKey);
            pointerEvent.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent moveEvent)
        {
            if (!this.HasPointerCapture(moveEvent.pointerId))
            {
                return;
            }
            RaiseScrub(moveEvent.localPosition.x, moveEvent.altKey);
        }

        private void OnPointerUp(PointerUpEvent upEvent)
        {
            this.ReleasePointer(upEvent.pointerId);
        }

        /// <summary>
        /// Reports a scrub, snapped to the frame grid unless the caller asks for a free scrub.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Snapped by default.</strong> A frame is the unit the clip is actually evaluated
        /// at, so an unsnapped playhead shows a pose between two frames — a pose the game will never
        /// display. Landing on frames by default means what the viewport shows is a frame that
        /// exists.
        /// </para>
        /// <para>
        /// <strong>Alt scrubs freely</strong>, not Shift: Shift already means "larger step" on the
        /// arrow keys, and one modifier meaning two things in the same window is how a shortcut
        /// stops being learnable. Alt is Unity's usual "ignore the grid" modifier.
        /// </para>
        /// </remarks>
        private void RaiseScrub(float localX, bool freeScrub)
        {
            if (scrubbed == null)
            {
                return;
            }

            float normalizedTime = TimelineGeometry.Create(contentRect.width, viewZoom, viewPan).XToTime(localX);
            if (!freeScrub)
            {
                int frames = Mathf.Max(1, frameCount);
                normalizedTime = Mathf.Clamp01(Mathf.Round(normalizedTime * frames) / frames);
            }
            scrubbed(normalizedTime);
        }

        /// <summary>
        /// Rebuilds the frame-number labels. Call after changing the view or the clip timing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Never call this from <c>generateVisualContent</c>.</strong> It did exactly that
        /// once: Clear() and Add() mutate the visual tree, and mutating the tree from inside a
        /// repaint neither clears reliably nor re-lays-out, so labels accumulated on every zoom step
        /// and stacked into an unreadable smear. Repaint draws; it does not restructure.
        /// </para>
        /// <para>
        /// <strong>Labels are frame numbers now, not seconds.</strong> The seconds stride was
        /// computed from the <em>unzoomed</em> track width, so it never responded to zoom at all --
        /// which is what made the numbering collide as you zoomed out. Frames are also what the
        /// ticks are made of, so numbering frames means the number above a tick is that tick.
        /// Seconds remain readable in the transport bar and in this element tooltip.
        /// </para>
        /// <para>
        /// Labels span the <em>visible</em> range rather than the clip, so they continue into
        /// negative frames and past the clip end -- which is where keys are now allowed to live.
        /// </para>
        /// </remarks>
        public void RefreshSecondLabels()
        {
            Clear();

            float width = contentRect.width;
            if (width <= 0f)
            {
                return;
            }

            int frames = Mathf.Max(1, frameCount);
            TimelineGeometry geometry = TimelineGeometry.Create(width, viewZoom, viewPan);
            float pixelsPerFrame = geometry.PixelsPerNormalizedUnit / frames;
            if (pixelsPerFrame <= 0f)
            {
                return;
            }

            tooltip = "Frame numbers. Clip is " + durationSeconds.ToString("0.###")
                + "s over " + frames.ToString() + " frames.";

            int labelStep = TimelineGeometry.ChooseFrameStep(
                pixelsPerFrame, MinimumLabelSpacingPixels);

            float firstVisibleFrame = geometry.XToTime(0f) * frames;
            float lastVisibleFrame = geometry.XToTime(width) * frames;
            int frame = TimelineGeometry.FloorToStep(firstVisibleFrame, labelStep);

            // A ceiling on how many labels one pass may create. The step ladder bounds the loop in
            // every sane case; this is here so a degenerate view cannot spin building labels.
            const int MaximumLabels = 256;
            int created = 0;
            while (frame <= lastVisibleFrame && created < MaximumLabels)
            {
                Label marker = new Label(frame.ToString());
                marker.pickingMode = PickingMode.Ignore;
                marker.style.position = Position.Absolute;
                marker.style.left = geometry.TimeToX(frame / (float)frames) + 2f;
                marker.style.top = 0f;
                marker.style.fontSize = 9f;
                marker.style.color = frame == 0 || frame == frames
                    ? ClipBoundaryLabel : MajorTick;
                Add(marker);

                frame += labelStep;
                created++;
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
            painter.fillColor = RulerBackground;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, 0f));
            painter.LineTo(new Vector2(rect.width, 0f));
            painter.LineTo(new Vector2(rect.width, rect.height));
            painter.LineTo(new Vector2(0f, rect.height));
            painter.ClosePath();
            painter.Fill();

            TimelineGeometry geometry = TimelineGeometry.Create(rect.width, viewZoom, viewPan);
            TimelineRangeShading.Paint(painter, geometry, rect);

            // Ticks are real frames across the visible range, not a fixed subdivision of the clip.
            // The old version capped the count at 240 and spaced ticks by clip fraction, so a clip
            // longer than 240 frames drew ticks that did not land on frames at all -- the ruler was
            // measuring something that did not exist.
            int frames = Mathf.Max(1, frameCount);
            float pixelsPerFrame = geometry.PixelsPerNormalizedUnit / frames;
            if (pixelsPerFrame <= 0f)
            {
                return;
            }

            int labelStep = TimelineGeometry.ChooseFrameStep(
                pixelsPerFrame, MinimumLabelSpacingPixels);
            int minorStep = TimelineGeometry.ChooseMinorFrameStep(
                labelStep, pixelsPerFrame, MinimumMinorSpacingPixels);

            float firstVisibleFrame = geometry.XToTime(0f) * frames;
            float lastVisibleFrame = geometry.XToTime(rect.width) * frames;
            int frame = TimelineGeometry.FloorToStep(firstVisibleFrame, minorStep);

            const int MaximumTicks = 2048;
            int drawn = 0;
            painter.lineWidth = 1f;
            while (frame <= lastVisibleFrame && drawn < MaximumTicks)
            {
                // Major exactly when a label sits here, so a number never appears over a short tick
                // and a tall tick never appears bare.
                bool major = (frame % labelStep) == 0;
                float x = Mathf.Round(geometry.TimeToX(frame / (float)frames)) + 0.5f;
                painter.strokeColor = major ? MajorTick : MinorTick;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, major ? rect.height * 0.35f : rect.height * 0.65f));
                painter.LineTo(new Vector2(x, rect.height));
                painter.Stroke();

                frame += minorStep;
                drawn++;
            }
        }
    }
}
