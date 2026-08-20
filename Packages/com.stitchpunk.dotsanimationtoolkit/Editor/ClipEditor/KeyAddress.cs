// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Which kind of track a key belongs to.</summary>
    public enum TimelineTrackKind : byte
    {
        Transform = 0,
        Sprite = 1,
        Event = 2,

        /// <summary>
        /// An authored skeleton track (amendment A42) — a full 3D local TRS per key, bound to a
        /// bone by name.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Transform"/> because the two carry genuinely different keys:
        /// a cutout part needs one rotation axis, a joint needs a quaternion. Sharing a lane kind
        /// would mean a lane could not tell which key type it addresses.
        /// </remarks>
        Bone = 3
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
