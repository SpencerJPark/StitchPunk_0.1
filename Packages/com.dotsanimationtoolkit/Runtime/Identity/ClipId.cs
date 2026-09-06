// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Stable 64-bit identity of a clip. Ids are random (folded GUIDs assigned at asset creation),
    /// never name-derived, so renames, reorders, and asset moves never change identity.
    /// </summary>
    public readonly struct ClipId : IEquatable<ClipId>, IComparable<ClipId>
    {
        public readonly ulong Value; // 0 is reserved as "none/invalid"

        public ClipId(ulong value)
        {
            Value = value;
        }

        public bool IsValid => Value != 0;

        public bool Equals(ClipId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is ClipId otherClipId && Equals(otherClipId);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public int CompareTo(ClipId other)
        {
            return Value.CompareTo(other.Value);
        }

        public static bool operator ==(ClipId left, ClipId right)
        {
            return left.Value == right.Value;
        }

        public static bool operator !=(ClipId left, ClipId right)
        {
            return left.Value != right.Value;
        }

        // Hex rendering for inspectors. Managed; never call from Burst-compiled code.
        public override string ToString()
        {
            return Value.ToString("X16");
        }
    }
}
