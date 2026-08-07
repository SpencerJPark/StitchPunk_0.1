// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>Which kind of track a key belongs to.</summary>
    public enum TimelineTrackKind : byte
    {
        Transform = 0,
        Sprite = 1,
        Event = 2
    }

    /// <summary>
    /// Identifies one key by position rather than by reference (architecture section 7.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Addresses, not object references, because the keys are plain serializable structs inside
    /// lists — an undo, a re-sort, or a delete replaces the instances wholesale. A selection holding
    /// references would survive as stale copies pointing at values nothing shows any more; a
    /// selection holding addresses is either still valid or obviously out of range.
    /// </para>
    /// </remarks>
    public readonly struct KeyAddress : IEquatable<KeyAddress>
    {
        public readonly TimelineTrackKind trackKind;
        public readonly int trackIndex;
        public readonly int keyIndex;

        public KeyAddress(TimelineTrackKind trackKind, int trackIndex, int keyIndex)
        {
            this.trackKind = trackKind;
            this.trackIndex = trackIndex;
            this.keyIndex = keyIndex;
        }

        public bool Equals(KeyAddress other)
        {
            return trackKind == other.trackKind
                && trackIndex == other.trackIndex
                && keyIndex == other.keyIndex;
        }

        public override bool Equals(object other)
        {
            return other is KeyAddress && Equals((KeyAddress)other);
        }

        public override int GetHashCode()
        {
            return ((int)trackKind * 397 + trackIndex) * 397 + keyIndex;
        }
    }
}
