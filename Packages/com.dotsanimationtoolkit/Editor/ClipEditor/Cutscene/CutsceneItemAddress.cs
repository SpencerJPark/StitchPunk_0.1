// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Which of a cutscene's lanes an item belongs to. <c>None</c> addresses the slot header itself, which holds no items.</summary>
    public enum SelectedLaneKind
    {
        None,
        ClipBlock,
        RootTransformKey,
        FacingKey,
        PartTrackHeader,
        PartTrackKey,
        AttachMarker,
        MarkKey,
        LayerStopKey,
        CameraKey,
        Event,
        Hold
    }

    /// <summary>
    /// Identifies one cutscene timeline item by position rather than by reference: every lane holds
    /// plain serializable structs in lists, so a re-sort, delete or undo replaces the instances and
    /// a reference-holding selection would go stale without saying so.
    /// </summary>
    public readonly struct CutsceneItemAddress : IEquatable<CutsceneItemAddress>
    {
        /// <summary>−1 for the cutscene-scoped lanes (camera, events, holds).</summary>
        public readonly int slotIndex;

        public readonly SelectedLaneKind laneKind;

        /// <summary>−1 unless <see cref="laneKind"/> is a part track.</summary>
        public readonly int partTrackIndex;

        /// <summary>−1 addresses the lane itself rather than an item in it.</summary>
        public readonly int itemIndex;

        public CutsceneItemAddress(int slotIndex, SelectedLaneKind laneKind, int partTrackIndex, int itemIndex)
        {
            this.slotIndex = slotIndex;
            this.laneKind = laneKind;
            this.partTrackIndex = partTrackIndex;
            this.itemIndex = itemIndex;
        }

        /// <summary>Whether this addresses a real item, rather than a bare lane or nothing at all.</summary>
        public bool HasItem
        {
            get { return itemIndex >= 0 && laneKind != SelectedLaneKind.None; }
        }

        /// <summary>The same lane as this address, without the item — what "these two are in one list" compares.</summary>
        public CutsceneItemAddress LaneOnly()
        {
            return new CutsceneItemAddress(slotIndex, laneKind, partTrackIndex, -1);
        }

        public bool Equals(CutsceneItemAddress other)
        {
            return slotIndex == other.slotIndex
                && laneKind == other.laneKind
                && partTrackIndex == other.partTrackIndex
                && itemIndex == other.itemIndex;
        }

        public override bool Equals(object other)
        {
            return other is CutsceneItemAddress && Equals((CutsceneItemAddress)other);
        }

        public override int GetHashCode()
        {
            return ((slotIndex * 397 + (int)laneKind) * 397 + partTrackIndex) * 397 + itemIndex;
        }
    }
}
