// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Stable 64-bit identity of a clip (architecture section 3.4). Ids are random (folded GUIDs
    /// assigned at asset creation), never name-derived, so renames, reorders, and asset moves can
    /// never change identity. The value 0 is reserved and means "none/invalid". Commands carry a
    /// <see cref="ClipId"/>; resolution to a dense blob index happens once per Play/Queue command
    /// via binary search over <see cref="ClipRegistryBlob.sortedClipIds"/> (see
    /// <see cref="ClipRegistryUtil.TryResolveClip"/>).
    /// </summary>
    public readonly struct ClipId : IEquatable<ClipId>, IComparable<ClipId>
    {
        /// <summary>Raw 64-bit id value. 0 is reserved as "none/invalid".</summary>
        public readonly ulong Value;

        /// <summary>Wraps a raw 64-bit id value.</summary>
        /// <param name="value">The raw id value; 0 means "none/invalid".</param>
        public ClipId(ulong value)
        {
            Value = value;
        }

        /// <summary>True when this id refers to a clip; false for the reserved 0 value.</summary>
        public bool IsValid => Value != 0;

        /// <summary>Value equality on the raw 64-bit id.</summary>
        /// <param name="other">The id to compare against.</param>
        public bool Equals(ClipId other)
        {
            return Value == other.Value;
        }

        /// <summary>Boxing overload of value equality; false for non-<see cref="ClipId"/> operands.</summary>
        /// <param name="obj">The object to compare against.</param>
        public override bool Equals(object obj)
        {
            return obj is ClipId otherClipId && Equals(otherClipId);
        }

        /// <summary>Hash of the raw id value.</summary>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <summary>
        /// Unsigned ordering on the raw id value — the ordering used by the sorted id arrays the
        /// binary-search resolve contract depends on (architecture section 4.3).
        /// </summary>
        /// <param name="other">The id to compare against.</param>
        public int CompareTo(ClipId other)
        {
            return Value.CompareTo(other.Value);
        }

        /// <summary>Value equality operator.</summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        public static bool operator ==(ClipId left, ClipId right)
        {
            return left.Value == right.Value;
        }

        /// <summary>Value inequality operator.</summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        public static bool operator !=(ClipId left, ClipId right)
        {
            return left.Value != right.Value;
        }

        /// <summary>
        /// Sixteen-digit uppercase hexadecimal rendering of the id — the read-only format the
        /// inspectors draw (architecture section 3.4). Managed; never call from Burst-compiled code.
        /// </summary>
        public override string ToString()
        {
            return Value.ToString("X16");
        }
    }
}
