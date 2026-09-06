// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The transform value a part is holding but has not keyed — what Auto Key off leaves behind
    /// after a move — kept in an object so Unity's undo stack can reach it. Never saved: created
    /// with <c>HideFlags.HideAndDontSave</c> and destroyed with the window.
    /// </summary>
    internal sealed class HeldTransformEdit : ScriptableObject
    {
        /// <summary>Whether a part is holding an unkeyed value at all.</summary>
        public bool hasValue;

        /// <summary>The rig target holding it. Only one part holds a value at a time.</summary>
        public uint targetId;

        // Vector3, not float3: Undo restores by serialized state, so this must be what Unity serializes.
        public Vector3 position;
        public Vector3 rotationDegrees;
        public Vector3 scale;
    }
}
