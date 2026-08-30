// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The one place that converts between cutscene time (raw seconds) and timeline pixels.
    /// </summary>
    /// <remarks>
    /// <strong>Deliberately not <see cref="TimelineGeometry"/> (decision G-D2).</strong> That type
    /// converts <em>normalized</em> time against a fixed clip duration; a cutscene's length is
    /// elastic (Phase G spec §2 — hold points pause the clock rather than bounding it), so there is
    /// no duration to normalize against and every lane here is addressed in raw seconds instead. Pan
    /// is left to a <c>ScrollView</c> around the lane stack rather than reimplemented here — the
    /// clip editor's own pan field exists only because it has no native scroll container to lean on.
    /// </remarks>
    public struct CutsceneTimelineGeometry
    {
        /// <summary>Left inset before time 0, in pixels — room for a marker centred exactly on t=0 to still be grabbable.</summary>
        public float leftPadding;

        /// <summary>Pixels spanned by one second at the current zoom.</summary>
        public float pixelsPerSecond;

        /// <summary>The slowest zoom the timeline allows, in pixels per second.</summary>
        public const float MinimumPixelsPerSecond = 4f;

        /// <summary>The fastest zoom the timeline allows, in pixels per second.</summary>
        public const float MaximumPixelsPerSecond = 400f;

        public static CutsceneTimelineGeometry Create(float pixelsPerSecond)
        {
            return new CutsceneTimelineGeometry
            {
                leftPadding = 10f,
                pixelsPerSecond = Mathf.Clamp(
                    pixelsPerSecond, MinimumPixelsPerSecond, MaximumPixelsPerSecond)
            };
        }

        /// <summary>Seconds to local x, in the lane's own (scrollable) content space.</summary>
        public float TimeToX(float seconds)
        {
            return leftPadding + seconds * pixelsPerSecond;
        }

        /// <summary>
        /// Local x to seconds. Clamped to 0 — unlike a clip's key timeline, nothing here authors
        /// negative time, so a drag past the left edge holds at the start rather than reading as a
        /// time that could never play.
        /// </summary>
        public float XToTime(float x)
        {
            return Mathf.Max(0f, (x - leftPadding) / pixelsPerSecond);
        }

        /// <summary>
        /// The largest step from the 1-2-5 ladder (in seconds) whose on-screen spacing is still at
        /// least <paramref name="minimumSpacingPixels"/> wide. Same ladder
        /// <see cref="TimelineGeometry.ChooseFrameStep"/> uses for frames — 1, 2, 5, 10, 20, 50... —
        /// because those are the intervals a ruler reads without arithmetic, in any unit.
        /// </summary>
        public static float ChooseSecondsStep(float pixelsPerSecond, float minimumSpacingPixels)
        {
            if (pixelsPerSecond <= 0f || minimumSpacingPixels <= 0f)
            {
                return 1f;
            }

            int mantissaIndex = 0;
            float decade = 0.01f;
            float step = decade;
            int guard = 0;
            while (step * pixelsPerSecond < minimumSpacingPixels && guard < 64)
            {
                mantissaIndex++;
                if (mantissaIndex >= 3)
                {
                    mantissaIndex = 0;
                    decade *= 10f;
                }
                step = MantissaAt(mantissaIndex) * decade;
                guard++;
            }
            return step;
        }

        private static float MantissaAt(int mantissaIndex)
        {
            switch (mantissaIndex)
            {
                case 1: return 2f;
                case 2: return 5f;
                default: return 1f;
            }
        }
    }
}
