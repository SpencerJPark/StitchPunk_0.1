// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit.Editor
{
    public enum TimelineTrackKind : byte
    {
        Transform = 0,
        Sprite = 1,
        Event = 2,

        // A full 3D local TRS per key bound to a bone by name, separate from Transform since a
        // cutout part needs one rotation axis where a joint needs a quaternion.
        Bone = 3
    }

    /// <summary>
    /// Identifies one key by position rather than by reference: the keys are plain serializable
    /// structs inside lists, so an undo, re-sort, or delete replaces the instances wholesale and a
    /// reference-holding selection would go stale silently.
    /// </summary>
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
