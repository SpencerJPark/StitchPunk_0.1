// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One box-handle drag gesture on a ragdoll body, shared by the Ragdoll tab's viewport
    /// and the Clip Editor's own viewport so both drag the same way.</summary>
    public sealed class RagdollBoxDragSession
    {
        public RagdollBoxHandle ActiveHandle
        {
            get { return RagdollBoxHandle.None; }
        }

        public bool TryBegin(
            ClipPreviewController previewController,
            RigAsset rig,
            uint bodyId,
            Vector2 viewportPoint,
            float aspect)
        {
            return false;
        }

        public void Continue(
            ClipPreviewController previewController,
            RigAsset rig,
            Vector2 viewportPoint,
            float aspect,
            bool symmetric)
        {
        }

        public bool End(ClipPreviewController previewController, RigAsset rig)
        {
            return false;
        }
    }
}
