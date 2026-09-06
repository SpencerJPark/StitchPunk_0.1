// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Stable 32-bit identity of a rig socket — an attachment point. Ids are random, assigned when
    /// the socket row is created, never derived from its display name or list position.
    /// </summary>
    public readonly struct SocketId : IEquatable<SocketId>, IComparable<SocketId>
    {
        public readonly uint Value; // 0 is reserved as "none/invalid"

        public SocketId(uint value)
        {
            Value = value;
        }

        public bool IsValid => Value != 0;

        public bool Equals(SocketId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is SocketId && Equals((SocketId)obj);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public int CompareTo(SocketId other)
        {
            return Value.CompareTo(other.Value);
        }

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
