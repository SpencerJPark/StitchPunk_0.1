// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The ruler's tick arithmetic, and the invariant it exists to hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bug these cover was that the ruler derived tick spacing from the clip rather than from
    /// the zoom, and capped its tick count at 240. Past that length the ticks no longer landed on
    /// frames, so a label read a frame number that was not under it. The invariant worth testing is
    /// therefore not "the step is 10" but <em>a labelled frame is drawn at the pixel the geometry
    /// puts that frame at</em> — checked across zoom levels, which is exactly where it broke.
    /// </para>
    /// </remarks>
    public sealed class TimelineRulerGeometryTests
    {
        private const float LaneWidth = 800f;
        private const float MinimumLabelSpacing = 46f;

        // -------------------------------------------------------------------------------
        // The step ladder.
        // -------------------------------------------------------------------------------

        [Test]
        public void ChooseFrameStep_WalksTheOneTwoFiveLadder()
        {
            // Each case is "this many pixels per frame" -> "the step that keeps labels 46px apart".
            Assert.AreEqual(1, TimelineGeometry.ChooseFrameStep(50f, MinimumLabelSpacing),
                "A frame 50px wide needs no grouping at all.");
            Assert.AreEqual(2, TimelineGeometry.ChooseFrameStep(30f, MinimumLabelSpacing));
            Assert.AreEqual(5, TimelineGeometry.ChooseFrameStep(10f, MinimumLabelSpacing));
            Assert.AreEqual(10, TimelineGeometry.ChooseFrameStep(5f, MinimumLabelSpacing));
            Assert.AreEqual(20, TimelineGeometry.ChooseFrameStep(2.5f, MinimumLabelSpacing));
            Assert.AreEqual(50, TimelineGeometry.ChooseFrameStep(1f, MinimumLabelSpacing));
            Assert.AreEqual(100, TimelineGeometry.ChooseFrameStep(0.5f, MinimumLabelSpacing));
        }

        [Test]
        public void ChooseFrameStep_NeverReturnsAStepThatWouldCollide()
        {
            // The property, rather than the table: whatever step comes back must be wide enough.
            // A doubling ladder passes this too, which is why the table above exists as well.
            for (float pixelsPerFrame = 0.05f; pixelsPerFrame < 60f; pixelsPerFrame *= 1.15f)
            {
                int step = TimelineGeometry.ChooseFrameStep(pixelsPerFrame, MinimumLabelSpacing);
                Assert.GreaterOrEqual(
                    step * pixelsPerFrame, MinimumLabelSpacing,
                    "Step " + step + " at " + pixelsPerFrame + "px per frame would overlap.");
            }
        }

        [Test]
        public void ChooseFrameStep_IsAlwaysPositive()
        {
            Assert.AreEqual(1, TimelineGeometry.ChooseFrameStep(0f, MinimumLabelSpacing));
            Assert.AreEqual(1, TimelineGeometry.ChooseFrameStep(-4f, MinimumLabelSpacing));
            Assert.AreEqual(1, TimelineGeometry.ChooseFrameStep(10f, 0f));
        }

        [Test]
        public void ChooseMinorFrameStep_PrefersFifthsThenHalvesThenNothing()
        {
            Assert.AreEqual(2, TimelineGeometry.ChooseMinorFrameStep(10, 8f, 5f),
                "A tenth-frame step splits into fifths when there is room.");
            Assert.AreEqual(5, TimelineGeometry.ChooseMinorFrameStep(10, 1.2f, 5f),
                "Too tight for fifths, so halves.");
            Assert.AreEqual(10, TimelineGeometry.ChooseMinorFrameStep(10, 0.4f, 5f),
                "Too tight for halves, so no minor ticks at all.");
        }

        [Test]
        public void ChooseMinorFrameStep_NeverSubdividesAStepOfOne()
        {
            // A fifth of 1 is 0, and a tick every zero frames does not terminate.
            Assert.AreEqual(1, TimelineGeometry.ChooseMinorFrameStep(1, 100f, 5f));
        }

        // -------------------------------------------------------------------------------
        // Grid alignment through zero — the negative half of the ruler.
        // -------------------------------------------------------------------------------

        [Test]
        public void FloorToStep_KeepsTheGridAlignedAcrossZero()
        {
            Assert.AreEqual(10, TimelineGeometry.FloorToStep(12.5f, 10));
            Assert.AreEqual(0, TimelineGeometry.FloorToStep(4f, 10));

            // Truncation would give 0 and -10 here; flooring gives -10 and -20, which is what keeps
            // the negative ticks on the same grid as the positive ones instead of mirroring it.
            Assert.AreEqual(-10, TimelineGeometry.FloorToStep(-4f, 10));
            Assert.AreEqual(-20, TimelineGeometry.FloorToStep(-12.5f, 10));
            Assert.AreEqual(-10, TimelineGeometry.FloorToStep(-10f, 10));
        }

        // -------------------------------------------------------------------------------
        // The invariant the report asked for.
        // -------------------------------------------------------------------------------

        [Test]
        public void LabelledFrameIsDrawnWhereTheGeometryPutsThatFrame()
        {
            // 600 frames is past the old 240-tick cap, which is where ticks used to stop matching
            // the frames they claimed to mark.
            const int FrameCount = 600;

            float[] zoomLevels = { 0.25f, 0.5f, 1f, 2f, 7.5f, 40f, 200f };
            float[] panValues = { -0.4f, 0f, 0.33f, 1.2f };

            for (int zoomIndex = 0; zoomIndex < zoomLevels.Length; zoomIndex++)
            {
                for (int panIndex = 0; panIndex < panValues.Length; panIndex++)
                {
                    TimelineGeometry geometry = TimelineGeometry.Create(
                        LaneWidth, zoomLevels[zoomIndex], panValues[panIndex]);
                    float pixelsPerFrame = geometry.PixelsPerNormalizedUnit / FrameCount;
                    int step = TimelineGeometry.ChooseFrameStep(pixelsPerFrame, MinimumLabelSpacing);

                    float firstVisible = geometry.XToTime(0f) * FrameCount;
                    int frame = TimelineGeometry.FloorToStep(firstVisible, step);
                    float lastVisible = geometry.XToTime(LaneWidth) * FrameCount;

                    int checkedLabels = 0;
                    while (frame <= lastVisible && checkedLabels < 64)
                    {
                        // The ruler draws the label at TimeToX(frame / frameCount). Round-tripping
                        // that pixel back through the same converter must name the same frame — if
                        // it does not, the number is standing over a different time than it says.
                        float labelX = geometry.TimeToX(frame / (float)FrameCount);
                        float frameUnderLabel = geometry.XToTime(labelX) * FrameCount;

                        Assert.AreEqual(
                            frame, frameUnderLabel, 0.01f,
                            "Label " + frame + " sits over frame " + frameUnderLabel
                            + " at zoom " + zoomLevels[zoomIndex]
                            + ", pan " + panValues[panIndex]);

                        frame += step;
                        checkedLabels++;
                    }

                    Assert.Greater(checkedLabels, 0,
                        "No labels were produced at zoom " + zoomLevels[zoomIndex]
                        + ", pan " + panValues[panIndex] + " — the ruler would be blank.");
                }
            }
        }

        [Test]
        public void RulerCoversNegativeFramesWhenPannedBeforeTheClip()
        {
            TimelineGeometry geometry = TimelineGeometry.Create(LaneWidth, 1f, -0.5f);
            float firstVisibleFrame = geometry.XToTime(0f) * 30;

            Assert.Less(firstVisibleFrame, 0f,
                "Panning before the clip must expose negative frames — keys are allowed to live "
                + "there, so the ruler has to number there.");
        }

        [Test]
        public void TimeToXIsUnclampedSoOutOfRangeKeysHaveDistinctPositions()
        {
            TimelineGeometry geometry = TimelineGeometry.Create(LaneWidth, 1f, 0f);

            float beforeStart = geometry.TimeToX(-0.25f);
            float atStart = geometry.TimeToX(0f);
            float atEnd = geometry.TimeToX(1f);
            float pastEnd = geometry.TimeToX(1.25f);

            Assert.Less(beforeStart, atStart,
                "A key before the clip must draw left of frame zero, not stacked on it.");
            Assert.Greater(pastEnd, atEnd,
                "A key past the clip end must draw right of the end, not stacked on it.");

            // The specific failure clamping caused: two different out-of-range times collapsing to
            // one pixel, so they could be seen only as a single diamond and grabbing it got
            // whichever the hit test happened to find first.
            Assert.AreNotEqual(geometry.TimeToX(1.1f), geometry.TimeToX(1.6f));
        }
    }
}
