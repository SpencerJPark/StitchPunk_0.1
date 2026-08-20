// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Stable 32-bit identity of a billboard root — a rig node that turns to face the viewer, and
    /// the pivot every node beneath it inherits (amendment A44).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-rig scope makes 32 bits ample, matching <see cref="TargetId"/> and <see cref="SocketId"/>,
    /// and all three draw from the same generator. A collision across the three kinds is harmless
    /// because each is looked up in its own array, and one generator is one fewer thing to keep in
    /// step.
    /// </para>
    /// <para>
    /// <strong>This is the root's own identity, not the identity of the node it addresses.</strong>
    /// A billboard root names the node it turns through a <c>BillboardNodeAddress</c> — a target id
    /// or a hierarchy path — and that address is editable. Clip billboard tracks bind to <em>this</em>
    /// id instead, so re-pointing a root at a different node keeps every clip that keys it intact.
    /// Binding tracks to the addressed node would have made "billboard the torso instead of the hips"
    /// silently orphan the animation authored against it.
    /// </para>
    /// </remarks>
    public readonly struct BillboardRootId : IEquatable<BillboardRootId>, IComparable<BillboardRootId>
    {
        /// <summary>Raw 32-bit id value. 0 is reserved as "none/invalid".</summary>
        public readonly uint Value;

        /// <summary>Wraps a raw 32-bit id value.</summary>
        /// <param name="value">The raw id value; 0 means "none/invalid".</param>
        public BillboardRootId(uint value)
        {
            Value = value;
        }

        /// <summary>True when this id refers to a billboard root; false for the reserved 0 value.</summary>
        public bool IsValid => Value != 0;

        /// <summary>Value equality on the raw 32-bit id.</summary>
        public bool Equals(BillboardRootId other)
        {
            return Value == other.Value;
        }

        /// <summary>Boxing overload of value equality; false for non-<see cref="BillboardRootId"/> operands.</summary>
        public override bool Equals(object obj)
        {
            return obj is BillboardRootId && Equals((BillboardRootId)obj);
        }

        /// <summary>Hash of the raw id.</summary>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <summary>Ordinal comparison on the raw id — the sort order the baked registry uses.</summary>
        public int CompareTo(BillboardRootId other)
        {
            return Value.CompareTo(other.Value);
        }

        /// <summary>Renders the raw id for logs.</summary>
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
