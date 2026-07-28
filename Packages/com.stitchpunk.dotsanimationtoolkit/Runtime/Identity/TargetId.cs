// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Stable 32-bit identity of a rig target (architecture section 3.4). Per-rig scope makes
    /// 32 bits ample; ids are random (truncated folded GUIDs assigned when the target row is
    /// created) and never derived from the target's display name or list position. The value 0 is
    /// reserved and means "none/invalid". Targets are resolved to dense indices at bind/bake time
    /// only (see <see cref="ClipRegistryUtil.ResolveTargetIndex"/>); per-frame code always uses the
    /// cached dense index.
    /// </summary>
    public readonly struct TargetId : IEquatable<TargetId>, IComparable<TargetId>
    {
        /// <summary>Raw 32-bit id value. 0 is reserved as "none/invalid".</summary>
        public readonly uint Value;

        /// <summary>Wraps a raw 32-bit id value.</summary>
        /// <param name="value">The raw id value; 0 means "none/invalid".</param>
        public TargetId(uint value)
        {
            Value = value;
        }

        /// <summary>True when this id refers to a rig target; false for the reserved 0 value.</summary>
        public bool IsValid => Value != 0;

        /// <summary>Value equality on the raw 32-bit id.</summary>
        /// <param name="other">The id to compare against.</param>
        public bool Equals(TargetId other)
        {
            return Value == other.Value;
        }

        /// <summary>Boxing overload of value equality; false for non-<see cref="TargetId"/> operands.</summary>
        /// <param name="obj">The object to compare against.</param>
        public override bool Equals(object obj)
        {
            return obj is TargetId otherTargetId && Equals(otherTargetId);
        }

        /// <summary>Hash of the raw id value.</summary>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <summary>
        /// Unsigned ordering on the raw id value — the ordering used by
        /// <see cref="ClipRegistryBlob.sortedTargetIds"/> for the binary-search resolve contract
        /// (architecture section 4.3).
        /// </summary>
        /// <param name="other">The id to compare against.</param>
        public int CompareTo(TargetId other)
        {
            return Value.CompareTo(other.Value);
        }

        /// <summary>Value equality operator.</summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        public static bool operator ==(TargetId left, TargetId right)
        {
            return left.Value == right.Value;
        }

        /// <summary>Value inequality operator.</summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        public static bool operator !=(TargetId left, TargetId right)
        {
            return left.Value != right.Value;
        }

        /// <summary>
        /// Eight-digit uppercase hexadecimal rendering of the id — the read-only format the
        /// inspectors draw (architecture section 3.4). Managed; never call from Burst-compiled code.
        /// </summary>
        public override string ToString()
        {
            return Value.ToString("X8");
        }
    }
}
