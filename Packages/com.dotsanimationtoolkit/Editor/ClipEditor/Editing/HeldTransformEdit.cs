// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The transform value a part is holding but has not keyed — what Auto Key off leaves behind
    /// after a move — kept in an object so Unity's undo stack can reach it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>It is an object at all because the undo stack records objects.</strong> Held on the
    /// window as plain fields, a move made before keying was invisible to <c>Undo</c>: no asset had
    /// changed yet, so there was nothing recorded and nothing to take back — the one edit in the
    /// window Ctrl+Z could not reach. Recording this before each held move puts the move on the same
    /// stack as every other edit, in order beside them, rather than in a second undo system of the
    /// window's own.
    /// </para>
    /// <para>
    /// <strong>Vector3 rather than float3.</strong> Undo restores an object by its serialized state,
    /// so what is stored here has to be what Unity serializes. The window converts at the boundary,
    /// exactly as every other float3-to-Unity handoff in it does.
    /// </para>
    /// <para>
    /// Never an asset and never saved: this is per-window live state.
    /// <c>HideFlags.HideAndDontSave</c> keeps it out of the project and out of scenes, and the window
    /// destroys it when it closes — which leaves its undo entries pointing at a dead object, inert
    /// and harmless, since the window whose state they described has gone with it.
    /// </para>
    /// </remarks>
    internal sealed class HeldTransformEdit : ScriptableObject
    {
        /// <summary>Whether a part is holding an unkeyed value at all.</summary>
        public bool hasValue;

        /// <summary>The rig target holding it. Only one part holds a value at a time.</summary>
        public uint targetId;

        public Vector3 position;
        public Vector3 rotationDegrees;
        public Vector3 scale;
    }
}
