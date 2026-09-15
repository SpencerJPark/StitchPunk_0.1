// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The ragdoll viewport: the previewed rig with its body boxes, the box-handle drags,
    /// and a Drop / Reset transport over the chosen scenery.</summary>
    public sealed class RagdollViewportElement : VisualElement, IDisposable
    {
        public event Action<uint> BodyPicked;
        public event Action BodyBoxEdited;

        public uint SelectedBodyId
        {
            get { return 0u; }
        }

        public bool IsDropping
        {
            get { return false; }
        }

        public string StatusMessage
        {
            get { return string.Empty; }
        }

        public void Show(RigAsset rig)
        {
        }

        public void SetSelectedBodyId(uint bodyId)
        {
        }

        public void SetRestPoseSource(ClipSetAsset clipSet, ClipAsset clip, float normalizedTime)
        {
        }

        public void Drop()
        {
        }

        public void ResetDrop()
        {
        }

        public void Dispose()
        {
        }
    }
}
