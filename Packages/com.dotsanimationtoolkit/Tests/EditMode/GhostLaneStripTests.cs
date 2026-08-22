// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Editor;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Guards how many empty rows the timeline puts under its last track.
    /// </summary>
    /// <remarks>
    /// <strong>The count is what makes the empty area selectable, not what makes it look right.</strong>
    /// A box select can only begin on an element, so a row short of the bottom edge is a strip of
    /// timeline a drag cannot be started in — the bug these rows exist to remove. Rounding down
    /// would leave one every time the pane height is not a whole number of rows, which is nearly
    /// always, and it would look almost correct while doing it.
    /// </remarks>
    public sealed class GhostLaneStripTests
    {
        [Test]
        public void RowCount_IsZero_WhenTheTracksAlreadyFillTheTimeline()
        {
            Assert.AreEqual(0, GhostLaneStripElement.RowCountForHeight(0f, 22f));
            Assert.AreEqual(
                0, GhostLaneStripElement.RowCountForHeight(-140f, 22f),
                "Tracks taller than the viewport leave negative room; the strip has to disappear " +
                "rather than wrap around to a huge count.");
        }

        [Test]
        public void RowCount_IsOne_BeforeTheRowHeightHasBeenLaidOut()
        {
            // The height comes from the stylesheet by way of a row, so the first pass has nothing
            // to measure. It builds the one row the next pass reads.
            Assert.AreEqual(1, GhostLaneStripElement.RowCountForHeight(220f, 0f));

            // An element that has never been laid out answers NaN rather than zero, and NaN loses
            // every comparison it is given. Dividing by it would round to no rows at all, and the
            // strip would sit there empty with no further layout pass coming to correct it.
            Assert.AreEqual(1, GhostLaneStripElement.RowCountForHeight(220f, float.NaN));
        }

        [Test]
        public void RowCount_FillsExactly_WhenTheSpaceDividesEvenly()
        {
            Assert.AreEqual(10, GhostLaneStripElement.RowCountForHeight(220f, 22f));
        }

        [Test]
        public void RowCount_CoversThePartialRow_SoNoDeadStripIsLeftAtTheBottom()
        {
            // 221 of 22 is ten rows and a sliver. The eleventh is clipped by the strip's overflow,
            // and that sliver is live area a drag can start in.
            Assert.AreEqual(11, GhostLaneStripElement.RowCountForHeight(221f, 22f));
            Assert.AreEqual(1, GhostLaneStripElement.RowCountForHeight(3f, 22f));
        }
    }
}
