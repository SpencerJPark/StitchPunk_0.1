// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// <c>TrackLaneElement</c>'s co-located event marker stacking and click-cycling (Phase D14,
    /// Task 2).
    /// </summary>
    /// <remarks>
    /// Several event markers can now share a normalized time — the data already allowed it, and
    /// this phase makes the timeline draw and select them individually instead of the second and
    /// third silently hiding under the first. Both methods under test are pure and static
    /// specifically so this file can check the grouping and the cycling policy without generating
    /// a mesh or simulating a pointer: <c>ComputeCoLocatedSlots</c> is what
    /// <c>OnGenerateVisualContent</c> turns into a vertical offset, and <c>ResolveTiedClick</c> is
    /// what <c>OnPointerDown</c> uses to decide which tied member a click lands on.
    /// </remarks>
    public sealed class CoLocatedEventMarkerTests
    {
        // -----------------------------------------------------------------------------------
        // ComputeCoLocatedSlots
        // -----------------------------------------------------------------------------------

        [Test]
        public void ComputeCoLocatedSlots_AllDistinctTimes_EveryKeyIsItsOwnGroupOfOne()
        {
            List<float> keyTimes = new List<float> { 0.1f, 0.3f, 0.6f };

            TrackLaneElement.CoLocatedSlot[] slots =
                TrackLaneElement.ComputeCoLocatedSlots(keyTimes);

            Assert.AreEqual(3, slots.Length);
            for (int index = 0; index < slots.Length; index++)
            {
                Assert.AreEqual(0, slots[index].SlotIndex, "keyIndex " + index);
                Assert.AreEqual(1, slots[index].GroupSize, "keyIndex " + index);
            }
        }

        [Test]
        public void ComputeCoLocatedSlots_ThreeSharedTime_AreOneGroupWithAscendingSlots()
        {
            List<float> keyTimes = new List<float> { 0.5f, 0.5f, 0.5f };

            TrackLaneElement.CoLocatedSlot[] slots =
                TrackLaneElement.ComputeCoLocatedSlots(keyTimes);

            Assert.AreEqual(3, slots.Length);
            for (int index = 0; index < slots.Length; index++)
            {
                Assert.AreEqual(index, slots[index].SlotIndex);
                Assert.AreEqual(3, slots[index].GroupSize);
            }
        }

        [Test]
        public void ComputeCoLocatedSlots_TwoSeparateGroups_DoNotMergeAcrossTheGap()
        {
            // Two markers at 0.2, a lone one at 0.5, and two more at 0.8 — three runs, and the
            // lone one in the middle must not be folded into either neighbour.
            List<float> keyTimes = new List<float> { 0.2f, 0.2f, 0.5f, 0.8f, 0.8f };

            TrackLaneElement.CoLocatedSlot[] slots =
                TrackLaneElement.ComputeCoLocatedSlots(keyTimes);

            Assert.AreEqual(2, slots[0].GroupSize);
            Assert.AreEqual(0, slots[0].SlotIndex);
            Assert.AreEqual(2, slots[1].GroupSize);
            Assert.AreEqual(1, slots[1].SlotIndex);

            Assert.AreEqual(1, slots[2].GroupSize);
            Assert.AreEqual(0, slots[2].SlotIndex);

            Assert.AreEqual(2, slots[3].GroupSize);
            Assert.AreEqual(0, slots[3].SlotIndex);
            Assert.AreEqual(2, slots[4].GroupSize);
            Assert.AreEqual(1, slots[4].SlotIndex);
        }

        [Test]
        public void ComputeCoLocatedSlots_TimesAFrameApart_StayAsSeparateGroups()
        {
            // A frame apart at any sane rate is many multiples of the epsilon this groups by — two
            // markers authored on adjacent frames must never be mistaken for one co-located pair.
            List<float> keyTimes = new List<float> { 0.100f, 0.133f };

            TrackLaneElement.CoLocatedSlot[] slots =
                TrackLaneElement.ComputeCoLocatedSlots(keyTimes);

            Assert.AreEqual(1, slots[0].GroupSize);
            Assert.AreEqual(1, slots[1].GroupSize);
        }

        [Test]
        public void ComputeCoLocatedSlots_EmptyList_ReturnsEmptyArray()
        {
            TrackLaneElement.CoLocatedSlot[] slots =
                TrackLaneElement.ComputeCoLocatedSlots(new List<float>());

            Assert.AreEqual(0, slots.Length);
        }

        // -----------------------------------------------------------------------------------
        // ResolveTiedClick
        // -----------------------------------------------------------------------------------

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
            // The active key belongs to some other stack (or lane) entirely — a click on this one
            // must start it fresh at the first member rather than reading a stale match.
            List<int> tiedIndices = new List<int> { 4, 5, 6 };

            int chosen = TrackLaneElement.ResolveTiedClick(tiedIndices, candidate => candidate == 99);

            Assert.AreEqual(4, chosen);
        }

        [Test]
        public void ResolveTiedClick_RepeatedClicks_VisitEveryMemberBeforeRepeating()
        {
            // Simulates three successive presses on the same stack, each time feeding back whatever
            // the previous press chose — the same relay OnPointerDown and the window's selection
            // state form in practice.
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
            Assert.AreEqual(first, fourth, "A fourth click on a three-member stack must wrap back to the first.");
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
