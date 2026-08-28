// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Maps an event marker's <see cref="KeyAddress"/> between the flat, unordered storage
    /// <see cref="ClipAsset.events"/> uses and the per-event-name lane a track index now identifies
    /// (E6 Task 2: "an event lane must be addressed per event name").
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A pure function of the marker list alone — no registry, no clip set.</strong> Lane
    /// order is first-appearance order in the list, not name order: sorting by name would need a
    /// registry threaded into every caller, and a lane's numeric <em>position</em> is UI addressing
    /// nobody reads meaning into — only its label, resolved separately for display, has to be a name
    /// (spec §4.2.3). Keeping this a pure function of <c>events</c> is also what lets both the
    /// timeline (<see cref="ClipEditorWindow"/>) and the copy/paste buffer
    /// (<see cref="ClipKeyClipboard"/>, a static class with no window instance to share state with)
    /// agree on which lane a marker belongs to without sharing anything else.
    /// </para>
    /// <para>
    /// <strong>Storage order does not change what a lane contains.</strong> Sorting one lane (see
    /// <c>ClipEditorWindow.SortTrackKeys</c>) writes its markers back into the same flat slots they
    /// already occupied, in their new time order — it never touches another lane's slots. That
    /// leaves <c>events</c> no longer globally time-sorted, which is safe only because nothing
    /// downstream needs it to be: validation checks events against V04/V09 only, never V03, and
    /// <c>ClipRegistryBuilder.FillEvents</c> re-sorts by time before baking regardless of authoring
    /// order.
    /// </para>
    /// </remarks>
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
