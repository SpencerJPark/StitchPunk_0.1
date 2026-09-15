// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Ragdoll tab: a bodies column, the viewport with its Drop transport, and the
    /// selected body's inspector, all over the shared Rig.</summary>
    public sealed class RagdollPanel : VisualElement, IDisposable
    {
        public RigAsset SelectedRig
        {
            get { return null; }
        }

        public uint SelectedBodyId
        {
            get { return 0u; }
        }

        public void Bind(ActiveAssetSelection sharedSelection)
        {
        }

        public void Bind(RigAsset rig)
        {
        }

        public void Dispose()
        {
        }
    }
}
