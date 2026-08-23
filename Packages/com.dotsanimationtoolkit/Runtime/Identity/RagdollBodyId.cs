// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Stable 32-bit identity of a ragdoll body — a box collider welded to a rig node, addressed by
    /// hierarchy rather than by an authored parent reference (Phase D, amendment A50).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-rig scope makes 32 bits ample, matching <see cref="TargetId"/>, <see cref="SocketId"/>
    /// and <see cref="BillboardRootId"/>, and all four draw from the same generator. A collision
    /// across the four kinds is harmless because each is looked up in its own array, and one
    /// generator is one fewer thing to keep in step.
    /// </para>
    /// <para>
    /// <strong>This is the body's own identity, not the identity of the node it addresses.</strong>
    /// A ragdoll body names the node it is welded to through a <c>RigNodeAddress</c> — a target id,
    /// a hierarchy path, or a bone name — and that address is editable. The runtime <c>RagdollBody</c>
    /// buffer element (Phase D3) is addressed by <em>this</em> id, never by buffer position, so
    /// re-pointing a body at a different node, or reordering the authored list, cannot silently
    /// re-target a launch impulse or a per-body debug overlay aimed at it.
    /// </para>
    /// </remarks>
    public readonly struct RagdollBodyId : IEquatable<RagdollBodyId>, IComparable<RagdollBodyId>
    {
        /// <summary>Raw 32-bit id value. 0 is reserved as "none/invalid".</summary>
        public readonly uint Value;

        /// <summary>Wraps a raw 32-bit id value.</summary>
        /// <param name="value">The raw id value; 0 means "none/invalid".</param>
        public RagdollBodyId(uint value)
        {
            Value = value;
        }

        /// <summary>True when this id refers to a ragdoll body; false for the reserved 0 value.</summary>
        public bool IsValid => Value != 0;

        /// <summary>Value equality on the raw 32-bit id.</summary>
        public bool Equals(RagdollBodyId other)
        {
            return Value == other.Value;
        }

        /// <summary>Boxing overload of value equality; false for non-<see cref="RagdollBodyId"/> operands.</summary>
        public override bool Equals(object obj)
        {
            return obj is RagdollBodyId && Equals((RagdollBodyId)obj);
        }

        /// <summary>Hash of the raw id.</summary>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <summary>Ordinal comparison on the raw id — the sort order the baked registry uses.</summary>
        public int CompareTo(RagdollBodyId other)
        {
            return Value.CompareTo(other.Value);
        }

        /// <summary>Renders the raw id for logs.</summary>
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
