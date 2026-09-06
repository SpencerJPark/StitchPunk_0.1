// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Stable 32-bit identity of a ragdoll body — a box collider welded to a rig node, addressed by
    /// hierarchy rather than by an authored parent reference. Runtime buffer elements are addressed
    /// by this id, never by buffer position, so reordering the authored list can't retarget an impulse.
    /// </summary>
    public readonly struct RagdollBodyId : IEquatable<RagdollBodyId>, IComparable<RagdollBodyId>
    {
        public readonly uint Value; // 0 is reserved as "none/invalid"

        public RagdollBodyId(uint value)
        {
            Value = value;
        }

        public bool IsValid => Value != 0;

        public bool Equals(RagdollBodyId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is RagdollBodyId && Equals((RagdollBodyId)obj);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public int CompareTo(RagdollBodyId other)
        {
            return Value.CompareTo(other.Value);
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(RagdollBodyId left, RagdollBodyId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RagdollBodyId left, RagdollBodyId right)
        {
            return !left.Equals(right);
        }
    }
}
