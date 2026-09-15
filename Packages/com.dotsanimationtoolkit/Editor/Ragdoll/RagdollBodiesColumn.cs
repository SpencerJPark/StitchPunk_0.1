// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Ragdoll tab's left column: every body on the rig, with add and delete.</summary>
    public sealed class RagdollBodiesColumn : VisualElement
    {
        public event Action<uint> BodySelected;
        public event Action RigBodiesChanged;

        public uint SelectedBodyId
        {
            get { return 0u; }
        }

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
