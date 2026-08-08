// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Stable 32-bit identity of a rig socket — an attachment point (architecture section 3.4's
    /// identity scheme, extended to sockets).
    /// </summary>
    /// <remarks>
    /// Per-rig scope makes 32 bits ample, matching <see cref="TargetId"/>. Ids are random, assigned
    /// when the socket row is created, and never derived from the socket's display name or list
    /// position — renaming "hand_r" to "RightHand" or reordering the list must not silently
    /// re-point every sword in the game at a different bone. 0 is reserved and means
    /// "none/invalid".
    /// </remarks>
    public readonly struct SocketId : IEquatable<SocketId>, IComparable<SocketId>
    {
        /// <summary>Raw 32-bit id value. 0 is reserved as "none/invalid".</summary>
        public readonly uint Value;

        /// <summary>Wraps a raw 32-bit id value.</summary>
        /// <param name="value">The raw id value; 0 means "none/invalid".</param>
        public SocketId(uint value)
        {
            Value = value;
        }

        /// <summary>True when this id refers to a socket; false for the reserved 0 value.</summary>
        public bool IsValid => Value != 0;

        /// <summary>Value equality on the raw 32-bit id.</summary>
        public bool Equals(SocketId other)
        {
            return Value == other.Value;
        }

        /// <summary>Boxing overload of value equality; false for non-<see cref="SocketId"/> operands.</summary>
        public override bool Equals(object obj)
        {
            return obj is SocketId && Equals((SocketId)obj);
        }

        /// <summary>Hash of the raw id.</summary>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <summary>Ordinal comparison on the raw id — the sort order the baked registry uses.</summary>
        public int CompareTo(SocketId other)
        {
            return Value.CompareTo(other.Value);
        }

        /// <summary>Renders the raw id for logs.</summary>
        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(SocketId left, SocketId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SocketId left, SocketId right)
        {
            return !left.Equals(right);
        }
    }
}
