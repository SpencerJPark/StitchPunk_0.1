// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Ragdoll tab's right column: the selected body's collider, mass, damping and
    /// limits, then the rig-wide ragdoll settings.</summary>
    public sealed class RagdollInspectorColumn : VisualElement
    {
        public event Action BodyEdited;

        public void SetRig(RigAsset rig)
        {
        }

        public void SetSelectedBodyId(uint bodyId)
        {
        }

        public void Refresh()
        {
        }
    }
}
