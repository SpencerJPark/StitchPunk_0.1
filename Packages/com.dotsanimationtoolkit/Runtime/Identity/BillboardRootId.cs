// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Stable 32-bit identity of a billboard root — a rig node that turns to face the viewer, and
    /// the pivot every node beneath it inherits. Clip billboard tracks bind to this id rather than
    /// to the node it addresses, so re-pointing the root at a different node keeps clips intact.
    /// </summary>
    public readonly struct BillboardRootId : IEquatable<BillboardRootId>, IComparable<BillboardRootId>
    {
        public readonly uint Value; // 0 is reserved as "none/invalid"

        public BillboardRootId(uint value)
        {
            Value = value;
        }

        public bool IsValid => Value != 0;

        public bool Equals(BillboardRootId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is BillboardRootId && Equals((BillboardRootId)obj);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public int CompareTo(BillboardRootId other)
        {
            return Value.CompareTo(other.Value);
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(BillboardRootId left, BillboardRootId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BillboardRootId left, BillboardRootId right)
        {
            return !left.Equals(right);
        }
    }
}
