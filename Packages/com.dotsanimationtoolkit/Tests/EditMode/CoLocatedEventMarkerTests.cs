// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// <c>TrackLaneElement.ResolveTiedClick</c> — the same-pixel click-cycling policy that survives
    /// E6 Task 2's removal of event-marker stacking (Phase D14, Task 2).
    /// </summary>
    /// <remarks>
    /// Two keys can still tie for nearest-to-the-pointer within one lane — most often two markers
    /// sharing a time on the same event, now that every event name gets its own lane and different
    /// names can no longer collide on screen. <c>ComputeCoLocatedSlots</c> and its stacking tests
    /// were removed with the vertical stacking they existed to drive; this file now covers only the
    /// click-cycling <see cref="TrackLaneElement.OnPointerDown"/> still needs for a same-pixel tie.
    /// Pure and static specifically so the cycling policy can be checked without generating a mesh
    /// or simulating a pointer.
    /// </remarks>
    public sealed class CoLocatedEventMarkerTests
    {
        [Test]
        public void ResolveTiedClick_NothingSelected_PicksTheFirstMember()
        {
            List<int> tiedIndices = new List<int> { 4, 5, 6 };

            int chosen = TrackLaneElement.ResolveTiedClick(tiedIndices, candidate => false);

            Assert.AreEqual(4, chosen);
        }

        [Test]
        public void ResolveTiedClick_FirstMemberSelected_AdvancesToTheSecond()
        {
            List<int> tiedIndices = new List<int> { 4, 5, 6 };

            int chosen = TrackLaneElement.ResolveTiedClick(tiedIndices, candidate => candidate == 4);

            Assert.AreEqual(5, chosen);
        }

        [Test]
        public void ResolveTiedClick_LastMemberSelected_WrapsBackToTheFirst()
        {
            List<int> tiedIndices = new List<int> { 4, 5, 6 };

            int chosen = TrackLaneElement.ResolveTiedClick(tiedIndices, candidate => candidate == 6);

            Assert.AreEqual(4, chosen);
        }

        [Test]
        public void ResolveTiedClick_SelectionOutsideTheGroup_FallsBackToTheFirstMember()
        {
            // The active key belongs to some other tied group entirely — a click on this one must
            // start it fresh at the first member rather than reading a stale match.
            List<int> tiedIndices = new List<int> { 4, 5, 6 };

            int chosen = TrackLaneElement.ResolveTiedClick(tiedIndices, candidate => candidate == 99);

            Assert.AreEqual(4, chosen);
        }

        [Test]
        public void ResolveTiedClick_RepeatedClicks_VisitEveryMemberBeforeRepeating()
        {
            // Simulates three successive presses on the same tied group, each time feeding back
            // whatever the previous press chose — the same relay OnPointerDown and the window's
            // selection state form in practice.
            List<int> tiedIndices = new List<int> { 10, 11, 12 };
            int selected = -1;

            int first = TrackLaneElement.ResolveTiedClick(tiedIndices, candidate => candidate == selected);
            selected = first;
            int second = TrackLaneElement.ResolveTiedClick(tiedIndices, candidate => candidate == selected);
            selected = second;
            int third = TrackLaneElement.ResolveTiedClick(tiedIndices, candidate => candidate == selected);
            selected = third;
            int fourth = TrackLaneElement.ResolveTiedClick(tiedIndices, candidate => candidate == selected);

            CollectionAssert.AreEqual(new int[] { 10, 11, 12 }, new int[] { first, second, third });
            Assert.AreEqual(first, fourth, "A fourth click on a three-member group must wrap back to the first.");
        }

        [Test]
        public void ResolveTiedClick_SingleMember_AlwaysReturnsIt()
        {
            List<int> tiedIndices = new List<int> { 7 };

            Assert.AreEqual(7, TrackLaneElement.ResolveTiedClick(tiedIndices, candidate => true));
            Assert.AreEqual(7, TrackLaneElement.ResolveTiedClick(tiedIndices, candidate => false));
        }
    }
}
