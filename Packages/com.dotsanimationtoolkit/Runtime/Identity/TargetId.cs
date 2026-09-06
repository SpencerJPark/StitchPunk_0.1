// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Stable 32-bit identity of a rig target. Ids are random (truncated folded GUIDs assigned
    /// when the target row is created), never derived from display name or list position. Targets
    /// resolve to dense indices at bind/bake time only; per-frame code uses the cached index.
    /// </summary>
    public readonly struct TargetId : IEquatable<TargetId>, IComparable<TargetId>
    {
        public readonly uint Value; // 0 is reserved as "none/invalid"

        public TargetId(uint value)
        {
            Value = value;
        }

        public bool IsValid => Value != 0;

        public bool Equals(TargetId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is TargetId otherTargetId && Equals(otherTargetId);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public int CompareTo(TargetId other)
        {
            return Value.CompareTo(other.Value);
        }

        public static bool operator ==(TargetId left, TargetId right)
        {
            return left.Value == right.Value;
        }

        public static bool operator !=(TargetId left, TargetId right)
        {
            return left.Value != right.Value;
        }

        // Hex rendering for inspectors. Managed; never call from Burst-compiled code.
        public override string ToString()
        {
            return Value.ToString("X8");
        }
    }
}
