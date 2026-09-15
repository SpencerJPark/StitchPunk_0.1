// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Materials tab's detail column: one material's shader, users, contract properties, instancing and sheet findings.</summary>
    public sealed class MaterialInspectorColumn : VisualElement
    {
        public RigMaterialUsage BoundUsage { get; private set; }
        public ClipSetAsset BoundClipSet { get; private set; }

        public MaterialInspectorColumn()
        {
            style.flexGrow = 1f;
        }

        public void Bind(RigMaterialUsage usage, ClipSetAsset clipSet)
        {
            BoundUsage = usage;
            BoundClipSet = clipSet;
        }
    }
}
