// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Per-part link between a rig target and the VAT textures its own baked part uses. Unlike
    /// <see cref="VatTextureBinding"/> this resolves against the part's own <c>targetId</c>, so a
    /// multi-part VAT texture set binds each part to its own frames rather than one shared texture.
    /// </summary>
    public struct VatPartTextureBinding : IComponentData
    {
        public UnityObjectRef<Texture2D> boneOrPositionTexture; // bone texture (bone flavor) or position texture (vertex flavor)

        public UnityObjectRef<Texture2D> normalTexture; // optional, vertex flavor only; default (null-equivalent) when absent
    }
}
