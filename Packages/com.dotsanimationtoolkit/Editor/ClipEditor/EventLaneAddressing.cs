// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Maps an event marker's <see cref="KeyAddress"/> between the flat, unordered storage
    /// <see cref="ClipAsset.events"/> uses and the per-event-name lane a track index identifies.
    /// A pure function of the marker list alone — no registry, no clip set — so the timeline and
    /// the copy/paste buffer always agree on which lane a marker belongs to.
    /// </summary>
    public static class EventLaneAddressing
    {
        /// <summary>Every distinct event key present, in first-appearance order — one entry per lane.</summary>
        public static List<uint> ComputeLaneKeys(List<EventMarker> events)
        {
            List<uint> laneKeys = new List<uint>();
            if (events == null)
            {
                return laneKeys;
            }
            for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
            {
                uint eventKey = events[eventIndex].eventKey;
                if (!laneKeys.Contains(eventKey))
                {
                    laneKeys.Add(eventKey);
                }
            }
            return laneKeys;
        }

        /// <summary>The flat list positions belonging to one lane, in flat (storage) order.</summary>
        public static List<int> ResolveLaneFlatIndices(List<EventMarker> events, int laneIndex)
        {
            List<int> flatIndices = new List<int>();
            if (events == null)
            {
                return flatIndices;
            }
            List<uint> laneKeys = ComputeLaneKeys(events);
            if (laneIndex < 0 || laneIndex >= laneKeys.Count)
            {
                return flatIndices;
            }
            uint targetKey = laneKeys[laneIndex];
            for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
            {
                if (events[eventIndex].eventKey == targetKey)
                {
                    flatIndices.Add(eventIndex);
                }
            }
            return flatIndices;
        }

        /// <summary>The flat storage index a lane-local address points to, or -1 when it addresses nothing.</summary>
        public static int ResolveFlatIndex(List<EventMarker> events, int laneIndex, int localIndex)
        {
            List<int> flatIndices = ResolveLaneFlatIndices(events, laneIndex);
            return localIndex >= 0 && localIndex < flatIndices.Count ? flatIndices[localIndex] : -1;
        }
    }
}
