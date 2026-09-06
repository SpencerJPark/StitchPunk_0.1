// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Per-actor link between the baked registry and its VAT textures. The primary GPU binding is
    /// material-level (shared material, shared batch); this component exists for bake-time
    /// validation and for advanced hosts building materials at runtime.
    /// </summary>
    public struct VatTextureBinding : IComponentData
    {
        public ulong setKey; // matches ClipRegistryBlob.vatSetKey

        public UnityObjectRef<Texture2D> boneOrPositionTexture; // bone texture (bone flavor) or position texture (vertex flavor)

        public UnityObjectRef<Texture2D> normalTexture; // optional, vertex flavor only; default (null-equivalent) when absent
    }
}
