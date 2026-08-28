// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <see cref="EventLaneAddressing"/> — the pure mapping between a flat
    /// <c>ClipAsset.events</c> list and the per-event-name lane a timeline track index now
    /// identifies (E6 Task 2).
    /// </summary>
    public sealed class EventLaneAddressingTests
    {
        [Test]
        public void ComputeLaneKeys_SameTimeDifferentNames_AreSeparateLanesInFirstAppearanceOrder()
        {
            // Footstep and Damage landing on the same frame is the exact scenario the shared "Events"
            // lane used to stack; each must resolve to its own lane now, with no regard to time.
            List<EventMarker> events = new List<EventMarker>
            {
                new EventMarker { normalizedTime = 0.5f, eventKey = 30u },
                new EventMarker { normalizedTime = 0.5f, eventKey = 17u },
            };

            List<uint> laneKeys = EventLaneAddressing.ComputeLaneKeys(events);

            CollectionAssert.AreEqual(new uint[] { 30u, 17u }, laneKeys);
        }

        [Test]
        public void ResolveFlatIndex_TwoMarkersSharingAKey_MapLaneLocalIndicesToTheirOwnFlatSlots()
        {
            List<EventMarker> events = new List<EventMarker>
            {
                new EventMarker { normalizedTime = 0.1f, eventKey = 30u },
                new EventMarker { normalizedTime = 0.5f, eventKey = 17u },
                new EventMarker { normalizedTime = 0.9f, eventKey = 30u },
            };

            // Lane 0 is key 30u (first appearance) and holds flat slots 0 and 2, in that order;
            // lane 1 is key 17u and holds only flat slot 1.
            Assert.AreEqual(0, EventLaneAddressing.ResolveFlatIndex(events, 0, 0));
            Assert.AreEqual(2, EventLaneAddressing.ResolveFlatIndex(events, 0, 1));
            Assert.AreEqual(1, EventLaneAddressing.ResolveFlatIndex(events, 1, 0));
            Assert.AreEqual(-1, EventLaneAddressing.ResolveFlatIndex(events, 0, 2), "Lane 0 has only two members.");
            Assert.AreEqual(-1, EventLaneAddressing.ResolveFlatIndex(events, 2, 0), "There is no third lane.");
        }
    }
}
